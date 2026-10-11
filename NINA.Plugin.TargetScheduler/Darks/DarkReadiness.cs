using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class DarkReadyInput {
        public DateTime Now { get; set; }
        public Func<DateTime, double?> SunAltitudeAt { get; set; }
        public IList<DarkNeed> Queue { get; set; }
        public IList<BiasNeed> BiasQueue { get; set; }
        public bool WeatherConnected { get; set; }
        public double? Ambient { get; set; }
        public IList<CoolerHold> Holds { get; set; }
        public IList<CoolerLimit> Limits { get; set; }
        public TimeSpan? SaveTime { get; set; }
        public double? CoolingSecondsPerC { get; set; }
        public DateTime? LearningUsedAt { get; set; }
    }

    public class DarkReadyResult {
        public bool Ready { get; set; }
        public bool Learning { get; set; }
        public string Reason { get; set; }
        public DarkNeed Need { get; set; }
        public IList<BiasNeed> Bias { get; set; }
        public string Basis { get; set; }
    }

    public static class DarkReadiness {

        public static string NightOf(DateTime localTime) {
            return localTime.AddHours(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static CoolerHold Proof(IEnumerable<CoolerHold> holds, double ambient, double setTemp) {
            return (holds ?? Enumerable.Empty<CoolerHold>())
                .Where(h => h != null && h.Ambient >= ambient - 0.05 && h.Ambient - h.SetTemp >= ambient - setTemp - 0.05)
                .OrderByDescending(h => h.At)
                .FirstOrDefault();
        }

        public static TimeSpan CoolTime(double ambient, double setTemp, double? coolingSecondsPerC) {
            if (setTemp >= ambient) {
                return TimeSpan.Zero;
            }
            if (!coolingSecondsPerC.HasValue || coolingSecondsPerC.Value <= 0) {
                return DarkPlanner.UnmeasuredMoveHorizon;
            }
            return TimeSpan.FromSeconds((ambient - setTemp) * coolingSecondsPerC.Value);
        }

        public static DarkReadyResult Check(DarkReadyInput i) {
            DarkReadyResult darks = CheckDarks(i);
            if (darks.Ready) {
                return darks;
            }

            List<BiasNeed> bias = (i.BiasQueue ?? new List<BiasNeed>()).Where(n => n != null).ToList();
            if (bias.Count == 0) {
                return darks;
            }

            var w = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = i.Now, SunAltitudeAt = i.SunAltitudeAt };
            string sun = DarkPlanner.SunFitReason(w, TimeSpan.Zero, 0, i.SaveTime ?? TimeSpan.Zero);
            if (sun != null) {
                return No(string.Format(CultureInfo.InvariantCulture, "{0}; the bias frames cannot be taken either: {1}", darks.Reason, sun));
            }

            return new DarkReadyResult {
                Ready = true,
                Bias = bias,
                Basis = string.Format(CultureInfo.InvariantCulture,
                    "no darks can be taken now ({0}), but the stacker's bias backlog still needs {1}; bias frames need only the camera at its current setpoint, and the sun is below the light-safe limit of {2:0.0}°",
                    darks.Reason, BiasPlanner.Describe(bias), DarkPlanner.SunLimitDegrees)
            };
        }

        private static DarkReadyResult CheckDarks(DarkReadyInput i) {
            List<DarkNeed> queue = (i.Queue ?? new List<DarkNeed>()).Where(n => n != null).ToList();
            if (queue.Count == 0) {
                return No("no combo in the stacker's dark backlog still needs darks");
            }

            var w = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = i.Now, SunAltitudeAt = i.SunAltitudeAt };
            string sun = DarkPlanner.SunStopReason(w);
            if (sun != null) {
                return No(sun);
            }

            if (!i.WeatherConnected) {
                return No("no weather device is connected, so the ambient temperature is not known");
            }
            if (!i.Ambient.HasValue || double.IsNaN(i.Ambient.Value)) {
                return No("the weather device reports no temperature");
            }
            double ambient = i.Ambient.Value;

            TimeSpan save = i.SaveTime ?? TimeSpan.Zero;
            var fitting = new List<DarkNeed>();
            DarkNeed shortest = null;
            string shortestWhy = null;
            foreach (DarkNeed n in queue) {
                string why = DarkPlanner.SunFitReason(w, CoolTime(ambient, n.SetTemp, i.CoolingSecondsPerC), n.Exposure, save);
                if (why == null) {
                    fitting.Add(n);
                } else if (shortest == null || n.Exposure < shortest.Exposure) {
                    shortest = n;
                    shortestWhy = why;
                }
            }
            if (fitting.Count == 0) {
                return No(string.Format(CultureInfo.InvariantCulture, "the shortest needed dark ({0}) does not fit before dawn: {1}", shortest, shortestWhy));
            }

            var unknown = new List<DarkNeed>();
            string refused = null;
            CoolerCapacity capacity = DarkPlanner.Capacity(i.Holds, i.Limits);
            foreach (DarkNeed n in DarkPlanner.WarmFirst(fitting)) {
                string limit = DarkPlanner.AboveAmbientReason(n.SetTemp, ambient)
                    ?? DarkPlanner.CapacityReason(capacity, n.SetTemp, ambient)
                    ?? DarkPlanner.LimitReason(i.Limits, n.SetTemp, ambient, double.NaN, null);
                if (limit != null) {
                    refused ??= string.Format(CultureInfo.InvariantCulture, "{0}: {1}", n, limit);
                    continue;
                }

                CoolerHold proof = Proof(i.Holds, ambient, n.SetTemp);
                if (proof != null) {
                    return new DarkReadyResult {
                        Ready = true,
                        Need = n,
                        Basis = string.Format(CultureInfo.InvariantCulture,
                            "{0} °C is {1:0.#} °C below the {2:0.#} °C ambient, and the cooler held {3} °C at {4:0.#} °C ambient ({5:0.#} °C below it) on {6:yyyy-MM-dd HH:mm}{7}",
                            n.SetTemp, ambient - n.SetTemp, ambient, proof.SetTemp, proof.Ambient, proof.Ambient - proof.SetTemp, proof.At.ToLocalTime(),
                            proof.PeakPower.HasValue ? string.Format(CultureInfo.InvariantCulture, " at up to {0:0}% cooler power", proof.PeakPower.Value) : "")
                    };
                }

                unknown.Add(n);
            }

            if (unknown.Count > 0) {
                DarkNeed n = unknown[0];
                if (i.LearningUsedAt.HasValue) {
                    return No(string.Format(CultureInfo.InvariantCulture,
                        "no hold or cooler failure covers {0} °C at {1:0.#} °C ambient, and tonight's learning attempt was used up at {2:HH:mm}",
                        n.SetTemp, ambient, i.LearningUsedAt.Value.ToLocalTime()));
                }
                return new DarkReadyResult {
                    Ready = true,
                    Learning = true,
                    Need = n,
                    Basis = string.Format(CultureInfo.InvariantCulture,
                        "no hold or cooler failure covers {0} °C at {1:0.#} °C ambient; this is tonight's learning attempt, used up when Target Scheduler Darks records the first hold or cooler failure", n.SetTemp, ambient)
                };
            }

            return No(string.Format(CultureInfo.InvariantCulture, "the cooler cannot reach any needed set temperature at {0:0.#} °C ambient ({1})", ambient, refused));
        }

        private static DarkReadyResult No(string reason) {
            return new DarkReadyResult { Ready = false, Reason = reason };
        }
    }
}
