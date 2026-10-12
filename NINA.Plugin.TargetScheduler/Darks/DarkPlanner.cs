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
        public DateTime? At { get; set; }
        public double SetTemp { get; set; }
        public double? Ambient { get; set; }
        public double? ImagingPower { get; set; }
        public double? ImagingSetpoint { get; set; }
        public double? Reached { get; set; }
        public string Reason { get; set; }
    }

    public class CoolerCapacity {
        public double DropC { get; set; }
        public double Ambient { get; set; }
        public double Temperature { get; set; }
        public DateTime? At { get; set; }
        public bool Held { get; set; }
        public double? PeakPower { get; set; }

        public override string ToString() {
            string when = At.HasValue ? At.Value.ToLocalTime().ToString(" yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
            return Held
                ? string.Format(CultureInfo.InvariantCulture, "held {0} °C at {1:0.#} °C ambient{2}{3}", Temperature, Ambient, when,
                    PeakPower.HasValue ? string.Format(CultureInfo.InvariantCulture, " at up to {0:0}% cooler power", PeakPower.Value) : "")
                : string.Format(CultureInfo.InvariantCulture, "reached {0:0.#} °C at {1:0.#} °C ambient{2} before it stopped making progress", Temperature, Ambient, when);
        }
    }

    public class CoolerHold {
        public DateTime At { get; set; }
        public double Ambient { get; set; }
        public double SetTemp { get; set; }
        public double? PeakPower { get; set; }
    }

    public enum DarkCoverState {
        Unknown = 0,
        NeitherOpenNorClosed = 1,
        Closed = 2,
        Open = 3,
        Error = 4,
        NotPresent = 5
    }

    public class DarkCover {
        public bool Configured { get; set; }
        public string Device { get; set; }
        public bool Connected { get; set; }
        public DarkCoverState State { get; set; }
        public bool LightOn { get; set; }
        public string ReadError { get; set; }
    }

    public class DarkWindow {
        public DarkPlacement Placement { get; set; }
        public DarkCover Cover { get; set; }
        public DateTime Now { get; set; }
        public DateTime? WaitUntil { get; set; }
        public TimeSpan? AfterWait { get; set; }
        public bool ReplanOrPause { get; set; }
        public bool? Safe { get; set; }
        public DateTime? StopBy { get; set; }
        public Func<DateTime, double?> SunAltitudeAt { get; set; }
    }

    public static class DarkPlanner {
        public const double AtTargetToleranceC = 2.0;

        public const double SunLimitDegrees = -8.0;

        public const double RetryAmbientDropC = 1.0;

        public const double CapacityPowerPercent = 99.0;

        public const string SunLimitBasis = "measured from the darks in s3:astro/DARK with the stacker's light-leak spread (leak limit about 0.6 ADU) and the sun's altitude over each exposure at the site in their headers. 2026-09-29 dawn, 600 s Blue at -5 °C: dark 0020 ended at -0.67° and is clean (0.33 ADU); dark 0021 ran from -0.67° to +1.45° and leaks (0.71 ADU), then 1.76, 3.11 and 4.14 ADU. 2026-09-30 dawn, 600 s Luminance at +5 °C: clean to -4.46° (0.37 ADU). 2025-02-05 dusk, 600 s S-II at 0 °C: dark 0113 from -3.61° to -5.66° leaks (5.51 ADU) and dark 0114 from -5.66° to -7.73° is at the limit (0.59 ADU). -8° is below every altitude at which any of these darks gained signal, and below the last clean dawn dark at -0.67°";

        public static readonly TimeSpan UnmeasuredMoveHorizon = TimeSpan.FromHours(1);

        private static readonly TimeSpan SunStep = TimeSpan.FromMinutes(1);

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

        public static List<DarkNeed> WarmFirst(IEnumerable<DarkNeed> queue) {
            return (queue ?? Enumerable.Empty<DarkNeed>())
                .OrderByDescending(n => n.SetTemp)
                .ThenBy(n => n.Priority)
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

        public static string CoverStopReason(DarkCover c) {
            if (c == null || !c.Configured) {
                return null;
            }
            string device = string.IsNullOrWhiteSpace(c.Device) ? "the profile's flat device" : $"the flat device {c.Device}";
            if (c.ReadError != null) {
                return $"{device} could not be read ({c.ReadError}), so the cover is not confirmed closed";
            }
            if (!c.Connected) {
                return $"{device} is not connected, so the cover is not confirmed closed";
            }
            if (c.State != DarkCoverState.Closed) {
                return $"{device} reports its cover as {c.State}, not Closed";
            }
            if (c.LightOn) {
                return $"{device} reports its light on";
            }
            return null;
        }

        public static string StopReason(DarkWindow w) {
            string cover = CoverStopReason(w.Cover);
            if (cover != null) {
                return cover;
            }

            if (w.StopBy.HasValue && w.Now >= w.StopBy.Value) {
                return "the instruction's time limit is up";
            }

            string sun = SunStopReason(w);
            if (sun != null) {
                return sun;
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

        public static string SunStopReason(DarkWindow w) {
            if (w.SunAltitudeAt == null) {
                return "the sun's altitude is not known, so daylight cannot be ruled out";
            }
            double? alt = w.SunAltitudeAt(w.Now);
            if (!alt.HasValue || double.IsNaN(alt.Value)) {
                return "the sun's altitude is not known, so daylight cannot be ruled out";
            }
            if (alt.Value >= SunLimitDegrees) {
                return string.Format(CultureInfo.InvariantCulture,
                    "the sun is at {0:0.0}°, at or above the light-safe limit of {1:0.0}°", alt.Value, SunLimitDegrees);
            }
            return null;
        }

        public static string SunFitReason(DarkWindow w, TimeSpan untilStart, double exposure, TimeSpan overhead) {
            string stop = SunStopReason(w);
            if (stop != null) {
                return stop;
            }

            DateTime start = w.Now + (untilStart > TimeSpan.Zero ? untilStart : TimeSpan.Zero);
            DateTime end = start + TimeSpan.FromSeconds(Math.Max(0, exposure)) + (overhead > TimeSpan.Zero ? overhead : TimeSpan.Zero);
            for (DateTime t = w.Now; ; t += SunStep) {
                if (t > end) {
                    t = end;
                }
                double? alt = w.SunAltitudeAt(t);
                if (!alt.HasValue || double.IsNaN(alt.Value)) {
                    return "the sun's altitude is not known, so daylight cannot be ruled out";
                }
                if (alt.Value >= SunLimitDegrees) {
                    return string.Format(CultureInfo.InvariantCulture,
                        "a {0} s dark would end at {1:HH:mm:ss}, and the sun reaches {2:0.0}° by {3:HH:mm:ss}, at or above the light-safe limit of {4:0.0}°",
                        exposure, end.ToLocalTime(), alt.Value, t.ToLocalTime(), SunLimitDegrees);
                }
                if (t >= end) {
                    return null;
                }
            }
        }

        public static DateTime? SunLimitAt(DarkWindow w, TimeSpan within) {
            if (w.SunAltitudeAt == null) {
                return w.Now;
            }
            DateTime end = w.Now + within;
            for (DateTime t = w.Now; ; t += SunStep) {
                if (t > end) {
                    t = end;
                }
                double? alt = w.SunAltitudeAt(t);
                if (!alt.HasValue || double.IsNaN(alt.Value) || alt.Value >= SunLimitDegrees) {
                    return t;
                }
                if (t >= end) {
                    return null;
                }
            }
        }

        public static DateTime? LatestStart(DarkWindow w, double exposure, TimeSpan overhead, TimeSpan within) {
            DateTime? sun = SunLimitAt(w, within);
            if (!sun.HasValue) {
                return null;
            }
            return sun.Value - TimeSpan.FromSeconds(Math.Max(0, exposure)) - (overhead > TimeSpan.Zero ? overhead : TimeSpan.Zero);
        }

        public static string FitReason(DarkWindow w, DarkNeed need, double currentSetpoint, double imagingSetpoint, CoolerRates rates, TimeSpan overhead) {
            TimeSpan? there = MoveTime(currentSetpoint, need.SetTemp, rates);
            if (!there.HasValue) {
                string unmeasured = UnmeasuredMoveReason(w, need, currentSetpoint, overhead);
                if (unmeasured != null) {
                    return unmeasured;
                }
            }

            string sun = SunFitReason(w, there ?? TimeSpan.Zero, need.Exposure, overhead);
            if (sun != null) {
                return sun;
            }

            if (w.Placement != DarkPlacement.BeforeWait) {
                return null;
            }

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

        public static string UnmeasuredMoveReason(DarkWindow w, DarkNeed need, double currentSetpoint, TimeSpan overhead) {
            string direction = need.SetTemp < currentSetpoint ? "cooling" : "warming";
            if (w.Placement == DarkPlacement.BeforeWait && w.WaitUntil.HasValue) {
                return string.Format(CultureInfo.InvariantCulture,
                    "it needs the setpoint moved from {0} °C to {1} °C, the cooler's {2} rate has not been measured, and the wait ends at {3:HH:mm:ss}, so the move cannot be shown to finish in time",
                    currentSetpoint, need.SetTemp, direction, w.WaitUntil.Value.ToLocalTime());
            }

            DateTime? latest = LatestStart(w, need.Exposure, overhead, UnmeasuredMoveHorizon + TimeSpan.FromSeconds(Math.Max(0, need.Exposure)) + overhead);
            if (latest.HasValue) {
                return string.Format(CultureInfo.InvariantCulture,
                    "it needs the setpoint moved from {0} °C to {1} °C, the cooler's {2} rate has not been measured, and a {3} s dark must start by {4:HH:mm:ss} to end before the sun reaches {5:0.0}°, so the move cannot be shown to finish in time",
                    currentSetpoint, need.SetTemp, direction, need.Exposure, latest.Value.ToLocalTime(), SunLimitDegrees);
            }

            return null;
        }

        public const string CameraDisconnected = "the camera disconnected";

        public static string AboveAmbientReason(double setTemp, double? ambient) {
            if (!ambient.HasValue || setTemp <= ambient.Value) {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} °C is above the {1:0.#} °C ambient, and the cooler cannot heat the sensor", setTemp, ambient.Value);
        }

        public static string LimitReason(IEnumerable<CoolerLimit> limits, double setTemp, double? ambient, double imagingSetpoint, double? imagingPower) {
            foreach (CoolerLimit l in limits ?? Enumerable.Empty<CoolerLimit>()) {
                if (l == null || setTemp > l.SetTemp + 0.05) {
                    continue;
                }

                if (ambient.HasValue && l.Ambient.HasValue) {
                    if (ambient.Value <= l.Ambient.Value - RetryAmbientDropC + 0.05) {
                        continue;
                    }
                    return string.Format(CultureInfo.InvariantCulture,
                        "the cooler could not hold {0} °C at {1:0.#} °C ambient and it is {2:0.#} °C now; it is tried again once the ambient is {3:0.#} °C or lower ({4})",
                        l.SetTemp, l.Ambient.Value, ambient.Value, l.Ambient.Value - RetryAmbientDropC, l.Reason);
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

        public static CoolerCapacity Capacity(IEnumerable<CoolerHold> holds, IEnumerable<CoolerLimit> limits) {
            CoolerCapacity best = null;
            bool full = false;
            foreach (CoolerHold h in holds ?? Enumerable.Empty<CoolerHold>()) {
                if (h == null || double.IsNaN(h.Ambient) || double.IsNaN(h.SetTemp)) {
                    continue;
                }
                full |= h.PeakPower.HasValue && h.PeakPower.Value >= CapacityPowerPercent;
                double drop = h.Ambient - h.SetTemp;
                if (best == null || drop > best.DropC) {
                    best = new CoolerCapacity { DropC = drop, Ambient = h.Ambient, Temperature = h.SetTemp, At = h.At, Held = true, PeakPower = h.PeakPower };
                }
            }
            foreach (CoolerLimit l in limits ?? Enumerable.Empty<CoolerLimit>()) {
                if (l == null || !l.Ambient.HasValue) {
                    continue;
                }
                full = true;
                if (!l.Reached.HasValue || double.IsNaN(l.Reached.Value)) {
                    continue;
                }
                double drop = l.Ambient.Value - l.Reached.Value;
                if (best == null || drop > best.DropC) {
                    best = new CoolerCapacity { DropC = drop, Ambient = l.Ambient.Value, Temperature = l.Reached.Value, At = l.At, Held = false };
                }
            }
            if (best == null || !full) {
                return null;
            }
            return best;
        }

        public static string CapacityReason(CoolerCapacity capacity, double setTemp, double? ambient) {
            if (capacity == null || !ambient.HasValue || double.IsNaN(ambient.Value)) {
                return null;
            }
            double drop = ambient.Value - setTemp;
            if (drop <= capacity.DropC + 0.05) {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} °C is {1:0.#} °C below the {2:0.#} °C ambient, and the most the cooler has reached is {3:0.#} °C below ambient ({4}); it is left for a night with the ambient at {5:0.#} °C or lower",
                setTemp, drop, ambient.Value, capacity.DropC, capacity, setTemp + capacity.DropC);
        }

        public static List<DarkNeed> TonightFirst(IEnumerable<DarkNeed> queue, double imagingSetpoint) {
            return (queue ?? Enumerable.Empty<DarkNeed>())
                .OrderBy(n => Tonight(n, imagingSetpoint) ? 0 : 1)
                .ThenBy(n => n.Priority)
                .ThenBy(n => n.ComboKey, StringComparer.Ordinal)
                .ToList();
        }

        public static bool Tonight(DarkNeed need, double imagingSetpoint) {
            return double.IsNaN(imagingSetpoint) || need.SetTemp >= imagingSetpoint - AtTargetToleranceC - 0.05;
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
