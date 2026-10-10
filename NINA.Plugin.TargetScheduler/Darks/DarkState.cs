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
        public List<CoolerHold> Holds { get; set; } = new List<CoolerHold>();
        public List<CoolerLimit> Limits { get; set; } = new List<CoolerLimit>();
        public string LearningNight { get; set; }
        public DateTime? LearningAt { get; set; }
        public double? LearningAmbient { get; set; }
    }

    public class DarkState {
        public const int MaxEvidence = 200;

        public static readonly TimeSpan LimitMaxAge = TimeSpan.FromDays(30);

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
            if (limit.Ambient.HasValue) {
                Update(d => {
                    d.Limits ??= new List<CoolerLimit>();
                    d.Holds ??= new List<CoolerHold>();
                    d.Holds.RemoveAll(h => Contradicts(h, limit));
                    d.Limits.RemoveAll(l => l.Ambient.HasValue && DarkPlanner.SameSetpoint(l.SetTemp, limit.SetTemp) && Math.Abs(l.Ambient.Value - limit.Ambient.Value) < 0.5);
                    d.Limits.Add(limit);
                    Trim(d.Limits, l => l.At ?? DateTime.MinValue);
                });
                return;
            }

            lock (sync) {
                limits.RemoveAll(l => DarkPlanner.SameSetpoint(l.SetTemp, limit.SetTemp));
                limits.Add(limit);
            }
        }

        public IList<CoolerLimit> Limits(bool includeKept = true) {
            DateTime oldest = DateTime.UtcNow - LimitMaxAge;
            lock (sync) {
                IEnumerable<CoolerLimit> kept = includeKept
                    ? (data.Limits ?? Enumerable.Empty<CoolerLimit>()).Where(l => !l.At.HasValue || l.At.Value >= oldest)
                    : Enumerable.Empty<CoolerLimit>();
                return limits.Concat(kept).ToList();
            }
        }

        public void RecordHold(CoolerHold hold) {
            Update(d => {
                d.Limits ??= new List<CoolerLimit>();
                d.Holds ??= new List<CoolerHold>();
                d.Limits.RemoveAll(l => Contradicts(hold, l));
                d.Holds.RemoveAll(h => DarkPlanner.SameSetpoint(h.SetTemp, hold.SetTemp) && Math.Abs(h.Ambient - hold.Ambient) < 0.5);
                d.Holds.Add(hold);
                Trim(d.Holds, h => h.At);
            });
            lock (sync) {
                limits.RemoveAll(l => Contradicts(hold, l));
            }
        }

        public IList<CoolerHold> Holds() {
            lock (sync) {
                return (data.Holds ?? new List<CoolerHold>()).ToList();
            }
        }

        public DateTime? LearningUsed(string night) {
            lock (sync) {
                return data.LearningNight == night ? data.LearningAt : null;
            }
        }

        public void RecordLearning(string night, DateTime nowUtc, double ambient) {
            Update(d => {
                d.LearningNight = night;
                d.LearningAt = nowUtc;
                d.LearningAmbient = ambient;
            });
        }

        public static bool Contradicts(CoolerHold hold, CoolerLimit limit) {
            return limit.Ambient.HasValue && hold.Ambient >= limit.Ambient.Value - 0.05 && hold.SetTemp <= limit.SetTemp + 0.05;
        }

        private static void Trim<T>(List<T> list, Func<T, DateTime> at) {
            if (list.Count <= MaxEvidence) {
                return;
            }
            List<T> keep = list.OrderByDescending(at).Take(MaxEvidence).ToList();
            list.Clear();
            list.AddRange(keep);
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
