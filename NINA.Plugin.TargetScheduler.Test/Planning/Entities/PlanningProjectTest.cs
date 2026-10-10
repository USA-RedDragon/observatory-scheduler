using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Entities {

    [TestFixture]
    public class PlanningProjectTest {

        [Test]
        public void TestPlanningProject() {
            DateTime create = new DateTime(2024, 11, 11, 1, 2, 3);
            Mock<IProfile> profileMock = new Mock<IProfile>();
            Project project = new Project("abc123");
            project.Id = 101;
            project.Name = "project";
            project.CreateDate = create;
            project.MinimumTime = 30;
            project.MinimumAltitude = 25;
            project.MaximumAltitude = 80;
            project.HorizonOffset = 3;
            project.MeridianWindow = 120;
            project.FilterSwitchFrequency = 4;
            project.DitherEvery = 6;
            project.SmartExposureOrder = true;
            project.FlatsHandling = Project.FLATS_HANDLING_TARGET_COMPLETION;

            PlanningProject sut = new PlanningProject(profileMock.Object, project, new ExposureCompletionHelper(false, 0, 0));

            sut.PlanId.Should().NotBeNull();
            sut.DatabaseId.Should().Be(101);
            sut.Guid.Should().Be(project.Guid).And.NotBeNullOrEmpty();
            sut.Name.Should().Be("project");
            sut.State.Should().Be(ProjectState.Draft);
            sut.Priority.Should().Be(ProjectPriority.Normal);
            sut.CreateDate.Should().Be(create);
            sut.MinimumTime.Should().Be(30);
            sut.MinimumAltitude.Should().Be(25);
            sut.MaximumAltitude.Should().Be(80);
            sut.UseCustomHorizon.Should().BeFalse();
            sut.HorizonOffset.Should().Be(3);
            sut.MeridianWindow.Should().Be(120);
            sut.FilterSwitchFrequency.Should().Be(4);
            sut.DitherEvery.Should().Be(6);
            sut.SmartExposureOrder.Should().BeTrue();
            sut.EnableGrader.Should().BeTrue();
            sut.IsMosaic.Should().BeFalse();
            sut.FlatsHandling.Should().Be(Project.FLATS_HANDLING_TARGET_COMPLETION);
            sut.RuleWeights.Should().NotBeNull().And.HaveCount(9);
            sut.Rejected.Should().BeFalse();
            sut.HorizonDefinition.Should().NotBeNull();
            sut.ExposureCompletionHelper.Should().NotBeNull();
            sut.Targets.Should().NotBeNull();
        }

        [Test]
        public void TestMissingRuleWeightsFilledWithZero() {
            Mock<IProfile> profileMock = new Mock<IProfile>();
            Project project = new Project("abc123");
            project.RuleWeights = new List<RuleWeight> {
                new RuleWeight(ProjectPriorityRule.RULE_NAME, 40),
                new RuleWeight(MosaicCompletionRule.RULE_NAME, 25)
            };

            PlanningProject sut = new PlanningProject(profileMock.Object, project, new ExposureCompletionHelper(false, 0, 0));

            sut.RuleWeights.Keys.Should().BeEquivalentTo(ScoringRule.GetAllScoringRules().Keys);
            sut.RuleWeights[ProjectPriorityRule.RULE_NAME].Should().Be(40);
            sut.RuleWeights[MosaicCompletionRule.RULE_NAME].Should().Be(25);
            sut.RuleWeights[PanelDeficitRule.RULE_NAME].Should().Be(0);
            sut.RuleWeights[PercentCompleteRule.RULE_NAME].Should().Be(0);
        }

        [Test]
        public void TestRuleWeightsDictionaryKeepsExplicitWeights() {
            Dictionary<string, double> sut = PlanningProject.GetRuleWeightsDictionary(new List<RuleWeight> { new RuleWeight(PanelDeficitRule.RULE_NAME, 70) });
            sut[PanelDeficitRule.RULE_NAME].Should().Be(70);
            sut.Should().HaveCount(ScoringRule.GetAllScoringRules().Count);

            PlanningProject.GetRuleWeightsDictionary(null).Values.Should().OnlyContain(w => w == 0);
        }
    }
}