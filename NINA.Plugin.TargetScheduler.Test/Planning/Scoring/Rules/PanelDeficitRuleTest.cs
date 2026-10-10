using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Scoring.Rules {

    [TestFixture]
    public class PanelDeficitRuleTest {

        [TearDown]
        public void TearDown() {
            MosaicCompletion.GoalSource = null;
        }

        [Test]
        public void testRuleIdentity() {
            PanelDeficitRule sut = new PanelDeficitRule();
            sut.Name.Should().Be("Panel Deficit");
            sut.DefaultWeight.Should().Be(0);
            ScoringRule.GetAllScoringRules().Should().ContainKey(PanelDeficitRule.RULE_NAME);
        }

        [Test]
        public void testNotMosaicProject() {
            Mock<IProject> projectMock = GetProject(false);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 1));
            AddPanel(projectMock, "p2", 2, ("L", 10, 9));

            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().Be(0);
        }

        [Test]
        public void testOnePanelProject() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 1));

            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().Be(0);
        }

        [Test]
        public void testWeakestPanelFirst() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 2));
            Mock<ITarget> t2 = AddPanel(projectMock, "p2", 2, ("L", 10, 5));
            Mock<ITarget> t3 = AddPanel(projectMock, "p3", 3, ("L", 10, 9));

            PanelDeficitRule sut = new PanelDeficitRule();
            IScoringEngine engine = PlanMocks.GetMockScoringEnging().Object;
            double s1 = sut.Score(engine, t1.Object);
            double s2 = sut.Score(engine, t2.Object);
            double s3 = sut.Score(engine, t3.Object);

            s1.Should().BeApproximately(0.8, 0.00001);
            s2.Should().BeApproximately(0.25 / 0.8, 0.00001);
            s3.Should().BeApproximately(0.01 / 0.8, 0.00001);
            s1.Should().BeGreaterThan(s2);
            s2.Should().BeGreaterThan(s3);
        }

        [Test]
        public void testWeakestFilterLimitsPanel() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 9), ("R", 10, 3));
            Mock<ITarget> t2 = AddPanel(projectMock, "p2", 2, ("L", 10, 5), ("R", 10, 5));

            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.3, 0.00001);
            MosaicCompletion.MosaicProgress(projectMock.Object).Should().BeApproximately(0.3, 0.00001);

            PanelDeficitRule sut = new PanelDeficitRule();
            IScoringEngine engine = PlanMocks.GetMockScoringEnging().Object;
            sut.Score(engine, t1.Object).Should().BeApproximately(0.7, 0.00001);
            sut.Score(engine, t2.Object).Should().BeApproximately(0.25 / 0.7, 0.00001);
        }

        [Test]
        public void testCompletedPlansCount() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 6));
            PlanMocks.AddMockPlanFilterToCompleted(t1, PlanMocks.GetMockPlanExposure("R", 10, 10));
            AddPanel(projectMock, "p2", 2, ("L", 10, 2));

            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.6, 0.00001);
        }

        [Test]
        public void testDisabledPlansIgnored() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 6));
            Mock<IExposure> disabled = PlanMocks.GetMockPlanExposure("R", 10, 0);
            disabled.Object.IsEnabled = false;
            PlanMocks.AddMockPlanFilter(t1, disabled);

            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.6, 0.00001);
        }

        [Test]
        public void testPanelPastGoalScoresZero() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 10));
            AddPanel(projectMock, "p2", 2, ("L", 10, 3));

            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().Be(0);
            MosaicCompletion.IsPanelPastGoalWhileMosaicIncomplete(t1.Object).Should().BeTrue();
        }

        [Test]
        public void testAllPanelsPastGoal() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 10));
            AddPanel(projectMock, "p2", 2, ("L", 10, 10));

            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().Be(0);
            MosaicCompletion.IsPanelPastGoalWhileMosaicIncomplete(t1.Object).Should().BeFalse();
        }

        [Test]
        public void testGoalSourceUsedWhenKnown() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 9));
            Mock<ITarget> t2 = AddPanel(projectMock, "p2", 2, ("L", 10, 1));

            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("p1", "L")).Returns(new GoalProgress { TargetGuid = "p1", Filter = "L", Progress = 0.1 });
            source.Setup(m => m.Get("p2", "L")).Returns(new GoalProgress { TargetGuid = "p2", Filter = "L", Progress = 0.6 });
            MosaicCompletion.GoalSource = source.Object;

            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.1, 0.00001);
            MosaicCompletion.PanelProgress(t2.Object).Should().BeApproximately(0.6, 0.00001);

            PanelDeficitRule sut = new PanelDeficitRule();
            IScoringEngine engine = PlanMocks.GetMockScoringEnging().Object;
            sut.Score(engine, t1.Object).Should().BeApproximately(0.9, 0.00001);
            sut.Score(engine, t2.Object).Should().BeApproximately(0.16 / 0.9, 0.00001);
        }

        [Test]
        public void testGoalSourceFallback() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 4), ("R", 10, 9));
            AddPanel(projectMock, "p2", 2, ("L", 10, 8));

            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("p1", "R")).Returns(new GoalProgress { Progress = 0.2 });
            MosaicCompletion.GoalSource = source.Object;

            MosaicCompletion.PlanProgress(t1.Object, t1.Object.ExposurePlans[0]).Should().BeApproximately(0.4, 0.00001);
            MosaicCompletion.PlanProgress(t1.Object, t1.Object.ExposurePlans[1]).Should().BeApproximately(0.2, 0.00001);
            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.2, 0.00001);
        }

        [Test]
        public void testGoalDoneIsPastGoal() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 1), ("R", 10, 1));
            AddPanel(projectMock, "p2", 2, ("L", 10, 1));

            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("p1", "L")).Returns(new GoalProgress { Progress = 0.3, Plateau = true, Done = true });
            source.Setup(m => m.Get("p1", "R")).Returns(new GoalProgress { Progress = 1.1, Done = true });
            MosaicCompletion.GoalSource = source.Object;

            MosaicCompletion.PanelProgress(t1.Object).Should().Be(1);
            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().Be(0);
        }

        [Test]
        public void testPlateauWithoutPlateauStopIsNotPastGoal() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 1));
            AddPanel(projectMock, "p2", 2, ("L", 10, 5));

            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("p1", "L")).Returns(new GoalProgress { Progress = 0.4, Plateau = true, Done = false });
            MosaicCompletion.GoalSource = source.Object;

            MosaicCompletion.PanelProgress(t1.Object).Should().BeApproximately(0.4, 0.00001);
            MosaicCompletion.IsPanelPastGoalWhileMosaicIncomplete(t1.Object).Should().BeFalse();
            new PanelDeficitRule().Score(PlanMocks.GetMockScoringEnging().Object, t1.Object).Should().BeGreaterThan(0);
        }

        [Test]
        public void testGoalProgressBeyondOneIsClamped() {
            Mock<IProject> projectMock = GetProject(true);
            Mock<ITarget> t1 = AddPanel(projectMock, "p1", 1, ("L", 10, 1));
            AddPanel(projectMock, "p2", 2, ("L", 10, 5));

            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("p1", "L")).Returns(new GoalProgress { Progress = 1.4 });
            MosaicCompletion.GoalSource = source.Object;

            MosaicCompletion.PlanProgress(t1.Object, t1.Object.ExposurePlans[0]).Should().BeApproximately(1.4, 0.00001);
            MosaicCompletion.PanelProgress(t1.Object).Should().Be(1);
            MosaicCompletion.IsPanelPastGoalWhileMosaicIncomplete(t1.Object).Should().BeTrue();
        }

        public static Mock<IProject> GetProject(bool isMosaic) {
            Mock<IProject> projectMock = PlanMocks.GetMockPlanProject("mosaic", ProjectState.Active);
            projectMock.SetupProperty(m => m.IsMosaic, isMosaic);
            projectMock.SetupProperty(m => m.EnableGrader, true);
            projectMock.SetupProperty(m => m.ExposureCompletionHelper, new ExposureCompletionHelper(true, 0, 100));
            return projectMock;
        }

        public static Mock<ITarget> AddPanel(Mock<IProject> projectMock, string guid, int databaseId, params (string filter, int desired, int accepted)[] plans) {
            Mock<ITarget> targetMock = PlanMocks.GetMockPlanTarget($"Mosaic Panel {databaseId}", TestData.SPICA);
            targetMock.SetupProperty(m => m.Guid, guid);
            targetMock.SetupProperty(m => m.DatabaseId, databaseId);
            PlanMocks.AddMockPlanTarget(projectMock, targetMock);

            foreach (var (filter, desired, accepted) in plans) {
                PlanMocks.AddMockPlanFilter(targetMock, PlanMocks.GetMockPlanExposure(filter, desired, accepted));
            }

            return targetMock;
        }
    }
}
