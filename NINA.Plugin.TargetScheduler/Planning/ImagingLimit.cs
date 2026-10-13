using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Planning {

    public class FlipSettings {
        public double MinutesAfterMeridian { get; set; }
        public double MaxMinutesAfterMeridian { get; set; }
        public double PauseTimeBeforeMeridian { get; set; }
        public bool UseSideOfPier { get; set; }
    }

    public class ImagingLimit {
        public static readonly TimeSpan UnmeasuredFlipHorizon = TimeSpan.FromHours(1);

        private static readonly TimeSpan ScanStep = TimeSpan.FromMinutes(5);

        public DateTime End { get; set; }
        public string Basis { get; set; }

        /// <summary>
        /// True when this end of imaging falls in the night a plan that starts at start covers, so that plan
        /// must stop imaging where the running sequence will.
        /// </summary>
        public bool Covers(DateTime start) {
            return End > start && End - start <= TimeSpan.FromHours(24);
        }
        public FlipSettings Flip { get; set; }
        public TimeSpan? FlipOverhead { get; set; }

        public static DateTime? FindEnd(Func<DateTime, double> altitude, double offset, bool endsAbove, DateTime from, TimeSpan within) {
            bool Ended(DateTime t) {
                double a = altitude(t);
                return endsAbove ? a > offset : a <= offset;
            }

            if (Ended(from)) {
                return from;
            }

            DateTime last = from;
            DateTime stop = from + within;
            for (DateTime t = from + ScanStep; ; t += ScanStep) {
                if (t > stop) {
                    t = stop;
                }
                if (Ended(t)) {
                    DateTime lo = last, hi = t;
                    while (hi - lo > TimeSpan.FromSeconds(1)) {
                        DateTime mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
                        if (Ended(mid)) {
                            hi = mid;
                        } else {
                            lo = mid;
                        }
                    }
                    return hi;
                }
                if (t >= stop) {
                    return null;
                }
                last = t;
            }
        }

        public static TimeSpan FlipWait(DateTime atTime, DateTime transit, FlipSettings f) {
            TimeSpan wait = transit.AddMinutes(f.MinutesAfterMeridian) - atTime;
            if (wait < TimeSpan.Zero || wait > TimeSpan.FromHours(2)) {
                return TimeSpan.Zero;
            }
            return wait;
        }

        public bool FlipDue(DateTime atTime, double exposureSeconds, DateTime transit) {
            if (Flip == null || transit == DateTime.MinValue) {
                return false;
            }
            DateTime latest = transit.AddMinutes(Flip.MaxMinutesAfterMeridian);
            if (atTime > latest) {
                return false;
            }
            DateTime flipAt = latest;
            if (Flip.PauseTimeBeforeMeridian != 0) {
                flipAt = transit.AddMinutes(-Flip.PauseTimeBeforeMeridian);
            } else if (atTime >= transit.AddMinutes(Flip.MinutesAfterMeridian)) {
                return true;
            }
            TimeSpan slack = Flip.UseSideOfPier ? TimeSpan.Zero : TimeSpan.FromMinutes(2);
            return flipAt - atTime <= TimeSpan.FromSeconds(Math.Max(0, exposureSeconds)) + slack;
        }

        public string FitReason(DateTime atTime, double exposureSeconds, DateTime transit) {
            TimeSpan exposure = TimeSpan.FromSeconds(Math.Max(0, exposureSeconds));
            DateTime end = atTime + exposure;
            string flipText = "";
            if (FlipDue(atTime, exposureSeconds, transit)) {
                TimeSpan wait = FlipWait(atTime, transit, Flip);
                if (!FlipOverhead.HasValue) {
                    DateTime known = atTime + wait + exposure;
                    if (known + UnmeasuredFlipHorizon > End) {
                        return string.Format(CultureInfo.InvariantCulture,
                            "NINA's meridian flip is due before the {0} s exposure, the flip's slew, recenter and settle time has not been measured yet, and the sequence ends imaging at {1:HH:mm:ss} ({2}), so the flip and the exposure cannot be shown to finish in time",
                            exposureSeconds, End, Basis);
                    }
                    return null;
                }
                end = atTime + wait + FlipOverhead.Value + exposure;
                flipText = string.Format(CultureInfo.InvariantCulture,
                    " after NINA's meridian flip ({0:0} s waiting to pass the meridian and {1:0} s measured for the slew, recenter and settle)",
                    wait.TotalSeconds, FlipOverhead.Value.TotalSeconds);
            }

            if (end > End) {
                return string.Format(CultureInfo.InvariantCulture,
                    "a {0} s exposure{1} would end at {2:HH:mm:ss}, after the sequence ends imaging at {3:HH:mm:ss} ({4})",
                    exposureSeconds, flipText, end, End, Basis);
            }

            return null;
        }

        public static TimeSpan NinaFlipWait(double timeToMeridianFlipHours, FlipSettings f) {
            if (f == null || double.IsNaN(timeToMeridianFlipHours) || double.IsInfinity(timeToMeridianFlipHours)) {
                return TimeSpan.Zero;
            }
            TimeSpan wait = TimeSpan.FromHours(timeToMeridianFlipHours) - TimeSpan.FromMinutes(f.MaxMinutesAfterMeridian - f.MinutesAfterMeridian);
            if (wait < TimeSpan.Zero || wait > TimeSpan.FromHours(2)) {
                return TimeSpan.Zero;
            }
            return wait;
        }

        public static TimeSpan? MeasuredOverhead(TimeSpan flipElapsed, TimeSpan ninaWait) {
            TimeSpan overhead = flipElapsed - ninaWait;
            return overhead > TimeSpan.Zero ? overhead : (TimeSpan?)null;
        }
    }

    public class FlipStateData {
        public List<FlipMeasurement> Flips { get; set; } = new List<FlipMeasurement>();
    }

    public class FlipMeasurement {
        public double OverheadSeconds { get; set; }
        public DateTime MeasuredAt { get; set; }
    }

    public class FlipState {
        public const int Keep = 5;

        private readonly object sync = new object();
        private readonly string path;
        private readonly FlipStateData data;

        public FlipState(string path) {
            this.path = path;
            data = Load(path);
        }

        private static FlipStateData Load(string path) {
            try {
                if (path != null && File.Exists(path)) {
                    FlipStateData d = JsonSerializer.Deserialize<FlipStateData>(File.ReadAllText(path));
                    if (d?.Flips != null) {
                        return d;
                    }
                }
            } catch (Exception) {
                return new FlipStateData();
            }

            return new FlipStateData();
        }

        public TimeSpan? Overhead() {
            lock (sync) {
                return data.Flips.Count == 0 ? (TimeSpan?)null : TimeSpan.FromSeconds(data.Flips.Max(f => f.OverheadSeconds));
            }
        }

        public void Record(TimeSpan overhead, DateTime nowUtc) {
            lock (sync) {
                data.Flips.Add(new FlipMeasurement { OverheadSeconds = overhead.TotalSeconds, MeasuredAt = nowUtc });
                data.Flips = data.Flips.OrderBy(f => f.MeasuredAt).Skip(Math.Max(0, data.Flips.Count - Keep)).ToList();
                if (path == null) {
                    return;
                }
                try {
                    File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
                } catch (Exception) {
                    return;
                }
            }
        }
    }
}
