using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class BiasNeed {
        public string ComboKey { get; set; }
        public int Priority { get; set; }
        public double Gain { get; set; }
        public double Offset { get; set; }
        public double Binning { get; set; } = 1;
        public string ReadoutMode { get; set; }
        public int FramesNeeded { get; set; }
        public int FramesHave { get; set; }
        public DateTime? NewestBiasAt { get; set; }
        public double SessionGapHours { get; set; }
        public string Basis { get; set; }
        public DateTime? ComputedAt { get; set; }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "bias, gain {0}, offset {1}, bin {2}", Gain, Offset, Binning);
        }
    }

    public class BiasTaken {
        public string ComboKey { get; set; }
        public DateTime TakenAt { get; set; }
    }

    public static class BiasPlanner {
        public const double MaxExposureMin = 30;

        public static TimeSpan SessionGap(BiasNeed need) {
            return need.SessionGapHours > 0 ? TimeSpan.FromHours(need.SessionGapHours) : TimeSpan.FromHours(36);
        }

        public static int TakenNotIngested(BiasNeed need, IEnumerable<BiasTaken> taken, DateTime nowUtc) {
            TimeSpan gap = SessionGap(need);
            return (taken ?? Enumerable.Empty<BiasTaken>()).Count(t =>
                t != null && t.ComboKey == need.ComboKey &&
                (!need.NewestBiasAt.HasValue || t.TakenAt > need.NewestBiasAt.Value) &&
                nowUtc - t.TakenAt <= gap);
        }

        public static int Remaining(BiasNeed need, IEnumerable<BiasTaken> taken, DateTime nowUtc) {
            return Math.Max(0, need.FramesNeeded - need.FramesHave - TakenNotIngested(need, taken, nowUtc));
        }

        public static List<BiasNeed> Queue(IEnumerable<BiasNeed> needs, IEnumerable<BiasTaken> taken, DateTime nowUtc) {
            List<BiasTaken> list = (taken ?? Enumerable.Empty<BiasTaken>()).ToList();
            return (needs ?? Enumerable.Empty<BiasNeed>())
                .Where(n => n != null && Remaining(n, list, nowUtc) > 0)
                .OrderBy(n => n.Priority)
                .ThenBy(n => n.ComboKey, StringComparer.Ordinal)
                .ToList();
        }

        public static double Exposure(double exposureMin) {
            return double.IsNaN(exposureMin) || exposureMin > MaxExposureMin ? 0 : Math.Max(0, exposureMin);
        }

        public static string FitReason(DarkWindow w, double exposure, TimeSpan overhead) {
            string sun = DarkPlanner.SunFitReason(w, TimeSpan.Zero, exposure, overhead);
            if (sun != null) {
                return sun;
            }

            if (w.Placement != DarkPlacement.BeforeWait) {
                return null;
            }

            if (!w.WaitUntil.HasValue) {
                return "not inside a Target Scheduler wait";
            }
            if (!w.AfterWait.HasValue) {
                return "the AfterWait instructions have not been timed yet, so the margin before imaging is not known";
            }

            TimeSpan download = overhead > TimeSpan.Zero ? overhead : TimeSpan.Zero;
            DateTime end = w.Now + TimeSpan.FromSeconds(Math.Max(0, exposure)) + download + w.AfterWait.Value;
            if (end > w.WaitUntil.Value) {
                return string.Format(CultureInfo.InvariantCulture,
                    "a {0} s bias frame with {1:0} s to download and {2:0} s of AfterWait would end at {3:HH:mm:ss}, after the wait ends at {4:HH:mm:ss}",
                    exposure, download.TotalSeconds, w.AfterWait.Value.TotalSeconds, end.ToLocalTime(), w.WaitUntil.Value.ToLocalTime());
            }

            return null;
        }

        public static string Describe(IEnumerable<BiasNeed> queue) {
            return string.Join("; ", (queue ?? Enumerable.Empty<BiasNeed>()).Where(n => n != null).Select(n => n.ToString()));
        }
    }
}
