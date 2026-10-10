using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Darks {

    public enum DarkPlacement {
        BeforeWait = 0,
        WhileUnsafe = 1,
        EndOfSequence = 2
    }

    public class DarkNeed {
        public string ComboKey { get; set; }
        public int Priority { get; set; }
        public double Exposure { get; set; }
        public double Gain { get; set; }
        public double Offset { get; set; }
        public double Binning { get; set; } = 1;
        public string ReadoutMode { get; set; }
        public double SetTemp { get; set; }
        public int FramesNeeded { get; set; }
        public int FramesHave { get; set; }
        public int FramesRejected { get; set; }
        public int LightsBlocked { get; set; }
        public int LightsScaled { get; set; }
        public DateTime? NewestDarkAt { get; set; }
        public double SessionGapHours { get; set; }
        public string Basis { get; set; }
        public DateTime? ComputedAt { get; set; }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "{0} s, gain {1}, offset {2}, bin {3}, {4} °C", Exposure, Gain, Offset, Binning, SetTemp);
        }
    }

    public class DarkTaken {
        public string ComboKey { get; set; }
        public DateTime TakenAt { get; set; }
    }

    public class CoolerRates {
        public double? CoolingSecondsPerC { get; set; }
        public double? WarmingSecondsPerC { get; set; }
    }

    public class CoolerLimit {
        public double SetTemp { get; set; }
        public double? Ambient { get; set; }
        public double? ImagingPower { get; set; }
        public double? ImagingSetpoint { get; set; }
        public string Reason { get; set; }
    }

    public class DarkWindow {
        public DarkPlacement Placement { get; set; }
        public DateTime Now { get; set; }
        public DateTime? WaitUntil { get; set; }
        public TimeSpan? AfterWait { get; set; }
        public bool ReplanOrPause { get; set; }
        public bool? Safe { get; set; }
        public DateTime? StopBy { get; set; }
    }

    public static class DarkPlanner {
        public const double AtTargetToleranceC = 2.0;

        public static TimeSpan SessionGap(DarkNeed need) {
            return need.SessionGapHours > 0 ? TimeSpan.FromHours(need.SessionGapHours) : TimeSpan.FromHours(36);
        }

        public static int TakenNotIngested(DarkNeed need, IEnumerable<DarkTaken> taken, DateTime nowUtc) {
            TimeSpan gap = SessionGap(need);
            return (taken ?? Enumerable.Empty<DarkTaken>()).Count(t =>
                t != null && t.ComboKey == need.ComboKey &&
                (!need.NewestDarkAt.HasValue || t.TakenAt > need.NewestDarkAt.Value) &&
                nowUtc - t.TakenAt <= gap);
        }

        public static int Remaining(DarkNeed need, IEnumerable<DarkTaken> taken, DateTime nowUtc) {
            return Math.Max(0, need.FramesNeeded - need.FramesHave - TakenNotIngested(need, taken, nowUtc));
        }

        public static List<DarkNeed> Queue(IEnumerable<DarkNeed> needs, IEnumerable<DarkTaken> taken, DateTime nowUtc) {
            List<DarkTaken> list = (taken ?? Enumerable.Empty<DarkTaken>()).ToList();
            return (needs ?? Enumerable.Empty<DarkNeed>())
                .Where(n => n != null && n.Exposure > 0 && Remaining(n, list, nowUtc) > 0)
                .OrderBy(n => n.Priority)
                .ThenBy(n => n.ComboKey, StringComparer.Ordinal)
                .ToList();
        }

        public static bool AtTemperature(double sensor, double setpoint) {
            return !double.IsNaN(sensor) && Math.Abs(sensor - setpoint) <= AtTargetToleranceC;
        }

        public static bool SameSetpoint(double a, double b) {
            return Math.Abs(a - b) < 0.05;
        }

        public static TimeSpan? MoveTime(double from, double to, CoolerRates rates) {
            if (SameSetpoint(from, to)) {
                return TimeSpan.Zero;
            }

            double delta = Math.Abs(to - from);
            double? rate = to < from ? rates?.CoolingSecondsPerC : rates?.WarmingSecondsPerC;
            if (!rate.HasValue || rate.Value <= 0) {
                return null;
            }

            return TimeSpan.FromSeconds(delta * rate.Value);
        }

        public static string StopReason(DarkWindow w) {
            if (w.StopBy.HasValue && w.Now >= w.StopBy.Value) {
                return "the instruction's time limit is up";
            }

            switch (w.Placement) {
                case DarkPlacement.BeforeWait:
                    if (!w.WaitUntil.HasValue) {
                        return "not inside a Target Scheduler wait";
                    }
                    if (w.ReplanOrPause) {
                        return "a re-plan or pause was requested from the web";
                    }
                    if (!w.AfterWait.HasValue) {
                        return "the AfterWait instructions have not been timed yet, so the margin before imaging is not known";
                    }
                    if (w.Now >= w.WaitUntil.Value - w.AfterWait.Value) {
                        return "the wait is over";
                    }
                    return null;

                case DarkPlacement.WhileUnsafe:
                    if (!w.Safe.HasValue) {
                        return "no safety monitor is connected, so it is not known when imaging can resume";
                    }
                    if (w.Safe.Value) {
                        return "it is safe again";
                    }
                    return null;

                default:
                    return null;
            }
        }

        public static string FitReason(DarkWindow w, DarkNeed need, double currentSetpoint, double imagingSetpoint, CoolerRates rates, TimeSpan overhead) {
            if (w.Placement != DarkPlacement.BeforeWait) {
                return null;
            }

            TimeSpan? there = MoveTime(currentSetpoint, need.SetTemp, rates);
            TimeSpan? back = MoveTime(need.SetTemp, imagingSetpoint, rates);
            if (!there.HasValue || !back.HasValue) {
                return string.Format(CultureInfo.InvariantCulture,
                    "it needs the setpoint moved to {0} °C and back to {1} °C, and the cooler's rate in that direction has not been measured yet",
                    need.SetTemp, imagingSetpoint);
            }

            DateTime end = w.Now + there.Value + TimeSpan.FromSeconds(need.Exposure) + overhead + back.Value + (w.AfterWait ?? TimeSpan.Zero);
            if (end > w.WaitUntil.Value) {
                return string.Format(CultureInfo.InvariantCulture,
                    "a {0} s dark with {1:0} s to reach {2} °C, {3:0} s to return and {4:0} s of AfterWait would end at {5:HH:mm:ss}, after the wait ends at {6:HH:mm:ss}",
                    need.Exposure, there.Value.TotalSeconds, need.SetTemp, back.Value.TotalSeconds, (w.AfterWait ?? TimeSpan.Zero).TotalSeconds,
                    end.ToLocalTime(), w.WaitUntil.Value.ToLocalTime());
            }

            return null;
        }

        public static string LimitReason(IEnumerable<CoolerLimit> limits, double setTemp, double? ambient, double imagingSetpoint, double? imagingPower) {
            foreach (CoolerLimit l in limits ?? Enumerable.Empty<CoolerLimit>()) {
                if (l == null || setTemp > l.SetTemp + 0.05) {
                    continue;
                }

                if (ambient.HasValue && l.Ambient.HasValue) {
                    if (ambient.Value < l.Ambient.Value) {
                        continue;
                    }
                    return string.Format(CultureInfo.InvariantCulture,
                        "the cooler could not hold {0} °C at {1:0.#} °C ambient and it is {2:0.#} °C now ({3})", l.SetTemp, l.Ambient.Value, ambient.Value, l.Reason);
                }

                if (imagingPower.HasValue && l.ImagingPower.HasValue && l.ImagingSetpoint.HasValue && SameSetpoint(l.ImagingSetpoint.Value, imagingSetpoint)) {
                    if (imagingPower.Value < l.ImagingPower.Value) {
                        continue;
                    }
                    return string.Format(CultureInfo.InvariantCulture,
                        "the cooler could not hold {0} °C when it needed {1:0}% at {2} °C, and it needs {3:0}% now ({4})",
                        l.SetTemp, l.ImagingPower.Value, imagingSetpoint, imagingPower.Value, l.Reason);
                }

                return string.Format(CultureInfo.InvariantCulture, "the cooler could not hold {0} °C earlier this run ({1})", l.SetTemp, l.Reason);
            }

            return null;
        }

        public static double? SecondsPerC(double from, double to, TimeSpan took) {
            double delta = Math.Abs(to - from);
            if (delta < 1 || took <= TimeSpan.Zero) {
                return null;
            }

            return took.TotalSeconds / delta;
        }

        public static short? ReadoutIndex(string mode, IList<string> modes) {
            if (string.IsNullOrWhiteSpace(mode) || modes == null) {
                return null;
            }

            for (int i = 0; i < modes.Count; i++) {
                if (string.Equals(modes[i]?.Trim(), mode.Trim(), StringComparison.OrdinalIgnoreCase)) {
                    return (short)i;
                }
            }

            return null;
        }
    }
}
