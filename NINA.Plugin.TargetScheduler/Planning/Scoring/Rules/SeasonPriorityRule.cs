using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class SeasonPriorityRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Season Priority";
        public const double DEFAULT_WEIGHT = 0;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            return PlannerSignals.Current.SeasonBoost(potentialTarget?.Guid);
        }
    }
}
