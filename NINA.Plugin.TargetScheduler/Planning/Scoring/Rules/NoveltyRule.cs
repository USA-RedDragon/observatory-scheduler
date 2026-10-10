using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class NoveltyRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Novelty";
        public const double DEFAULT_WEIGHT = .1 * WEIGHT_SCALE;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            return GoalScoring.NoveltyScore(Standings(potentialTarget));
        }

        public static List<FilterStanding> Standings(ITarget target) {
            ExposureCompletionHelper helper = target.Project.ExposureCompletionHelper;
            List<FilterStanding> standings = new List<FilterStanding>();

            List<IExposure> plans = new List<IExposure>();
            if (target.ExposurePlans != null) { plans.AddRange(target.ExposurePlans); }
            if (target.CompletedExposurePlans != null) { plans.AddRange(target.CompletedExposurePlans); }

            foreach (IExposure plan in plans) {
                standings.Add(new FilterStanding {
                    Goal = helper.GoalFor(plan),
                    PercentComplete = helper.PercentComplete(plan),
                    Accepted = plan.Accepted,
                    ExposureSeconds = plan.ExposureLength
                });
            }

            return standings;
        }
    }
}
