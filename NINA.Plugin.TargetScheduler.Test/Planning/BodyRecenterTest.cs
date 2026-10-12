using FluentAssertions;
using Moq;
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
    public class BodyRecenterTest {
        private static readonly DateTime Start = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);
        private const double DecRate = 3600;

        [SetUp]
        public void SetUp() {
            BodyPositionTable.Current = new BodyPositionTable(Enumerable.Range(0, 13).Select(i => new BodyPositionRow {
                TargetGuid = "g1", Designation = "C/2025 R2", At = Start.AddMinutes(10 * i), Ra = 76.2, Dec = 20 + i * DecRate / 3600 / 6, RaRate = 0, DecRate = DecRate
            }));
            BodyCenters.Live.Clear();
            BodyField.Current = BodyField.Default;
        }

        [TearDown]
        public void Reset() {
            BodyPositionTable.Current = BodyPositionTable.Empty;
            BodyCenters.Live.Clear();
        }

        private static PlanningTarget Body(string designation = "C/2025 R2") {
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            exposure.SetupProperty(e => e.PreDither, false);
            PlanningTarget target = new PlanningTarget { Name = "C/2025 R2", Guid = "g1", Designation = designation, Coordinates = TestData.M42, Rotation = 0 };
            target.SelectedExposure = exposure.Object;
            return target;
        }

        private static DateTime AfterArcsec(double arcsec) {
            return Start.AddHours(arcsec / DecRate);
        }

        [Test]
        public void BodyContinuesBelowTheThresholdAndEndsAbove() {
            PlanningTarget target = Body();
            BodyRecenter.RecordCenter(target, target.CoordinatesAt(Start), Start);
            double limit = BodyField.Default.RecenterDegrees * 3600;
            BodyRecenter.EndReason(target, AfterArcsec(limit - 5), BodyCenters.Live, BodyField.Default).Should().BeNull();
            BodyRecenter.EndReason(target, AfterArcsec(limit + 5), BodyCenters.Live, BodyField.Default).Should().Contain("re-centering");

            BodyField narrow = BodyField.From(3.76, 405, 1000, 1000);
            BodyRecenter.EndReason(target, AfterArcsec(0.1 * 1000 * narrow.ImageScale + 5), BodyCenters.Live, narrow).Should().NotBeNull();
            BodyRecenter.EndReason(target, AfterArcsec(0.1 * 1000 * narrow.ImageScale - 5), BodyCenters.Live, narrow).Should().BeNull();
        }

        [Test]
        public void BodyWithoutACenterOrWithAnUnknownPosition() {
            PlanningTarget target = Body();
            BodyRecenter.EndReason(target, AfterArcsec(5000), BodyCenters.Live, BodyField.Default).Should().BeNull();
            BodyRecenter.EndReason(target, Start.AddHours(3), BodyCenters.Live, BodyField.Default).Should().Contain("no position");
        }

        [Test]
        public void FixedTargetsNeverEndForDrift() {
            PlanningTarget fixedTarget = new PlanningTarget { Name = "M42", Guid = "g1", Coordinates = TestData.M42 };
            BodyCenters.Live.Record("g1", "C/2025 R2", new BodyPosition(0, 0), Start);
            BodyRecenter.EndReason(fixedTarget, Start.AddHours(10), BodyCenters.Live, BodyField.Default).Should().BeNull();
            BodyRecenter.Required(fixedTarget, Start.AddHours(10), BodyCenters.Live, BodyField.Default).Should().BeFalse();
            BodyRecenter.RecordCenter(fixedTarget, TestData.M42, Start);
            BodyCenters.Live.Get("g1", "C/2025 R2").Position.Ra.Should().Be(0);
        }

        [Test]
        public void PreviousTargetEndsWhenTheBodyDrifts() {
            PlanningTarget target = Body();
            BodyRecenter.RecordCenter(target, target.CoordinatesAt(Start), Start);
            PreviousTargetExpert sut = new PreviousTargetExpert(PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), false, TestData.North_Mid_Lat);
            sut.CanContinue(AfterArcsec(BodyField.Default.RecenterDegrees * 3600 + 5).ToLocalTime(), null, target).Should().BeFalse();
        }

        private static SchedulerPlan Plan(PlanningTarget target, DateTime at, bool slew) {
            List<IInstruction> instructions = new List<IInstruction>();
            if (slew) { instructions.Add(new PlanSlew(target, true)); }
            return new SchedulerPlan(at, new List<IProject>(), target, instructions, false) { PlanCoordinates = target.CoordinatesAt(at) };
        }

        [Test]
        public void CentersAreRecordedWhenTheTargetIsSetForANewTargetOrARecenter() {
            PlanningTarget target = Body();
            BodyRecenter.AfterSetTarget(target, null, Plan(target, Start, true), true, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").At.Should().Be(Start);

            BodyRecenter.AfterSetTarget(target, target, Plan(target, Start.AddMinutes(5), false), true, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").At.Should().Be(Start);

            BodyRecenter.AfterSetTarget(target, target, Plan(target, Start.AddMinutes(30), true), true, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").At.Should().Be(Start.AddMinutes(30));
            BodyCenters.Live.Get("g1", "C/2025 R2").Position.Dec.Should().BeApproximately(target.CoordinatesAt(Start.AddMinutes(30)).Dec, 1e-9);
        }

        [Test]
        public void WithSlewAndCenterOffNoCenterIsKeptSoDriftNeverEndsThePlan() {
            PlanningTarget target = Body();
            BodyRecenter.RecordCenter(target, target.CoordinatesAt(Start), Start);
            BodyRecenter.AfterSetTarget(target, null, Plan(target, Start, true), false, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").Should().BeNull();
            DateTime late = AfterArcsec(BodyField.Default.RecenterDegrees * 3600 * 3);
            BodyRecenter.EndReason(target, late, BodyCenters.Live, BodyField.Default).Should().BeNull();
            new InstructionGenerator().Generate(target, target, late).OfType<PlanSlew>().Should().BeEmpty();
        }

        [Test]
        public void FixedTargetsRecordNothing() {
            PlanningTarget fixedTarget = new PlanningTarget { Name = "M42", Guid = "g9", Coordinates = TestData.M42, SelectedExposure = PlanMocks.GetMockPlanExposure("L", 10, 0).Object };
            BodyRecenter.AfterSetTarget(fixedTarget, null, Plan(fixedTarget, Start, true), true, BodyCenters.Live);
            BodyCenters.Live.Get("g9", "C/2025 R2").Should().BeNull();
        }

        [Test]
        public void SameBodyGetsASlewAndCenterAfterDrift() {
            PlanningTarget target = Body();
            InstructionGenerator sut = new InstructionGenerator();
            BodyRecenter.RecordCenter(target, target.CoordinatesAt(Start), Start);

            List<IInstruction> list = sut.Generate(target, target, AfterArcsec(10));
            list.OfType<PlanSlew>().Should().BeEmpty();

            list = sut.Generate(target, target, AfterArcsec(BodyField.Default.RecenterDegrees * 3600 + 5));
            list[0].Should().BeOfType<PlanSlew>();
            list.OfType<PlanBeforeNewTargetContainer>().Should().BeEmpty();

            target.IsPreview = true;
            sut.Generate(target, target, AfterArcsec(BodyField.Default.RecenterDegrees * 3600 + 5)).OfType<PlanSlew>().Should().BeEmpty();
        }

        [Test]
        public void AFramedBodyRecordsTheBodyNotTheOffsetFrameCentreSoItDoesNotRecenterAtOnce() {
            PlanningTarget target = Body();
            target.BodyFrame = BodyFrame.From(84, -0.25);
            SchedulerPlan plan = new SchedulerPlan(Start, new List<IProject>(), target, new List<IInstruction> { new PlanSlew(target, true) }, false) {
                PlanBodyCoordinates = target.CoordinatesAt(Start),
                PlanCoordinates = target.FramedCoordinatesAt(Start, BodyField.Default)
            };
            BodyDrift.SeparationDegrees(new BodyPosition(plan.PlanCoordinates.RADegrees, plan.PlanCoordinates.Dec), new BodyPosition(76.2, 20)).Should().BeApproximately(0.25 * BodyField.Default.LongSideDegrees, 1e-6);
            BodyRecenter.AfterSetTarget(target, null, plan, true, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").Position.Dec.Should().BeApproximately(20, 1e-9);
            BodyCenters.Live.Get("g1", "C/2025 R2").Position.Ra.Should().BeApproximately(76.2, 1e-9);
            BodyRecenter.EndReason(target, Start, BodyCenters.Live, BodyField.Default).Should().BeNull();

            BodyCenters.Live.Clear();
            SchedulerPlan noBody = new SchedulerPlan(Start, new List<IProject>(), target, new List<IInstruction> { new PlanSlew(target, true) }, false) {
                PlanCoordinates = target.FramedCoordinatesAt(Start, BodyField.Default)
            };
            BodyRecenter.AfterSetTarget(target, null, noBody, true, BodyCenters.Live);
            BodyCenters.Live.Get("g1", "C/2025 R2").Position.Dec.Should().BeApproximately(20, 1e-9);
            BodyRecenter.EndReason(target, Start, BodyCenters.Live, BodyField.Default).Should().BeNull();
        }
    }
}
