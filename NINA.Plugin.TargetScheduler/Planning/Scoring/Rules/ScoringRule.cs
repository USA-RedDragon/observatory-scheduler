using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public abstract class ScoringRule : IScoringRule {
        public const double WEIGHT_SCALE = 100;

        public abstract string Name { get; }
        public abstract double DefaultWeight { get; }

        /// <summary>
        /// Apply the rule and return a score in the range 0-1.
        /// </summary>
        /// <param name="scoringEngine"></param>
        /// <param name="potentialTarget"></param>
        /// <returns></returns>
        public abstract double Score(IScoringEngine scoringEngine, ITarget potentialTarget);

        public static Dictionary<string, IScoringRule> GetAllScoringRules() {
            IEnumerable<Type> ruleTypes = Assembly.GetAssembly(typeof(ScoringRule)).GetTypes().
                    Where(ruleType => ruleType.IsClass && !ruleType.IsAbstract && ruleType.IsSubclassOf(typeof(ScoringRule)));

            Dictionary<string, IScoringRule> map = new Dictionary<string, IScoringRule>();
            foreach (Type ruleType in ruleTypes) {
                ScoringRule rule = (ScoringRule)Activator.CreateInstance(ruleType);
                map.Add(rule.Name, rule);
            }

            return map;
        }

        public static List<RuleWeight> GetDefaultRuleWeights() {
            Dictionary<string, IScoringRule> rules = GetAllScoringRules();
            List<RuleWeight> ruleWeights = new List<RuleWeight>(rules.Count);
            foreach (KeyValuePair<string, IScoringRule> entry in rules) {
                var rule = entry.Value;
                ruleWeights.Add(new RuleWeight(rule.Name, rule.DefaultWeight));
            }

            return ruleWeights;
        }
    }

    public class ScoringResults {
        public double TotalScore { get; set; }
        public List<RuleResult> Results { get; private set; }

        /// <summary>
        /// Why the planner kept this running target over a challenger that scored within the tie band, or null.
        /// </summary>
        public string Held { get; set; }

        public ScoringResults() {
            Results = new List<RuleResult>();
        }

        public void AddRuleResult(RuleResult ruleResult) {
            Results.Add(ruleResult);
        }
    }

    public class RuleResult {
        public ScoringRule ScoringRule { get; private set; }
        public double Weight { get; private set; }
        public double Score { get; private set; }

        public RuleResult(ScoringRule scoringRule, double weight, double score) {
            ScoringRule = scoringRule;
            Weight = weight;
            Score = score;
        }
    }
}