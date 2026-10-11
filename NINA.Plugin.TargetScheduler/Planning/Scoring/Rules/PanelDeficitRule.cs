using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class PanelDeficitRule : ScoringRule {
        public const string RULE_NAME = "Panel Deficit";
        public const double DEFAULT_WEIGHT = 0 * WEIGHT_SCALE;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            IProject project = potentialTarget?.Project;
            return EvenShare.Spreads(EvenPanelsRule.IsOn(project), MosaicCompletion.PanelCount(project))
                ? MosaicCompletion.EvenScore(potentialTarget, scoringEngine.AtTime)
                : MosaicCompletion.DeficitScore(potentialTarget);
        }
    }
}
