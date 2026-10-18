using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Exposures {

    [TestFixture]
    public class BootstrapRotationTest {
        private const string Guid = "sculptor";
        private Mock<IGoalProgressSource> source;

        [SetUp]
        public void SetUp() {
            DitherManagerCache.Clear();
            source = new Mock<IGoalProgressSource>();
            MosaicCompletion.GoalSource = source.Object;
            SkyConditions.CurrentObserver = null;
        }

        [TearDown]
        public void TearDown() {
            MosaicCompletion.GoalSource = null;
            PlannerSignals.Current = null;
        }

        private static void PoorSky() {
            Sky(0.10);
        }

        private static void Sky(double quality) {
            PlannerSignals.Current = new PlannerSignals(new[] {
                new GoalProgressRow { TargetGuid = Guid, Filter = "Red", NightQuality = quality, NightQualityAt = DateTime.UtcNow }
            });
        }

        private void Goal(string filter, GoalState state, double progress = 0, double? cut = null, double? expected = null) {
            source.Setup(m => m.Get(Guid, filter)).Returns(new GoalProgress {
                TargetGuid = Guid, Filter = filter, State = state, Progress = progress, StackSubs = state == GoalState.Measured ? 8 : 0, MinSubs = 8,
                EffectiveHours = state == GoalState.Measured ? 1 : 0, GainPerHourPct = state == GoalState.Measured ? 10 : 0,
                ScoreCut = cut, ExpectedScore = expected
            });
        }

        [Test]
        public void AFreshTargetUnderAPoorSkyTakesHydrogenAlphaWhenBroadbandWouldFailItsCut() {
            var (pp, pt) = Sculptor("Red", "Green", "Blue", "H-a", "Luminance");
            foreach (string f in new[] { "Red", "Green", "Blue", "Luminance" }) { Goal(f, GoalState.Collecting, cut: 0.27, expected: 0.11); }
            Goal("H-a", GoalState.Collecting);
            PoorSky();

            Night(pp, pt, 4).Should().Equal("H-a", "H-a", "H-a", "H-a");
        }

        [Test]
        public void AFreshTargetUnderAGoodSkyRotatesWhenBroadbandCanPassItsCut() {
            var (pp, pt) = Sculptor("Red", "Green", "Blue", "H-a", "Luminance");
            foreach (string f in new[] { "Red", "Green", "Blue", "Luminance" }) { Goal(f, GoalState.Collecting, cut: 0.27, expected: 0.70); }
            Goal("H-a", GoalState.Collecting);
            Sky(0.70);

            Night(pp, pt, 8).Should().Equal("Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue", "Luminance");
        }

        private static (Mock<IProject> pp, Mock<ITarget> pt) Sculptor(params string[] filters) {
            Mock<IProject> pp = PlanMocks.GetMockPlanProject("Sculptor Galaxy", ProjectState.Active);
            pp.SetupProperty(p => p.FilterSwitchFrequency, 1);
            pp.SetupProperty(p => p.DitherEvery, 0);
            pp.SetupProperty(p => p.SmartExposureOrder, false);
            pp.SetupProperty(p => p.ExposureCompletionHelper, new ExposureCompletionHelper(true, 0, 125));
            pp.Object.RuleWeights[FilterSteeringRule.RULE_NAME] = 1;
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("Sculptor Galaxy", TestData.M31);
            pt.SetupProperty(t => t.Project, pp.Object);
            pt.SetupProperty(t => t.Guid, Guid);
            pt.SetupProperty(t => t.IsPreview, true);
            foreach (var (f, i) in filters.Select((f, i) => (f, i))) {
                PlanMocks.AddMockPlanFilter(pt, PlanMocks.GetMockPlanExposure(f, 50, 0, 300, 600 + i));
            }
            return (pp, pt);
        }

        private static List<string> Night(Mock<IProject> pp, Mock<ITarget> pt, int exposures) {
            IExposureSelector sut = new ExposureSelectionExpert().GetExposureSelector(pp.Object, pt.Object, new Target());
            sut.Should().BeOfType<SteeringExposureSelector>();
            var taken = new List<string>();
            for (int i = 0; i < exposures; i++) {
                IExposure e = sut.Select(DateTime.Now, pp.Object, pt.Object);
                taken.Add(e.FilterName);
                sut.ExposureTaken(e);
            }
            return taken;
        }

        [Test]
        public void AFreshTargetUnderAPoorSkyRotatesEveryNonHydrogenAlphaFilter() {
            var (pp, pt) = Sculptor("Red", "Green", "Blue", "H-a", "Luminance");
            foreach (string f in new[] { "Red", "Green", "Blue", "H-a", "Luminance" }) { Goal(f, GoalState.Collecting); }
            PoorSky();

            Night(pp, pt, 10).Should().Equal("Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue", "Luminance", "Red", "Green");
        }

        [Test]
        public void MeasuredGoalsUnderAPoorSkyStillSteerToHydrogenAlpha() {
            var (pp, pt) = Sculptor("Red", "Green", "Blue", "H-a", "Luminance");
            foreach (string f in new[] { "Red", "Green", "Blue", "H-a", "Luminance" }) { Goal(f, GoalState.Measured, 0.3); }
            PoorSky();

            Night(pp, pt, 4).Should().Equal("H-a", "H-a", "H-a", "H-a");
        }

        [Test]
        public void TheCadenceFollowsTheFilterSteeringShot() {
            var (pp, pt) = Sculptor("Red", "Green", "Blue", "H-a", "Luminance");
            var inner = new BasicExposureSelector(pp.Object, pt.Object, new Target());
            inner.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("Red");
            inner.Follow(pt.Object.ExposurePlans[2]).Should().BeTrue();
            inner.ExposureTaken(pt.Object.ExposurePlans[2]);

            inner.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("H-a");
        }
    }
}
