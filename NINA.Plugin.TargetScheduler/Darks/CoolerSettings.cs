using System;
using System.IO;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class CoolerSettings {
        private readonly object sync = new object();
        private readonly string path;
        private CoolerSettingsData data;

        public event Action Changed;

        public CoolerSettings(string path) {
            this.path = path;
            data = Read(path);
        }

        private static CoolerSettingsData Read(string path) {
            try {
                if (path != null && File.Exists(path)) {
                    return JsonSerializer.Deserialize<CoolerSettingsData>(File.ReadAllText(path)) ?? new CoolerSettingsData();
                }
            } catch (Exception) {
                return new CoolerSettingsData();
            }
            return new CoolerSettingsData();
        }

        public CoolerSettingsData Snapshot() {
            lock (sync) {
                return data.Copy();
            }
        }

        /// <summary>Applies a change and saves it, unless the result is inconsistent; returns the problem then.</summary>
        public string Update(Action<CoolerSettingsData> change) {
            lock (sync) {
                CoolerSettingsData next = data.Copy();
                change(next);
                string problem = next.Problem();
                if (problem != null) {
                    return problem;
                }
                data = next;
                if (path != null) {
                    try {
                        string tmp = path + ".tmp";
                        File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                        File.Move(tmp, path, true);
                    } catch (Exception ex) {
                        return $"the settings could not be saved: {ex.Message}";
                    }
                }
            }
            Changed?.Invoke();
            return null;
        }
    }
}
