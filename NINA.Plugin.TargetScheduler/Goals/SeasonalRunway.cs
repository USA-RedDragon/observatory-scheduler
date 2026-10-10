using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class RunwayFilter {
        public string Filter { get; set; }
        public GoalProgress Goal { get; set; }
        public double CountHours { get; set; }
        public double ExposureSeconds { get; set; }
        public int Acquired { get; set; }
    }

    public static class SeasonalRunway {
        public const double ImagedShare = 0.3;
        public const double FallbackHoursPerNight = 3;

        public static double HoursNeeded(IEnumerable<RunwayFilter> filters) {
            double total = 0;
            Dictionary<string, List<RunwayFilter>> goalFilters = new Dictionary<string, List<RunwayFilter>>();
            foreach (RunwayFilter f in filters ?? Enumerable.Empty<RunwayFilter>()) {
                if (f == null) { continue; }
                if (f.Goal != null) {
                    string key = SqliteGoalProgressSource.NormalizeFilter(f.Filter) ?? string.Empty;
                    if (!goalFilters.TryGetValue(key, out List<RunwayFilter> plans)) {
                        plans = new List<RunwayFilter>();
                        goalFilters[key] = plans;
                    }
                    plans.Add(f);
                    continue;
                }

                total += Positive(f.CountHours);
            }

            foreach (List<RunwayFilter> plans in goalFilters.Values) {
                total += GoalHours(plans[0].Goal, plans);
            }

            return total;
        }

        public static double GoalHours(GoalProgress goal, IList<RunwayFilter> plans) {
            if (goal == null || goal.Done) { return 0; }
            plans ??= new List<RunwayFilter>();

            if (goal.State == GoalState.Measured && KnownHours(goal)) {
                return goal.HoursNeeded.Value;
            }

            int acquired = plans.Count == 0 ? 0 : plans.Max(p => p.Acquired);
            double exposureSeconds = plans.Select(p => p.ExposureSeconds).Where(s => s > 0 && IsFinite(s)).DefaultIfEmpty(0).First();
            double ruleHours = SubHours(GoalCompletion.RemainingExposures(goal, exposureSeconds, acquired), exposureSeconds);

            if (goal.State == GoalState.Unmeasurable && goal.SubLimit.HasValue) { return ruleHours; }

            return Math.Max(plans.Sum(p => Positive(p.CountHours)), ruleHours);
        }

        public static bool KnownHours(GoalProgress goal) {
            return goal?.HoursNeeded != null && IsFinite(goal.HoursNeeded.Value) && goal.HoursNeeded.Value >= 0;
        }

        private static double SubHours(int subs, double exposureSeconds) {
            return Math.Max(0, subs) * exposureSeconds / 3600;
        }

        private static double Positive(double hours) {
            return hours > 0 && IsFinite(hours) ? hours : 0;
        }

        private static bool IsFinite(double value) {
            return !double.IsNaN(value) && !double.IsInfinity(value);
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
