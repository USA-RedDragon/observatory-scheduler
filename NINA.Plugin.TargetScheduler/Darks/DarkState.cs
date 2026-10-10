using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class DarkStateData {
        public double? AfterWaitSeconds { get; set; }
        public DateTime? AfterWaitMeasuredAt { get; set; }
        public double? CoolingSecondsPerC { get; set; }
        public DateTime? CoolingMeasuredAt { get; set; }
        public double? WarmingSecondsPerC { get; set; }
        public DateTime? WarmingMeasuredAt { get; set; }
        public double? SaveSeconds { get; set; }
        public DateTime? SaveMeasuredAt { get; set; }
    }

    public class DarkState {
        private readonly object sync = new object();
        private readonly string path;
        private DarkStateData data;
        private readonly List<CoolerLimit> limits = new List<CoolerLimit>();

        public DarkState(string path) {
            this.path = path;
            data = Load(path);
        }

        private static DarkStateData Load(string path) {
            try {
                if (path != null && File.Exists(path)) {
                    return JsonSerializer.Deserialize<DarkStateData>(File.ReadAllText(path)) ?? new DarkStateData();
                }
            } catch (Exception) {
                return new DarkStateData();
            }

            return new DarkStateData();
        }

        public DarkStateData Snapshot() {
            lock (sync) {
                return JsonSerializer.Deserialize<DarkStateData>(JsonSerializer.Serialize(data));
            }
        }

        public CoolerRates Rates() {
            lock (sync) {
                return new CoolerRates { CoolingSecondsPerC = data.CoolingSecondsPerC, WarmingSecondsPerC = data.WarmingSecondsPerC };
            }
        }

        public TimeSpan? AfterWait() {
            lock (sync) {
                return data.AfterWaitSeconds.HasValue ? TimeSpan.FromSeconds(data.AfterWaitSeconds.Value) : (TimeSpan?)null;
            }
        }

        public TimeSpan? SaveTime() {
            lock (sync) {
                return data.SaveSeconds.HasValue ? TimeSpan.FromSeconds(data.SaveSeconds.Value) : (TimeSpan?)null;
            }
        }

        public void RecordAfterWait(TimeSpan took, DateTime nowUtc) {
            Update(d => {
                d.AfterWaitSeconds = took.TotalSeconds;
                d.AfterWaitMeasuredAt = nowUtc;
            });
        }

        public void RecordSave(TimeSpan took, DateTime nowUtc) {
            Update(d => {
                d.SaveSeconds = took.TotalSeconds;
                d.SaveMeasuredAt = nowUtc;
            });
        }

        public void RecordMove(double from, double to, TimeSpan took, DateTime nowUtc) {
            double? rate = DarkPlanner.SecondsPerC(from, to, took);
            if (!rate.HasValue) {
                return;
            }

            Update(d => {
                if (to < from) {
                    d.CoolingSecondsPerC = rate;
                    d.CoolingMeasuredAt = nowUtc;
                } else {
                    d.WarmingSecondsPerC = rate;
                    d.WarmingMeasuredAt = nowUtc;
                }
            });
        }

        public void AddLimit(CoolerLimit limit) {
            lock (sync) {
                limits.RemoveAll(l => DarkPlanner.SameSetpoint(l.SetTemp, limit.SetTemp));
                limits.Add(limit);
            }
        }

        public IList<CoolerLimit> Limits() {
            lock (sync) {
                return limits.ToList();
            }
        }

        public void ClearLimits() {
            lock (sync) {
                limits.Clear();
            }
        }

        private void Update(Action<DarkStateData> change) {
            lock (sync) {
                change(data);
                if (path == null) {
                    return;
                }

                try {
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                    File.Move(tmp, path, true);
                } catch (Exception) {
                    return;
                }
            }
        }
    }
}
