using EmbedIO;
using EmbedIO.WebSockets;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public class ApiServer {
        public const string BaseRoute = "/os/v1";

        private readonly IRemoteBackend backend;
        private readonly Func<string> token;
        private readonly string urlPrefix;
        private readonly Action<string> log;
        private WebServer server;
        private StatusSocket socket;
        private Timer heartbeat;
        private volatile bool enabled;

        public ApiServer(IRemoteBackend backend, string urlPrefix, Func<string> token, Action<string> log = null) {
            this.backend = backend;
            this.urlPrefix = urlPrefix;
            this.token = token;
            this.log = log;
        }

        public bool Running => server != null && enabled;

        public string Failed { get; private set; }

        public void Start() {
            enabled = true;
            if (server != null) {
                return;
            }
            socket = new StatusSocket(BaseRoute + "/ws", backend);
            server = new WebServer(o => o.WithUrlPrefix(urlPrefix).WithMode(HttpListenerMode.EmbedIO))
                .WithModule(new AuthModule(token, () => enabled))
                .WithModule(socket)
                .WithModule(new ApiModule(BaseRoute, backend, log));
            backend.Pushed += OnPushed;
            server.RunAsync(CancellationToken.None).ContinueWith(t => {
                Failed = t.Exception?.GetBaseException().Message ?? "stopped";
                enabled = false;
                log?.Invoke($"remote API on {urlPrefix} stopped: {Failed}");
            }, TaskContinuationOptions.NotOnRanToCompletion);
            heartbeat = new Timer(_ => OnPushed("status", null), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        private void OnPushed(string type, object data) {
            var s = socket;
            if (s == null || !enabled) {
                return;
            }
            try {
                object payload = data ?? (type == "status" ? backend.Status() : null);
                var json = JsonSerializer.Serialize(new { type, data = payload }, Json.Options);
                _ = s.Broadcast(json);
            } catch (Exception e) {
                log?.Invoke($"api push failed: {e.Message}");
            }
        }

        public void Stop() {
            enabled = false;
        }

        public static bool TokenMatches(string header, string expected) {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(header)) {
                return false;
            }
            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            var given = Encoding.UTF8.GetBytes(header.Substring(prefix.Length).Trim());
            var want = Encoding.UTF8.GetBytes(expected);
            return given.Length == want.Length && CryptographicOperations.FixedTimeEquals(given, want);
        }

        private class AuthModule : WebModuleBase {
            private readonly Func<string> token;
            private readonly Func<bool> enabled;

            public AuthModule(Func<string> token, Func<bool> enabled) : base("/") {
                this.token = token;
                this.enabled = enabled;
            }

            public override bool IsFinalHandler => false;

            protected override async Task OnRequestAsync(IHttpContext context) {
                if (!enabled()) {
                    context.Response.StatusCode = 503;
                    await context.SendStringAsync("{\"error\":\"the API is off\"}", "application/json", Encoding.UTF8);
                    context.SetHandled();
                    return;
                }
                if (!TokenMatches(context.Request.Headers["Authorization"], token())) {
                    context.Response.StatusCode = 401;
                    await context.SendStringAsync("{\"error\":\"unauthorized\"}", "application/json", Encoding.UTF8);
                    context.SetHandled();
                }
            }
        }

        private class StatusSocket : WebSocketModule {
            private readonly IRemoteBackend backend;

            public StatusSocket(string path, IRemoteBackend backend) : base(path, true) {
                this.backend = backend;
            }

            public Task Broadcast(string json) {
                return BroadcastAsync(json);
            }

            protected override Task OnClientConnectedAsync(IWebSocketContext context) {
                var json = JsonSerializer.Serialize(new { type = "status", data = backend.Status() }, Json.Options);
                return SendAsync(context, json);
            }

            protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result) {
                return Task.CompletedTask;
            }
        }

        private class ApiModule : WebModuleBase {
            private readonly IRemoteBackend backend;
            private readonly Action<string> log;

            public ApiModule(string baseRoute, IRemoteBackend backend, Action<string> log) : base(baseRoute) {
                this.backend = backend;
                this.log = log;
            }

            public override bool IsFinalHandler => true;

            protected override async Task OnRequestAsync(IHttpContext context) {
                var path = context.RequestedPath.TrimEnd('/');
                var method = context.Request.HttpVerb;
                try {
                    if (path == "/status" && method == HttpVerbs.Get) {
                        await Send(context, 200, backend.Status());
                    } else if (path == "/version" && method == HttpVerbs.Get) {
                        await Send(context, 200, new { version = backend.Status().Version });
                    } else if (path == "/commands" && method == HttpVerbs.Post) {
                        var body = await context.GetRequestBodyAsStringAsync();
                        var env = JsonSerializer.Deserialize<CommandEnvelope>(body, CommandJson.Options);
                        if (env == null || string.IsNullOrWhiteSpace(env.Id) || string.IsNullOrWhiteSpace(env.Kind)) {
                            await Send(context, 400, new { error = "id and kind are required" });
                            return;
                        }
                        await Send(context, 200, backend.Submit(env));
                    } else if (path.StartsWith("/commands/") && method == HttpVerbs.Get) {
                        var r = backend.Get(Uri.UnescapeDataString(path.Substring("/commands/".Length)));
                        if (r == null) {
                            await Send(context, 404, new { error = "unknown command" });
                        } else {
                            await Send(context, 200, r);
                        }
                    } else if (path.StartsWith("/commands/") && method == HttpVerbs.Delete) {
                        await Send(context, 200, backend.Cancel(Uri.UnescapeDataString(path.Substring("/commands/".Length))));
                    } else if (path == "/preview" && method == HttpVerbs.Get) {
                        var req = new PreviewRequest();
                        var start = context.Request.QueryString["start"];
                        if (!string.IsNullOrEmpty(start) && DateTime.TryParse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var s)) {
                            req.Start = s;
                        }
                        await Send(context, 200, backend.Preview(req));
                    } else if (path == "/preview" && method == HttpVerbs.Post) {
                        var body = await context.GetRequestBodyAsStringAsync();
                        var req = string.IsNullOrWhiteSpace(body) ? new PreviewRequest() : JsonSerializer.Deserialize<PreviewRequest>(body, CommandJson.Options);
                        await Send(context, 200, backend.Preview(req ?? new PreviewRequest()));
                    } else {
                        await Send(context, 404, new { error = "not found" });
                    }
                } catch (JsonException e) {
                    await Send(context, 400, new { error = e.Message });
                } catch (Exception e) {
                    log?.Invoke($"api {method} {path} failed: {e}");
                    await Send(context, 500, new { error = e.Message });
                }
            }

            private static Task Send(IHttpContext context, int status, object body) {
                context.Response.StatusCode = status;
                return context.SendStringAsync(JsonSerializer.Serialize(body, Json.Options), "application/json", new UTF8Encoding(false));
            }
        }
    }

    public static class Json {

        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new UtcDateTimeConverter() },
        };
    }

    public class UtcDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime> {

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            return reader.GetDateTime().ToUniversalTime();
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) {
            var utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
