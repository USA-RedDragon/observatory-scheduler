using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Flats {

    /// <summary>
    /// Whether Target Scheduler Flats placed in the Before Wait instructions fit in the wait. The time per flat is the
    /// exposure plus 3.5 s, and each set adds 30 s for the filter and rotator: on 2026-10-10 150 flats in 5 sets took
    /// 665 s, which this puts at 825 s or more.
    /// </summary>
    public static class FlatWaitFit {
        public const double PerFlatSeconds = 3.5;
        public const double PerSetSeconds = 30;
        public const double PrepareSeconds = 60;
        public static readonly TimeSpan UnmeasuredAfterWait = TimeSpan.FromMinutes(10);

        public static TimeSpan Estimate(IEnumerable<double> setExposures, int flatCount) {
            double seconds = PrepareSeconds;
            foreach (double exposure in setExposures ?? Enumerable.Empty<double>()) {
                double e = double.IsNaN(exposure) || exposure < 0 ? 0 : exposure;
                seconds += PerSetSeconds + Math.Max(0, flatCount) * (e + PerFlatSeconds);
            }
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// In a wait the light goes on only under a cover the flat device confirms Closed.
        /// </summary>
        public static string CoverStopReason(bool connected, bool supportsOpenClose, string coverState) {
            if (!connected) { return "the flat device is not connected, so its cover is not confirmed closed"; }
            if (!supportsOpenClose) { return "the flat device has no cover it can report, so the light stays off"; }
            if (coverState != "Closed") { return $"the flat device reports its cover as {coverState}, not Closed"; }
            return null;
        }

        public static string StopReason(DateTime nowUtc, DateTime waitUntilUtc, TimeSpan? afterWait, TimeSpan estimate) {
            TimeSpan margin = afterWait ?? UnmeasuredAfterWait;
            DateTime end = nowUtc + estimate + margin;
            if (end <= waitUntilUtc) { return null; }
            return string.Format(CultureInfo.InvariantCulture,
                "the flats need about {0:0} min and {1:0} min of AfterWait{2}, so they would end at {3:HH:mm}, after the wait ends at {4:HH:mm}",
                estimate.TotalMinutes, margin.TotalMinutes, afterWait.HasValue ? "" : " (not timed yet)", end.ToLocalTime(), waitUntilUtc.ToLocalTime());
        }
    }
}
