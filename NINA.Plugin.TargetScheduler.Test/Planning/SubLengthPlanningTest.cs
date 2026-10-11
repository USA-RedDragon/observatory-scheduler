using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class SubLengthPlanningTest {
        private static readonly DateTime At = new DateTime(2023, 12, 17, 23, 0, 0);

        [TearDown]
        public void Reset() {
            SubLengthTable.Current = SubLengthTable.Empty;
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void PlannerUsesSystemLengthForLiveAndPreview(bool preview) {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            (Mock<ITarget> pt, Mock<IExposure> pf) = Target();

            double glare = SkyConditions.MoonGlare(SkyConditions.Observer(profile), TestData.M42, At);
            SubLengthTable.Current = new SubLengthTable(new[] {
                new SubLengthRow { TargetGuid = "g1", Filter = "Ha", Gain = 100, Bucket = SubLength.Bucket(glare), Exposure = 120 }
            });

            Planner planner = new Planner(At, profile, new NINA.Plugin.TargetScheduler.Database.Schema.ProfilePreference("abcd-1234"), PlanMocks.GetWeatherDataMediator(false, 0), false, preview);
            planner.SelectTargetExposures(new List<ITarget> { pt.Object });

            pt.Object.SelectedExposure.Should().BeSameAs(pf.Object);
            pf.Object.ExposureLength.Should().Be(120);
            pf.Object.LengthSource.Should().Be(SubLength.SourceSystem);
            pf.Object.LengthBucket.Should().Be(SubLength.Bucket(glare));
            new SchedulerPlan(At, null, pt.Object, new List<IInstruction>(), false).EndTime.Should().Be(At.AddSeconds(120));
        }

        [Test]
        public void ApplyFallsBackToPlanThenTemplate() {
            (Mock<ITarget> pt, Mock<IExposure> pf) = Target();
            SkyConditions.CurrentObserver = TestData.Pittsboro_NC;

            pf.Object.PlanExposureLength = 240;
            SubLengthPlanning.Apply(pt.Object, At);
            pf.Object.ExposureLength.Should().Be(240);
            pf.Object.LengthSource.Should().Be(SubLength.SourcePlan);

            pf.Object.PlanExposureLength = -1;
            SubLengthPlanning.Apply(pt.Object, At);
            pf.Object.ExposureLength.Should().Be(300);
            pf.Object.LengthSource.Should().Be(SubLength.SourceTemplate);
        }

        private static (Mock<ITarget>, Mock<IExposure>) Target() {
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt.SetupProperty(m => m.Guid, "g1");
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0, 300, 1);
            pf.SetupProperty(m => m.Gain, 100);
            pf.SetupProperty(m => m.PlanExposureLength, -1);
            pf.SetupProperty(m => m.TemplateExposureLength, 300);
            PlanMocks.AddMockPlanFilter(pt, pf);
            Mock<IExposureSelector> selector = new Mock<IExposureSelector>();
            selector.Setup(s => s.Select(It.IsAny<DateTime>(), It.IsAny<IProject>(), It.IsAny<ITarget>())).Returns(pf.Object);
            pt.SetupProperty(m => m.ExposureSelector, selector.Object);
            return (pt, pf);
        }
    }
}
