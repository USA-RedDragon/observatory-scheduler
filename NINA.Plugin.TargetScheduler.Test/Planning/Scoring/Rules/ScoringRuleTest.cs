using FluentAssertions;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Scoring.Rules {

    [TestFixture]
    public class ScoringRuleTest {

        [Test]
        public void testGetAllScoringRules() {
            Dictionary<string, IScoringRule> map = ScoringRule.GetAllScoringRules();
            map.Should().NotBeEmpty();

            map.Should().ContainKey(ProjectPriorityRule.RULE_NAME);
            map[ProjectPriorityRule.RULE_NAME].Name.Should().Be(ProjectPriorityRule.RULE_NAME);

            map.Should().ContainKey(TargetSwitchPenaltyRule.RULE_NAME);
            map[TargetSwitchPenaltyRule.RULE_NAME].Name.Should().Be(TargetSwitchPenaltyRule.RULE_NAME);

            map.Should().ContainKey(PercentCompleteRule.RULE_NAME);
            map[PercentCompleteRule.RULE_NAME].Name.Should().Be(PercentCompleteRule.RULE_NAME);

            map.Should().ContainKey(SettingSoonestRule.RULE_NAME);
            map[SettingSoonestRule.RULE_NAME].Name.Should().Be(SettingSoonestRule.RULE_NAME);
        }
        [Test]
        public void testNewRulesAreOptInAtZero() {
            Dictionary<string, IScoringRule> map = ScoringRule.GetAllScoringRules();
            foreach (string name in new[] { ConditionMatchRule.RULE_NAME, SeasonalRunwayRule.RULE_NAME, SeasonPriorityRule.RULE_NAME, FilterSteeringRule.RULE_NAME }) {
                map.Should().ContainKey(name);
                map[name].DefaultWeight.Should().Be(0);
                (map[name] is IOptInScoringRule).Should().BeTrue();
            }

            (map[FilterSteeringRule.RULE_NAME] is IPlannerSwitch).Should().BeTrue();
        }

        [Test]
        public void testSwitchesAreNeverScored() {
            Dictionary<string, double> weights = new Dictionary<string, double> { { FilterSteeringRule.RULE_NAME, 100 }, { ProjectPriorityRule.RULE_NAME, 50 } };
            List<IScoringRule> rules = new NINA.Plugin.TargetScheduler.Planning.Scoring.ScoringEngine(null, null, System.DateTime.Now, null).AddAllRules(weights);
            rules.Should().ContainSingle(r => r.Name == ProjectPriorityRule.RULE_NAME);
            rules.Should().NotContain(r => r.Name == FilterSteeringRule.RULE_NAME);
            FilterSteeringRule.IsOn(weights).Should().BeTrue();
            FilterSteeringRule.IsOn(new Dictionary<string, double> { { FilterSteeringRule.RULE_NAME, 0 } }).Should().BeFalse();
            FilterSteeringRule.IsOn((Dictionary<string, double>)null).Should().BeFalse();
        }
    }
}
