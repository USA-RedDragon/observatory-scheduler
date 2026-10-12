using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class BodyCallSitesTest {
        private static readonly DateTime Sunset = new DateTime(2026, 10, 12, 18, 40, 0);
        private static readonly DateTime Sunrise = new DateTime(2026, 10, 13, 7, 10, 0);

        [SetUp]
        public void SetUp() {
            TargetVisibilityCache.Clear();
        }

        [TearDown]
        public void Reset() {
            BodyPositionTable.Current = BodyPositionTable.Empty;
            TargetBodyTable.Current = TargetBodyTable.Empty;
            TargetVisibilityCache.Clear();
        }

        private static PlanningTarget Body(int id) {
            Mock<IProject> project = new Mock<IProject>();
            project.SetupAllProperties();
            project.SetupProperty(p => p.Name, "Comets");
            return new PlanningTarget {
                Name = "C/2025 R2", DatabaseId = id, Guid = "g1", Designation = "C/2025 R2", Coordinates = TestData.M42, Project = project.Object,
                ExposurePlans = new List<IExposure>(), AllExposurePlans = new List<IExposure>(), CompletedExposurePlans = new List<IExposure>()
            };
        }

        [Test]
        public void BodyWithoutPositionsIsNeverVisible() {
            PlanningTarget target = Body(9001);
            TargetVisibility viz = new TargetVisibility(target, TestData.North_Mid_Lat, Sunset.Date, Sunset, Sunrise, 60);
            viz.ImagingPossible.Should().BeFalse();
            viz.HasTransit().Should().BeFalse();
            viz.TargetPositions.Should().NotBeEmpty();
            viz.TargetPositions.All(p => p.Altitude == -90).Should().BeTrue();
        }

        [Test]
        public void BodyWithoutPositionsIsRejectedWithAReason() {
            PlanningTarget target = Body(9002);
            TargetImagingExpert expert = new TargetImagingExpert(PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), false);
            expert.Visibility(Sunset.AddHours(3), target, null, null).Should().BeFalse();
            target.Rejected.Should().BeTrue();
            target.RejectedReason.Should().Be(Reasons.TargetBodyPositionUnknown);
        }

        [Test]
        public void MoonAvoidanceFilterGivesUnknownPositionsTheirOwnReason() {
            PlanningTarget target = Body(9004);
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            target.ExposurePlans.Add(exposure.Object);
            Mock<IMoonAvoidanceExpert> moon = new Mock<IMoonAvoidanceExpert>(MockBehavior.Strict);
            TargetImagingExpert expert = new TargetImagingExpert(PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), false);
            expert.MoonAvoidanceFilter(Sunset.AddHours(3), target, moon.Object);
            exposure.Object.Rejected.Should().BeTrue();
            exposure.Object.RejectedReason.Should().Be(Reasons.FilterBodyPositionUnknown);
            new Planner(Sunset.AddHours(3), PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), PlanMocks.GetWeatherDataMediator(false, 0), false, false)
                .GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetBodyPositionUnknown);
        }

        [Test]
        public void MoonAvoidanceRejectsABodyWithoutAPosition() {
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            exposure.SetupProperty(e => e.MoonAvoidanceEnabled, true);
            exposure.SetupProperty(e => e.MoonAvoidanceSeparation, 1.0);
            exposure.SetupProperty(e => e.MoonAvoidanceWidth, 1);
            MoonAvoidanceExpert sut = new MoonAvoidanceExpertMock(TestData.North_Mid_Lat) { Altitude = 20, MoonAge = 14, SeparationAngle = 120 };
            sut.IsRejected(Sunset.AddHours(3), Body(9003), exposure.Object).Should().BeTrue();

            Mock<ITarget> fixedTarget = PlanMocks.GetMockPlanTarget("T1", null);
            sut.IsRejected(Sunset.AddHours(3), fixedTarget.Object, exposure.Object).Should().BeFalse();
        }
    }
}
