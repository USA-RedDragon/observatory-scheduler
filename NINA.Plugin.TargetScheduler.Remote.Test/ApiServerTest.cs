using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class ApiServerTest {
        private FakeBackend backend;
        private ApiServer server;
        private HttpClient http;
        private int port;

        private class FakeBackend : IRemoteBackend {
            public List<CommandEnvelope> Submitted { get; } = new List<CommandEnvelope>();
            public PreviewRequest LastPreview { get; private set; }

            public event Action<string, object> Pushed;

            public Status Status() => new Status { Version = "5.8.2.101", Time = new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc), State = "imaging", WebEditing = true };

            public CommandResult Submit(CommandEnvelope command) {
                Submitted.Add(command);
                return CommandResult.Of(command.Id, CommandStatus.Pending);
            }

            public CommandResult Get(string id) => id == "known" ? CommandResult.Of(id, CommandStatus.Applied) : null;

            public CommandResult Cancel(string id) => CommandResult.Of(id, CommandStatus.Cancelled);

            public Preview Preview(PreviewRequest request) {
                LastPreview = request;
                return new Preview { Start = request.Start ?? DateTime.UtcNow };
            }

            public void Push(string type, object data) => Pushed?.Invoke(type, data);
        }

        private static int FreePort() {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        [SetUp]
        public void SetUp() {
            backend = new FakeBackend();
            port = FreePort();
            server = new ApiServer(backend, $"http://127.0.0.1:{port}/", () => "s3cret");
            server.Start();
            http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Add("Authorization", "Bearer s3cret");
            for (int i = 0; i < 50; i++) {
                try {
                    using var c = new TcpClient("127.0.0.1", port);
                    break;
                } catch (SocketException) {
                    Thread.Sleep(50);
                }
            }
        }

        [TearDown]
        public void TearDown() {
            http.Dispose();
            server.Stop();
        }

        [Test]
        public async Task RejectsMissingOrWrongToken() {
            using var anon = new HttpClient { BaseAddress = http.BaseAddress };
            (await anon.GetAsync("/os/v1/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            anon.DefaultRequestHeaders.Add("Authorization", "Bearer nope");
            (await anon.GetAsync("/os/v1/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ServesStatusWithUtcTimes() {
            var body = await http.GetStringAsync("/os/v1/status");
            body.Should().Contain("\"version\":\"5.8.2.101\"").And.Contain("\"time\":\"2026-10-10T06:00:00.000Z\"").And.NotContain("\"target\":");
        }

        [Test]
        public async Task CommandsRoundTrip() {
            var env = "{\"id\":\"c1\",\"kind\":\"scheduler.replan\",\"payload\":{},\"author\":\"web\",\"created_at\":\"2026-10-10T06:00:00Z\"}";
            var res = await http.PostAsync("/os/v1/commands", new StringContent(env, Encoding.UTF8, "application/json"));
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            (await res.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"pending\"");
            backend.Submitted.Should().ContainSingle().Which.Kind.Should().Be("scheduler.replan");

            (await http.GetAsync("/os/v1/commands/known")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await http.GetAsync("/os/v1/commands/unknown")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await (await http.DeleteAsync("/os/v1/commands/c1")).Content.ReadAsStringAsync()).Should().Contain("cancelled");

            var bad = await http.PostAsync("/os/v1/commands", new StringContent("{\"kind\":\"x\"}", Encoding.UTF8, "application/json"));
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var junk = await http.PostAsync("/os/v1/commands", new StringContent("{nope", Encoding.UTF8, "application/json"));
            junk.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task PreviewAcceptsOverrides() {
            var req = "{\"start\":\"2026-10-10T01:00:00Z\",\"overrides\":[{\"entity\":\"project\",\"id\":5,\"field\":\"priority\",\"value\":2}]}";
            (await http.PostAsync("/os/v1/preview", new StringContent(req, Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.OK);
            backend.LastPreview.Overrides.Should().ContainSingle().Which.Value.Should().Be(2);
            backend.LastPreview.Start.Should().Be(new DateTime(2026, 10, 10, 1, 0, 0, DateTimeKind.Utc));
            (await http.GetAsync("/os/v1/preview?start=2026-10-10T02:00:00Z")).StatusCode.Should().Be(HttpStatusCode.OK);
            backend.LastPreview.Start.Should().Be(new DateTime(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public async Task StoppedServerAnswers503AndRestarts() {
            server.Stop();
            (await http.GetAsync("/os/v1/status")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            server.Start();
            (await http.GetAsync("/os/v1/status")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public void PortInUseIsReported() {
            var messages = new List<string>();
            var blocker = new TcpListener(IPAddress.Loopback, 0);
            blocker.Start();
            var taken = ((IPEndPoint)blocker.LocalEndpoint).Port;
            var clash = new ApiServer(new FakeBackend(), $"http://127.0.0.1:{taken}/", () => "x", m => { lock (messages) { messages.Add(m); } });
            clash.Start();
            for (int i = 0; i < 100 && clash.Running; i++) {
                Thread.Sleep(50);
            }
            clash.Running.Should().BeFalse();
            clash.Failed.Should().NotBeNullOrEmpty();
            lock (messages) {
                messages.Should().Contain(m => m.Contains("stopped"));
            }
            blocker.Stop();
        }

        [Test]
        public async Task UnknownPathIs404() {
            (await http.GetAsync("/os/v1/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Test]
        public async Task WebSocketSendsStatusThenPushes() {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", "Bearer s3cret");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/os/v1/ws"), cts.Token);
            (await Receive(ws, cts.Token)).Should().Contain("\"type\":\"status\"");
            backend.Push("result", CommandResult.Of("c9", CommandStatus.Applied));
            var msg = await Receive(ws, cts.Token);
            msg.Should().Contain("\"type\":\"result\"").And.Contain("c9");
        }

        [Test]
        public async Task WebSocketNeedsTheToken() {
            using var ws = new ClientWebSocket();
            Func<Task> connect = () => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/os/v1/ws"), CancellationToken.None);
            await connect.Should().ThrowAsync<WebSocketException>();
        }

        [TestCase("Bearer abc", "abc", true)]
        [TestCase("bearer abc", "abc", true)]
        [TestCase("Bearer abd", "abc", false)]
        [TestCase("abc", "abc", false)]
        [TestCase(null, "abc", false)]
        [TestCase("Bearer ", "", false)]
        public void TokenMatching(string header, string expected, bool ok) {
            ApiServer.TokenMatches(header, expected).Should().Be(ok);
        }

        private static async Task<string> Receive(ClientWebSocket ws, CancellationToken token) {
            var buf = new byte[65536];
            var sb = new StringBuilder();
            WebSocketReceiveResult r;
            do {
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token);
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            } while (!r.EndOfMessage);
            return sb.ToString();
        }
    }
}
