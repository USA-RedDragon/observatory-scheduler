using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Exposures {

    [TestFixture]
    public class GoalDrivenCompletionTest {
        private Mock<IGoalProgressSource> source;

        [SetUp]
        public void SetUp() {
            source = new Mock<IGoalProgressSource>();
            MosaicCompletion.GoalSource = source.Object;
        }

        [TearDown]
        public void TearDown() {
            MosaicCompletion.GoalSource = null;
        }

        private static (Mock<ITarget> target, Mock<IExposure> exposure) Plan(string guid, string filter, int desired, int accepted, int exposureLength = 300) {
            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("t", TestData.M42);
            target.SetupProperty(m => m.Guid, guid);
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure(filter, desired, accepted, exposureLength, 1);
            PlanMocks.AddMockPlanFilter(target, exposure);
            return (target, exposure);
        }

        [Test]
        public void GoalNotDoneKeepsAPlanWithAllItsCountsIncomplete() {
            var (target, exposure) = Plan("t1", "Ha", 10, 25);
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 0.5, HoursNeeded = 1 });
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.PercentComplete(exposure.Object).Should().BeApproximately(50, 0.0001);
            sut.IsIncomplete(exposure.Object).Should().BeTrue();
            sut.RemainingExposures(exposure.Object).Should().Be(12);
            sut.IsProvisionalPercentComplete(exposure.Object).Should().BeFalse();
            sut.IsIncomplete(target.Object).Should().BeTrue();
        }

        [Test]
        public void GoalPastOneButNotDoneStaysIncomplete() {
            var (target, exposure) = Plan("t1", "Ha", 10, 25);
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 1.3 });
            ExposureCompletionHelper sut = new ExposureCompletionHelper(false, 0, 100);

            sut.PercentComplete(exposure.Object).Should().BeLessThan(100);
            sut.RemainingExposures(exposure.Object).Should().Be(1);
            sut.IsIncomplete(target.Object).Should().BeTrue();
        }

        [Test]
        public void GoalDoneCompletesAPlanRegardlessOfCounts() {
            var (target, exposure) = Plan("t1", "Ha", 10, 0);
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 0.7, Plateau = true, Done = true });
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.PercentComplete(exposure.Object).Should().Be(100);
            sut.IsIncomplete(exposure.Object).Should().BeFalse();
            sut.RemainingExposures(exposure.Object).Should().Be(0);
            sut.IsIncomplete(target.Object).Should().BeFalse();
        }

        [Test]
        public void CollectingIgnoresTheDesiredCountUntilTheStackersMinimum() {
            var (target, exposure) = Plan("t1", "Ha", 10, 25);
            exposure.SetupProperty(m => m.Acquired, 25);
            GoalProgress goal = new GoalProgress { State = GoalState.Collecting, StackSubs = 5, MinSubs = 8, SubLimit = 32 };
            source.Setup(m => m.Get("t1", "Ha")).Returns(goal);
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.IsIncomplete(exposure.Object).Should().BeTrue();
            sut.RemainingExposures(exposure.Object).Should().Be(3);
            sut.IsIncomplete(target.Object).Should().BeTrue();

            goal.StackSubs = 8;
            sut.IsIncomplete(exposure.Object).Should().BeFalse();
            sut.RemainingExposures(exposure.Object).Should().Be(0);
            sut.IsIncomplete(target.Object).Should().BeFalse();
        }

        [Test]
        public void SeasonalRunwayUsesTheCountForAnUnmeasuredGoal() {
            var (target, exposure) = Plan("t1", "Ha", 30, 4, 600);
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { State = GoalState.Collecting, StackSubs = 5, MinSubs = 8, SubLimit = 32 });
            ExposureCompletionHelper sut = target.Object.Project.ExposureCompletionHelper;

            sut.RemainingExposures(exposure.Object).Should().Be(3);
            sut.CountRemainingExposures(exposure.Object).Should().Be(26);

            List<RunwayFilter> filters = SeasonalRunwayRule.Filters(target.Object);
            filters.Should().ContainSingle().Which.CountHours.Should().BeApproximately(26 * 600 / 3600.0, 1e-9);
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(26 * 600 / 3600.0, 1e-9);
        }

        [Test]
        public void UnmeasurableStopsAtTheBound() {
            var (target, exposure) = Plan("t1", "Ha", 1000, 0);
            GoalProgress goal = new GoalProgress { State = GoalState.Unmeasurable, StackSubs = 31, MinSubs = 8, SubLimit = 32 };
            source.Setup(m => m.Get("t1", "Ha")).Returns(goal);
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.IsIncomplete(exposure.Object).Should().BeTrue();
            goal.StackSubs = 32;
            sut.IsIncomplete(exposure.Object).Should().BeFalse();
            sut.IsIncomplete(target.Object).Should().BeFalse();
        }

        [Test]
        public void NoProgressRowFallsBackToCounts() {
            var (target, exposure) = Plan("t1", "Ha", 10, 4);
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.PercentComplete(exposure.Object).Should().BeApproximately(40, 0.0001);
            sut.RemainingExposures(exposure.Object).Should().Be(6);

            MosaicCompletion.GoalSource = null;
            sut.PercentComplete(exposure.Object).Should().BeApproximately(40, 0.0001);
        }

        [Test]
        public void MixedTargetIsCompleteOnlyWhenEveryPlanIs() {
            var (target, ha) = Plan("t1", "Ha", 10, 0);
            Mock<IExposure> oiii = PlanMocks.GetMockPlanExposure("OIII", 10, 10, 300, 2);
            PlanMocks.AddMockPlanFilter(target, oiii);
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 0.9, Done = true });
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.IsIncomplete(target.Object).Should().BeFalse();
            sut.PercentComplete(target.Object).Should().Be(100);

            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 0.9, Done = false });
            sut.IsIncomplete(target.Object).Should().BeTrue();
            sut.PercentComplete(target.Object).Should().BeApproximately(95, 0.0001);
        }

        [Test]
        public void DatabaseEntitiesUseTheTargetGuidAndTemplateFilter() {
            Target target = new Target { Guid = "db-target" };
            ExposureTemplate template = new ExposureTemplate("p", "Ha", "Ha") { DefaultExposure = 120 };
            ExposurePlan plan = new ExposurePlan("p") { Desired = 10, Accepted = 10, ExposureTemplate = template, Target = target };
            target.ExposurePlans = new List<ExposurePlan> { plan };
            source.Setup(m => m.Get("db-target", "Ha")).Returns(new GoalProgress { Progress = 0.25, HoursNeeded = 1 });
            ExposureCompletionHelper sut = new ExposureCompletionHelper(true, 0, 100);

            sut.PercentComplete(target).Should().BeApproximately(25, 0.0001);
            sut.RemainingExposures(plan).Should().Be(30);

            plan.Target = null;
            sut.PercentComplete(target).Should().BeApproximately(25, 0.0001);
        }

        [Test]
        public void PercentCompleteAndMosaicRulesFollowGoals() {
            Mock<IProject> project = Scoring.Rules.PanelDeficitRuleTest.GetProject(true);
            Mock<ITarget> t1 = Scoring.Rules.PanelDeficitRuleTest.AddPanel(project, "p1", 1, ("L", 10, 10));
            Mock<ITarget> t2 = Scoring.Rules.PanelDeficitRuleTest.AddPanel(project, "p2", 2, ("L", 10, 10));
            source.Setup(m => m.Get("p1", "L")).Returns(new GoalProgress { Progress = 0.2 });
            source.Setup(m => m.Get("p2", "L")).Returns(new GoalProgress { Progress = 0.6 });

            IScoringEngine engine = PlanMocks.GetMockScoringEnging().Object;
            new PercentCompleteRule().Score(engine, t1.Object).Should().BeApproximately(0.2, 0.0001);
            new MosaicCompletionRule().Score(engine, t1.Object).Should().BeApproximately(0.4, 0.0001);
            new MosaicCompletionRule().Score(engine, t2.Object).Should().Be(0);
        }
    }
}
