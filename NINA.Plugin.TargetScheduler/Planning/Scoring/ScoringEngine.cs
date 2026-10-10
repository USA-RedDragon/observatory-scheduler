using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring {

    public class ScoringEngine : IScoringEngine {
        public IProfile ActiveProfile { get; set; }
        public ProfilePreference ProfilePreference { get; set; }
        public DateTime AtTime { get; set; }
        public ITarget PreviousPlanTarget { get; }
        public List<IScoringRule> Rules { get; set; }

        private Dictionary<string, double> ruleWeights;

        public Dictionary<string, double> RuleWeights {
            get { return ruleWeights; }
            set { ruleWeights = value; Rules = null; }
        }

        public ScoringEngine(IProfile activeProfile, ProfilePreference ProfilePreference, DateTime atTime, ITarget previousTarget) {
            this.ActiveProfile = activeProfile;
            this.ProfilePreference = ProfilePreference;
            this.PreviousPlanTarget = previousTarget;
            this.AtTime = atTime;
        }

        public double ScoreTarget(ITarget target) {
            if (Rules == null) {
                Rules = AddAllRules(RuleWeights);
            }

            target.ScoringResults = new ScoringResults();

            double totalScore = 0;
            foreach (ScoringRule rule in Rules) {
                double weight = GetRuleWeight(RuleWeights, rule.Name) / ScoringRule.WEIGHT_SCALE;
                double score = rule.Score(this, target);
                totalScore += weight * score;
                target.ScoringResults.AddRuleResult(new RuleResult(rule, weight, score));
            }

            if (GetRuleWeight(RuleWeights, PanelDeficitRule.RULE_NAME) > 0 && MosaicCompletion.IsPanelPastGoalWhileMosaicIncomplete(target)) {
                totalScore = 0;
            }

            target.ScoringResults.TotalScore = totalScore;
            return totalScore;
        }

        public List<IScoringRule> AddAllRules(Dictionary<string, double> ruleWeights) {
            Dictionary<string, IScoringRule> allRules = ScoringRule.GetAllScoringRules();
            List<IScoringRule> activeRules = new List<IScoringRule>();

            foreach (KeyValuePair<string, IScoringRule> item in allRules) {
                if (item.Value is IPlannerSwitch) { continue; }
                if (GetRuleWeight(ruleWeights, item.Value.Name) > 0) {
                    activeRules.Add(item.Value);
                }
            }

            return activeRules;
        }

        public static double GetRuleWeight(Dictionary<string, double> ruleWeights, string ruleName) {
            if (ruleWeights == null || ruleName == null) {
                return 0;
            }

            return ruleWeights.TryGetValue(ruleName, out double weight) ? weight : 0;
        }
    }
}