using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class RemoteSettingsData {

        [JsonPropertyName("api_enabled")]
        public bool ApiEnabled { get; set; }

        [JsonPropertyName("api_port")]
        public int ApiPort { get; set; } = 8189;

        [JsonPropertyName("api_token")]
        public string ApiToken { get; set; }

        [JsonPropertyName("web_editing")]
        public bool WebEditing { get; set; }

        [JsonPropertyName("skips")]
        public string Skips { get; set; }

        [JsonPropertyName("live_autofocus")]
        public bool LiveAutofocus { get; set; } = true;
    }

    public class RemoteSettings {
        private readonly object sync = new object();
        private readonly string path;
        private RemoteSettingsData data;

        public event Action Changed;

        public RemoteSettings(string path) {
            this.path = path;
            data = Read(path);
            if (string.IsNullOrEmpty(data.ApiToken)) {
                data.ApiToken = NewToken();
                Save();
            }
        }

        public static string NewToken() {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        }

        private static RemoteSettingsData Read(string path) {
            try {
                if (File.Exists(path)) {
                    return JsonSerializer.Deserialize<RemoteSettingsData>(File.ReadAllText(path), CommandJson.Options) ?? new RemoteSettingsData();
                }
            } catch (Exception) {
                return new RemoteSettingsData();
            }
            return new RemoteSettingsData();
        }

        public RemoteSettingsData Snapshot() {
            lock (sync) {
                return new RemoteSettingsData { ApiEnabled = data.ApiEnabled, ApiPort = data.ApiPort, ApiToken = data.ApiToken, WebEditing = data.WebEditing, Skips = data.Skips, LiveAutofocus = data.LiveAutofocus };
            }
        }

        public void Update(Action<RemoteSettingsData> change) {
            lock (sync) {
                change(data);
                if (data.ApiPort <= 0 || data.ApiPort > 65535) {
                    data.ApiPort = 8189;
                }
                Save();
            }
            Changed?.Invoke();
        }

        private void Save() {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) {
                Directory.CreateDirectory(dir);
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, true);
        }
    }
}
