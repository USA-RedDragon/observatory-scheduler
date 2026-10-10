using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class FilterStanding {
        public GoalProgress Goal { get; set; }
        public double PercentComplete { get; set; }
        public int Accepted { get; set; }
        public double ExposureSeconds { get; set; }
    }

    public static class GoalScoring {
        public const double NoveltyFreshHours = 1;
        public const double NoveltyRefineFactor = 0.8;
        public const int RarityFullNights = 120;
        public const int RarityRange = 110;

        public static double NoveltyScore(double progress, double effectiveHours) {
            double remaining = GoalCompletion.Clamp01(1 - GoalCompletion.Clamp01(progress));
            return remaining * (effectiveHours < NoveltyFreshHours ? 1 : NoveltyRefineFactor);
        }

        public static double NoveltyScore(IList<FilterStanding> filters) {
            if (filters == null || filters.Count == 0) { return NoveltyScore(0, 0); }

            double progress = filters.Min(f => f.Goal != null ? GoalCompletion.Clamp01(f.Goal.Progress) : GoalCompletion.Clamp01(f.PercentComplete / 100));

            List<FilterStanding> goalDriven = filters.Where(f => f.Goal != null).ToList();
            double effectiveHours = goalDriven.Count > 0
                ? goalDriven.Min(f => Math.Max(0, f.Goal.EffectiveHours))
                : filters.Sum(f => Math.Max(0, f.Accepted) * Math.Max(0, f.ExposureSeconds) / 3600);

            return NoveltyScore(progress, effectiveHours);
        }

        public static double RarityScore(SeasonResult season) {
            if (season == null || season.OutOfSeason) { return 0; }
            return GoalCompletion.Clamp01((RarityFullNights - season.NightsLeft) / (double)RarityRange);
        }
    }
}
