using FluentAssertions;
using Moq;
using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NINA.Plugin.TargetScheduler.Test.Planning.Scoring.Rules;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Scoring {

    [TestFixture]
    public class ScoringEngineTest {

        [Test]
        public void TestOneRule() {
            Mock<IProfile> profileMock = new Mock<IProfile>();
            ProfilePreference profilePreference = new ProfilePreference();

            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add("TestRule1", 75);
            List<IScoringRule> rules = new List<IScoringRule>();
            rules.Add(new TestRule("TestRule1", 0.5));

            ScoringEngine sut = new ScoringEngine(profileMock.Object, profilePreference, DateTime.Now, null);
            sut.RuleWeights = ruleWeights;
            sut.Rules = rules;

            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            sut.ScoreTarget(pt.Object).Should().BeApproximately(0.375, 0.00001);
        }

        [Test]
        public void TestTwoRules() {
            Logger.SetLogLevel(LogLevelEnum.DEBUG);
            Mock<IProfile> profileMock = new Mock<IProfile>();
            ProfilePreference profilePreference = new ProfilePreference();

            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add("TestRule1", 100);
            ruleWeights.Add("TestRule2", 50);
            List<IScoringRule> rules = new List<IScoringRule>();
            rules.Add(new TestRule("TestRule1", 1));
            rules.Add(new TestRule("TestRule2", 0.5));

            ScoringEngine sut = new ScoringEngine(profileMock.Object, profilePreference, DateTime.Now, null);
            sut.RuleWeights = ruleWeights;
            sut.Rules = rules;

            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            sut.ScoreTarget(pt.Object).Should().BeApproximately(1.25, 0.00001);
        }

        [Test]
        public void TestMissingRuleWeightCountsAsZero() {
            Mock<IProfile> profileMock = new Mock<IProfile>();
            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add("TestRule1", 100);
            List<IScoringRule> rules = new List<IScoringRule>();
            rules.Add(new TestRule("TestRule1", 0.5));
            rules.Add(new TestRule("TestRule2", 1));

            ScoringEngine sut = new ScoringEngine(profileMock.Object, new ProfilePreference(), DateTime.Now, null);
            sut.RuleWeights = ruleWeights;
            sut.Rules = rules;

            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            sut.ScoreTarget(pt.Object).Should().BeApproximately(0.5, 0.00001);
        }

        [Test]
        public void TestAddAllRulesWithMissingWeights() {
            Mock<IProfile> profileMock = new Mock<IProfile>();
            ScoringEngine sut = new ScoringEngine(profileMock.Object, new ProfilePreference(), DateTime.Now, null);

            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add(ProjectPriorityRule.RULE_NAME, 50);

            List<IScoringRule> active = sut.AddAllRules(ruleWeights);
            active.Should().HaveCount(1);
            active[0].Name.Should().Be(ProjectPriorityRule.RULE_NAME);

            ScoringEngine.GetRuleWeight(ruleWeights, PanelDeficitRule.RULE_NAME).Should().Be(0);
            ScoringEngine.GetRuleWeight(null, PanelDeficitRule.RULE_NAME).Should().Be(0);
            ScoringEngine.GetRuleWeight(ruleWeights, ProjectPriorityRule.RULE_NAME).Should().Be(50);
        }

        [Test]
        public void TestPanelPastGoalGetsZeroPriority() {
            Mock<IProject> projectMock = PanelDeficitRuleTest.GetProject(true);
            Mock<ITarget> done = PanelDeficitRuleTest.AddPanel(projectMock, "p1", 1, ("L", 10, 10));
            Mock<ITarget> behind = PanelDeficitRuleTest.AddPanel(projectMock, "p2", 2, ("L", 10, 4));

            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add("TestRule1", 100);
            ruleWeights.Add(PanelDeficitRule.RULE_NAME, 50);
            List<IScoringRule> rules = new List<IScoringRule>();
            rules.Add(new TestRule("TestRule1", 1));
            rules.Add(new PanelDeficitRule());

            ScoringEngine sut = new ScoringEngine(new Mock<IProfile>().Object, new ProfilePreference(), DateTime.Now, null);
            sut.RuleWeights = ruleWeights;
            sut.Rules = rules;

            sut.ScoreTarget(done.Object).Should().Be(0);
            done.Object.ScoringResults.TotalScore.Should().Be(0);
            done.Object.ScoringResults.Results.Should().HaveCount(2);
            sut.ScoreTarget(behind.Object).Should().BeApproximately(1 + 0.5 * 0.6, 0.00001);
        }

        [Test]
        public void TestPanelPastGoalUnchangedWithoutPanelDeficitWeight() {
            Mock<IProject> projectMock = PanelDeficitRuleTest.GetProject(true);
            Mock<ITarget> done = PanelDeficitRuleTest.AddPanel(projectMock, "p1", 1, ("L", 10, 10));
            PanelDeficitRuleTest.AddPanel(projectMock, "p2", 2, ("L", 10, 4));

            Dictionary<string, double> ruleWeights = new Dictionary<string, double>();
            ruleWeights.Add("TestRule1", 100);
            List<IScoringRule> rules = new List<IScoringRule>();
            rules.Add(new TestRule("TestRule1", 1));

            ScoringEngine sut = new ScoringEngine(new Mock<IProfile>().Object, new ProfilePreference(), DateTime.Now, null);
            sut.RuleWeights = ruleWeights;
            sut.Rules = rules;

            sut.ScoreTarget(done.Object).Should().BeApproximately(1, 0.00001);
        }
    }

    internal class TestRule : ScoringRule {
        private string name;
        private double score;

        public TestRule(string name, double score) {
            this.name = name;
            this.score = score;
        }

        public override string Name { get { return name; } }
        public override double DefaultWeight { get { return 1; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            return score;
        }
    }
}