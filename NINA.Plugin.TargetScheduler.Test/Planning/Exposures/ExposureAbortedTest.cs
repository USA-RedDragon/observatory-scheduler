using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Exposures {

    [TestFixture]
    public class ExposureAbortedTest {

        [SetUp]
        public void SetUp() {
            DitherManagerCache.Clear();
            SmartExposureRotateCache.Clear();
        }

        private static (Mock<IProject> pp, Mock<ITarget> pt) Lrgb(int switchEvery, int ditherEvery, bool smart = false, int databaseId = 0) {
            Mock<IProject> pp = PlanMocks.GetMockPlanProject("P1", ProjectState.Active);
            pp.SetupAllProperties();
            pp.SetupProperty(p => p.FilterSwitchFrequency, switchEvery);
            pp.SetupProperty(p => p.DitherEvery, ditherEvery);
            pp.SetupProperty(p => p.SmartExposureOrder, smart);
            pp.SetupProperty(p => p.ExposureCompletionHelper, new ExposureCompletionHelper(true, 0, 125));
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("T1", TestData.M31);
            pt.SetupProperty(t => t.Project, pp.Object);
            pt.SetupProperty(t => t.DatabaseId, databaseId);
            foreach (var (f, i) in new[] { "L", "R", "G", "B" }.Select((f, i) => (f, i))) {
                PlanMocks.AddMockPlanFilter(pt, PlanMocks.GetMockPlanExposure(f, 10, 0, 100 + i));
            }
            return (pp, pt);
        }

        private static IExposure Take(IExposureSelector sut, IProject p, ITarget t) {
            IExposure e = sut.Select(DateTime.Now, p, t);
            sut.ExposureTaken(e);
            return e;
        }

        [Test]
        public void BasicSelectorRetriesTheAbortedFilterAndPersistsTheRestoredCadence() {
            var (pp, pt) = Lrgb(1, 0, databaseId: 4242);
            string dbPath = Path.Combine(Path.GetTempPath(), "scheduler-unittest-abort.sqlite");
            if (File.Exists(dbPath)) { File.Delete(dbPath); }
            var db = new SchedulerDatabaseInteraction(string.Format(@"Data Source={0};", dbPath));

            var mock = new Mock<BasicExposureSelector>(pp.Object, pt.Object, new Target()) { CallBase = true };
            mock.Setup(m => m.GetSchedulerDatabaseContext()).Returns(() => db.GetContext());
            BasicExposureSelector sut = mock.Object;

            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("L");
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            aborted.FilterName.Should().Be("R");

            List<FilterCadenceItem> during;
            using (var context = db.GetContext()) {
                during = context.GetFilterCadences(4242);
            }
            during.Single(fc => fc.Next).ReferenceIdx.Should().Be(2, "the preview reads the cadence during the exposure and expects it already advanced");

            sut.ExposureAborted(aborted);

            List<FilterCadenceItem> after;
            using (var context = db.GetContext()) {
                after = context.GetFilterCadences(4242);
            }
            after.Single(fc => fc.Next).ReferenceIdx.Should().Be(1);
            sut.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("R");

            var replanned = new BasicExposureSelector(pp.Object, pt.Object, new Target { FilterCadences = after });
            replanned.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("R");
        }

        [Test]
        public void BasicSelectorDoesNotCountTheAbortedFrameTowardTheNextDither() {
            var (pp, pt) = Lrgb(3, 2);
            var mock = new Mock<BasicExposureSelector>(pp.Object, pt.Object, new Target()) { CallBase = true };
            mock.Setup(m => m.GetSchedulerDatabaseContext()).Returns((SchedulerDatabaseContext)null);
            pt.SetupProperty(t => t.IsPreview, true);
            BasicExposureSelector sut = mock.Object;

            Take(sut, pp.Object, pt.Object).PreDither.Should().BeFalse();
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            aborted.PreDither.Should().BeFalse();
            sut.ExposureAborted(aborted);

            IExposure retry = sut.Select(DateTime.Now, pp.Object, pt.Object);
            retry.FilterName.Should().Be("L");
            retry.PreDither.Should().BeFalse();
            sut.ExposureTaken(retry);

            IExposure third = sut.Select(DateTime.Now, pp.Object, pt.Object);
            third.FilterName.Should().Be("L");
            third.PreDither.Should().BeTrue();
        }

        [Test]
        public void ADitherThatRanBeforeTheAbortedFrameStillCounts() {
            var (pp, pt) = Lrgb(0, 1);
            var sut = new RepeatUntilDoneExposureSelector(pp.Object, pt.Object, new Target());

            Take(sut, pp.Object, pt.Object).PreDither.Should().BeFalse();
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            aborted.PreDither.Should().BeTrue();
            sut.ExposureAborted(aborted);

            sut.Select(DateTime.Now, pp.Object, pt.Object).PreDither.Should().BeFalse();
        }

        [Test]
        public void AbortingTwiceOrWithoutATakenExposureChangesNothing() {
            var (pp, pt) = Lrgb(0, 1);
            var sut = new RepeatUntilDoneExposureSelector(pp.Object, pt.Object, new Target());
            sut.ExposureAborted(pt.Object.ExposurePlans[0]);

            IExposure first = Take(sut, pp.Object, pt.Object);
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            sut.ExposureAborted(aborted);
            sut.ExposureAborted(aborted);

            sut.Select(DateTime.Now, pp.Object, pt.Object).PreDither.Should().BeFalse();
            sut.ExposureTaken(first);
            sut.Select(DateTime.Now, pp.Object, pt.Object).PreDither.Should().BeTrue();
        }

        [Test]
        public void OverrideOrderRetriesTheAbortedExposure() {
            var (pp, pt) = Lrgb(100, 100);
            var target = new Target {
                OverrideExposureOrders = new List<OverrideExposureOrderItem> {
                    new OverrideExposureOrderItem(101, 1, OverrideExposureOrderAction.Exposure, 0),
                    new OverrideExposureOrderItem(101, 2, OverrideExposureOrderAction.Exposure, 1),
                    new OverrideExposureOrderItem(101, 3, OverrideExposureOrderAction.Exposure, 2),
                },
            };
            pt.SetupProperty(t => t.IsPreview, true);
            var sut = new OverrideOrderExposureSelector(pp.Object, pt.Object, target);

            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("L");
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            aborted.FilterName.Should().Be("R");
            sut.ExposureAborted(aborted);

            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("R");
            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("G");
        }

        [Test]
        public void SmartRotationGivesTheAbortedFilterItsSlotBack() {
            var (pp, pt) = Lrgb(2, 0, smart: true);
            pt.Object.ExposurePlans[0].MoonAvoidanceScore = .10;
            pt.Object.ExposurePlans[1].MoonAvoidanceScore = .41;
            pt.Object.ExposurePlans[2].MoonAvoidanceScore = .40;
            pt.Object.ExposurePlans[3].MoonAvoidanceScore = .40;
            var sut = new SmartExposureSelector(pp.Object, pt.Object, new Target());

            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("R");
            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("R");
            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("G");
            IExposure aborted = Take(sut, pp.Object, pt.Object);
            aborted.FilterName.Should().Be("G");
            sut.ExposureAborted(aborted);

            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("G");
            Take(sut, pp.Object, pt.Object).FilterName.Should().Be("B");
        }

        [Test]
        public void SteeringPassesTheAbortToTheSelectorItWraps() {
            var (pp, pt) = Lrgb(1, 0);
            pt.SetupProperty(t => t.IsPreview, true);
            var inner = new BasicExposureSelector(pp.Object, pt.Object, new Target());
            var sut = new SteeringExposureSelector(pp.Object, pt.Object, inner);

            inner.Select(DateTime.Now, pp.Object, pt.Object);
            sut.ExposureTaken(pt.Object.ExposurePlans[0]);
            inner.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("R");
            sut.ExposureTaken(pt.Object.ExposurePlans[1]);
            sut.ExposureAborted(pt.Object.ExposurePlans[1]);

            inner.Select(DateTime.Now, pp.Object, pt.Object).FilterName.Should().Be("R");
        }
    }
}
