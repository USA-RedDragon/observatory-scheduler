using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class SeasonalRunwayRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Seasonal Runway";
        public const double DEFAULT_WEIGHT = 0;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            try {
                SeasonEntry entry = RarityRule.Lookup(scoringEngine, potentialTarget);
                if (entry == null) { return 0; }

                double needed = SeasonalRunway.HoursNeeded(Filters(potentialTarget));
                double score = SeasonalRunway.Score(needed, entry.Season);
                TSLogger.Debug($"seasonal runway {potentialTarget.Project.Name}/{potentialTarget.Name}: {needed:0.0}h needed, {SeasonalRunway.SeasonHours(entry.Season):0.0}h of season, score {score:0.000}");
                return score;
            } catch (Exception ex) {
                TSLogger.Warning($"seasonal runway rule failed for {potentialTarget?.Name}, scoring 0: {ex.Message}");
                return 0;
            }
        }

        public static List<RunwayFilter> Filters(ITarget target) {
            ExposureCompletionHelper helper = target.Project.ExposureCompletionHelper;
            List<RunwayFilter> filters = new List<RunwayFilter>();
            foreach (IExposure plan in target.ExposurePlans ?? new List<IExposure>()) {
                if (plan == null || !plan.IsEnabled) { continue; }
                filters.Add(new RunwayFilter {
                    Filter = plan.FilterName,
                    Goal = helper?.GoalFor(plan),
                    CountHours = helper == null ? 0 : helper.CountRemainingExposures(plan) * plan.ExposureLength / 3600.0,
                    ExposureSeconds = plan.ExposureLength,
                    Acquired = plan.Acquired
                });
            }

            return filters;
        }
    }
}
