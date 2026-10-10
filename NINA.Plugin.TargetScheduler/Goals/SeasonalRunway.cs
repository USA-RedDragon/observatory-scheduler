using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class RunwayFilter {
        public string Filter { get; set; }
        public GoalProgress Goal { get; set; }
        public double CountHours { get; set; }
    }

    public static class SeasonalRunway {
        public const double ImagedShare = 0.3;
        public const double FallbackHoursPerNight = 3;

        public static double HoursNeeded(IEnumerable<RunwayFilter> filters) {
            double total = 0;
            HashSet<string> goalFilters = new HashSet<string>();
            foreach (RunwayFilter f in filters ?? Enumerable.Empty<RunwayFilter>()) {
                if (f == null) { continue; }
                if (f.Goal != null) {
                    string key = SqliteGoalProgressSource.NormalizeFilter(f.Filter) ?? string.Empty;
                    if (!goalFilters.Add(key) || f.Goal.Done) { continue; }
                    if (f.Goal.HoursNeeded > 0 && !double.IsInfinity(f.Goal.HoursNeeded)) { total += f.Goal.HoursNeeded; }
                    continue;
                }

                if (f.CountHours > 0 && !double.IsInfinity(f.CountHours)) { total += f.CountHours; }
            }

            return total;
        }

        public static double SeasonHours(SeasonResult season) {
            if (season == null || season.OutOfSeason) { return 0; }
            if (season.UsableHours > 0) { return season.UsableHours; }
            return Math.Max(0, season.NightsLeft) * FallbackHoursPerNight;
        }

        public static double Pressure(double hoursNeeded, double seasonHours) {
            if (hoursNeeded <= 0 || double.IsNaN(hoursNeeded)) { return 0; }
            double expected = seasonHours * ImagedShare;
            if (expected <= 0) { return double.PositiveInfinity; }
            return hoursNeeded / expected;
        }

        public static double Score(double hoursNeeded, SeasonResult season) {
            if (season == null || season.OutOfSeason) { return 0; }
            return GoalCompletion.Clamp01(Pressure(hoursNeeded, SeasonHours(season)));
        }
    }
}
