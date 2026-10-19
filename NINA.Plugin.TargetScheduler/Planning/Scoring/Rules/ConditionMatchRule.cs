using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
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
                Coordinates coordinates = potentialTarget.CoordinatesAt(at);
                if (coordinates == null && potentialTarget.IsBody()) {
                    TSLogger.Debug($"condition match {potentialTarget.Project.Name}/{potentialTarget.Name}: moving body position unknown, scoring 0");
                    return 0;
                }

                double glare = SkyConditions.MoonGlare(observer, coordinates, at);
                MoonState? moon = SkyConditions.KeepState(observer, coordinates, at);

                List<IExposure> plans = (potentialTarget.ExposurePlans ?? new List<IExposure>()).Where(p => p != null && !p.Rejected).ToList();
                if (plans.Count == 0 && potentialTarget.SelectedExposure != null) { plans.Add(potentialTarget.SelectedExposure); }

                double altitude = AstrometryUtils.GetAltitude(observer, coordinates, at);
                double peak = ConditionMatch.PeakAltitude(observer.Latitude, coordinates.Dec, potentialTarget.Project.MaximumAltitude);
                double? quality = SkyConditions.NightQuality(at);
                Dictionary<IExposure, FilterCondition> conditions = plans.Distinct().ToDictionary(p => p, p => PanelCondition(potentialTarget, p, glare, moon, observer, altitude));
                string steering = "";
                double score;
                if (FilterSteeringRule.IsOn(potentialTarget.Project)) {
                    (IExposure chosen, List<IExposure> sitOut) = SteeringExposureSelector.Plan(at, potentialTarget.Project, potentialTarget, potentialTarget.SelectedExposure);
                    FilterCondition steered = chosen != null && conditions.TryGetValue(chosen, out FilterCondition c) ? c : null;
                    List<FilterCondition> held = sitOut.Where(conditions.ContainsKey).Select(p => conditions[p]).ToList();
                    score = ConditionMatch.SteeredScore(altitude, peak, conditions.Values.ToList(), steered, held, quality);
                    steering = $", steered to {chosen?.FilterName ?? "the default order"}{(sitOut.Count > 0 ? $", {string.Join(", ", sitOut.Select(p => p.FilterName))} under the score cut" : "")}";
                } else {
                    score = ConditionMatch.Score(altitude, peak, conditions.Values, quality);
                }
                TSLogger.Debug($"condition match {potentialTarget.Project.Name}/{potentialTarget.Name}: alt {altitude:0.0}/{peak:0.0}, glare {glare:0.00}, quality {(quality.HasValue ? quality.Value.ToString("0.00") : "unknown")}{steering}, score {score:0.000}");
                return score;
            } catch (Exception ex) {
                TSLogger.Warning($"condition match rule failed for {potentialTarget?.Name}, scoring 0: {ex.Message}");
                return 0;
            }
        }

        private static FilterCondition PanelCondition(ITarget target, IExposure plan, double glare, MoonState? moon, ObserverInfo observer, double altitude) {
            FilterCondition condition = SkyConditions.For(plan, glare, SkyConditions.KeepApplies(target, plan) ? moon : null);
            condition.PanelMatch = PanelBalance.ConditionFactor(target, plan.FilterName, glare, observer);
            condition.AltWorth = SkyConditions.AltWorth(target, plan, altitude);
            return condition;
        }
    }
}
