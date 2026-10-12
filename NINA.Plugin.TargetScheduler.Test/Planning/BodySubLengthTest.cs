using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class BodySubLengthTest {
        private static readonly DateTime Start = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp() {
            SkyConditions.CurrentObserver = null;
            BodyField.Current = BodyField.Default;
            BodyPositionTable.Current = new BodyPositionTable(Enumerable.Range(0, 7).Select(i => new BodyPositionRow {
                TargetGuid = "g1", Designation = "C/2025 R2", At = Start.AddMinutes(10 * i), Ra = 76.2, Dec = 20, RaRate = i < 3 ? 0 : 3600, DecRate = i < 3 ? 6 : 0
            }));
            SubLengthTable.Current = new SubLengthTable(new[] { new SubLengthRow { TargetGuid = "g1", Filter = "L", Gain = 100, Bucket = SubLength.Dark, Exposure = 90 } });
        }

        [TearDown]
        public void Reset() {
            BodyPositionTable.Current = BodyPositionTable.Empty;
            SubLengthTable.Current = SubLengthTable.Empty;
        }

        private static (PlanningTarget, Mock<IExposure>) Target(string designation, double plan) {
            PlanningTarget target = new PlanningTarget { Name = "C/2025 R2", Guid = "g1", Designation = designation, Coordinates = TestData.M42 };
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            exposure.SetupProperty(e => e.Gain, 100);
            exposure.SetupProperty(e => e.PlanExposureLength, plan);
            exposure.SetupProperty(e => e.TemplateExposureLength, 600.0);
            target.AllExposurePlans = new List<IExposure> { exposure.Object };
            target.ExposurePlans = new List<IExposure> { exposure.Object };
            return (target, exposure);
        }

        [Test]
        public void SlowBodyKeepsItsLengthAndFastBodyIsCapped() {
            (PlanningTarget target, Mock<IExposure> exposure) = Target("C/2025 R2", 300);
            SubLengthPlanning.Apply(target, Start);
            exposure.Object.ExposureLength.Should().Be(300);
            exposure.Object.LengthSource.Should().Be(SubLength.SourcePlan);

            SubLengthPlanning.Apply(target, Start.AddMinutes(25));
            exposure.Object.ExposureLength.Should().Be(1);
            exposure.Object.LengthSource.Should().Be(BodyTrail.SourceBodyRate);
        }

        [Test]
        public void TheRateWindowCoversTheWholeExposure() {
            (PlanningTarget target, Mock<IExposure> exposure) = Target("C/2025 R2", 900);
            SubLengthPlanning.Apply(target, Start.AddMinutes(15));
            exposure.Object.ExposureLength.Should().Be(1);
        }

        [Test]
        public void BinningAndAnExplicitFieldWidenTheCap() {
            (PlanningTarget target, Mock<IExposure> exposure) = Target("C/2025 R2", 300);
            exposure.SetupProperty(e => e.BinningMode, new NINA.Core.Model.Equipment.BinningMode(2, 2));
            SubLengthChoice choice = SubLengthPlanning.Apply(exposure.Object, "g1", null);
            SubLengthPlanning.CapForBody(exposure.Object, choice, target, Start.AddMinutes(30));
            exposure.Object.ExposureLength.Should().Be(3);

            exposure.SetupProperty(e => e.BinningMode, null);
            choice = SubLengthPlanning.Apply(exposure.Object, "g1", null);
            SubLengthPlanning.CapForBody(exposure.Object, choice, target, Start.AddMinutes(30), BodyField.From(9.0, 405, null, null));
            exposure.Object.ExposureLength.Should().Be(4);
        }

        [Test]
        public void SyncClientsBuildTheFieldFromTheirOwnProfileAndCamera() {
            Mock<NINA.Profile.Interfaces.IProfileService> profile = PlanMocks.GetMockProfileService(TestData.North_Mid_Lat);
            profile.SetupProperty(m => m.ActiveProfile.CameraSettings.PixelSize, 3.76);
            profile.SetupProperty(m => m.ActiveProfile.TelescopeSettings.FocalLength, 405.0);
            BodyField field = BodyRecenter.Field(profile.Object.ActiveProfile, 6248, 4176);
            field.ImageScale.Should().BeApproximately(206.265 * 3.76 / 405, 1e-12);
            field.ShortSideDegrees.Should().BeApproximately(4176 * field.ImageScale / 3600, 1e-12);

            Mock<NINA.Equipment.Interfaces.Mediator.ICameraMediator> camera = new Mock<NINA.Equipment.Interfaces.Mediator.ICameraMediator>();
            BodyField noCamera = BodyRecenter.Field(profile.Object.ActiveProfile, camera.Object);
            noCamera.ImageScale.Should().BeApproximately(field.ImageScale, 1e-12);
            noCamera.ShortSideDegrees.Should().Be(BodyField.DefaultShortSideDegrees);
            BodyRecenter.Field(null, null).ImageScale.Should().Be(BodyField.DefaultImageScale);
        }

        [Test]
        public void FixedTargetsAreNotCapped() {
            (PlanningTarget target, Mock<IExposure> exposure) = Target(null, 300);
            SubLengthPlanning.Apply(target, Start.AddMinutes(25));
            exposure.Object.ExposureLength.Should().Be(300);
            exposure.Object.LengthSource.Should().Be(SubLength.SourcePlan);
        }

        [Test]
        public void UnknownTimeUsesTheFastestRateInTheTable() {
            (PlanningTarget target, Mock<IExposure> exposure) = Target("C/2025 R2", 300);
            SubLengthChoice choice = SubLengthPlanning.Apply(exposure.Object, "g1", null);
            SubLengthPlanning.CapForBody(exposure.Object, choice, target, null);
            exposure.Object.ExposureLength.Should().Be(1);
            SubLengthPlanning.BodyMaxSeconds(target, Start.AddDays(3), 60).Should().Be(1.91);
        }
    }
}
