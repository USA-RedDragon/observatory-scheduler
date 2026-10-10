using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class ConditionMatchRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Condition Match";
        public const double DEFAULT_WEIGHT = 0;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            try {
                ObserverInfo observer = SkyConditions.Observer(scoringEngine.ActiveProfile);
                DateTime at = scoringEngine.AtTime;
                double glare = SkyConditions.MoonGlare(observer, potentialTarget.Coordinates, at);

                List<IExposure> plans = (potentialTarget.ExposurePlans ?? new List<IExposure>()).Where(p => p != null && !p.Rejected).ToList();
                if (plans.Count == 0 && potentialTarget.SelectedExposure != null) { plans.Add(potentialTarget.SelectedExposure); }

                double altitude = AstrometryUtils.GetAltitude(observer, potentialTarget.Coordinates, at);
                double peak = ConditionMatch.PeakAltitude(observer.Latitude, potentialTarget.Coordinates.Dec, potentialTarget.Project.MaximumAltitude);
                double? quality = SkyConditions.NightQuality(at);
                double score = ConditionMatch.Score(altitude, peak, plans.Select(p => SkyConditions.For(p, glare)), quality);
                TSLogger.Debug($"condition match {potentialTarget.Project.Name}/{potentialTarget.Name}: alt {altitude:0.0}/{peak:0.0}, glare {glare:0.00}, quality {(quality.HasValue ? quality.Value.ToString("0.00") : "unknown")}, score {score:0.000}");
                return score;
            } catch (Exception ex) {
                TSLogger.Warning($"condition match rule failed for {potentialTarget?.Name}, scoring 0: {ex.Message}");
                return 0;
            }
        }
    }
}
