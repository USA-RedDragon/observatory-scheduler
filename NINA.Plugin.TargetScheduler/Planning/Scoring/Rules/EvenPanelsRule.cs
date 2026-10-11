using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class EvenPanelsRule : ScoringRule, IOptInScoringRule, IPlannerSwitch {
        public const string RULE_NAME = "Even Panels";
        public const double DEFAULT_WEIGHT = 0;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            return 0;
        }

        public static bool IsOn(IProject project) {
            return IsOn(project?.RuleWeights);
        }

        public static bool IsOn(Dictionary<string, double> ruleWeights) {
            return ruleWeights != null && ruleWeights.TryGetValue(RULE_NAME, out double weight) && weight > 0;
        }
    }
}
