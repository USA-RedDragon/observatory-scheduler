using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class TargetCoordinatesTest {
        private static readonly DateTime Start = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);

        [TearDown]
        public void Reset() {
            BodySnapshot.Current = BodySnapshot.Empty;
        }

        private static void Positions(string designation) {
            BodyPositionTable.Current = new BodyPositionTable(new[] {
                new BodyPositionRow { TargetGuid = "g1", Designation = designation, At = Start, Ra = 76.2, Dec = 27.8, RaRate = -13.4, DecRate = 0.8 },
                new BodyPositionRow { TargetGuid = "g1", Designation = designation, At = Start.AddMinutes(10), Ra = 76.199, Dec = 27.8001, RaRate = -13.4, DecRate = 0.8 },
            });
        }

        [Test]
        public void FixedTargetsKeepTheirCoordinatesObject() {
            Positions("C/2025 R2");
            PlanningTarget target = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42 };
            target.IsBody().Should().BeFalse();
            target.CoordinatesAt(Start).Should().BeSameAs(TestData.M42);
            target.CoordinatesAt(DateTime.MinValue).Should().BeSameAs(TestData.M42);

            Mock<ITarget> mock = PlanMocks.GetMockPlanTarget("T1", TestData.M31);
            mock.Object.Designation.Should().BeNull();
            mock.Object.CoordinatesAt(Start).Should().BeSameAs(TestData.M31);
        }

        [Test]
        public void BodiesUseTheTableAndNeverTheFixedCoordinates() {
            Positions("C/2025 R2");
            PlanningTarget target = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Designation = "c/2025 r2" };
            target.IsBody().Should().BeTrue();
            Coordinates c = target.CoordinatesAt(Start);
            c.RADegrees.Should().BeApproximately(76.2, 1e-9);
            c.Dec.Should().BeApproximately(27.8, 1e-9);
            c.Epoch.Should().Be(Epoch.J2000);
            target.CoordinatesAt(Start.AddMinutes(5)).RADegrees.Should().BeInRange(76.199, 76.2);
            target.CoordinatesAt(Start.AddMinutes(11)).Should().BeNull();
            target.CoordinatesAt(Start.AddMinutes(-1)).Should().BeNull();
        }

        [Test]
        public void PlanningTargetsCaptureOneSnapshot() {
            Positions("C/2025 R2");
            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2" } });
            IProject project = PlanMocks.GetMockPlanProject("comets", ProjectState.Active).Object;
            Target entity = new Target { Id = 7, Name = "C/2025 R2", Guid = "g1", ra = 5, dec = 10, OverrideExposureOrders = new List<OverrideExposureOrderItem>(), FilterCadences = new List<FilterCadenceItem>() };
            PlanningTarget target = new PlanningTarget(project, entity);
            target.Designation.Should().Be("C/2025 R2");
            target.BodyEphemeris.Count.Should().Be(2);

            BodySnapshot.Current = BodySnapshot.Empty;
            target.CoordinatesAt(Start).RADegrees.Should().BeApproximately(76.2, 1e-9);

            PlanningTarget fixedTarget = new PlanningTarget(project, new Target { Id = 8, Name = "M42", Guid = "g2", ra = 5, dec = 10, OverrideExposureOrders = new List<OverrideExposureOrderItem>(), FilterCadences = new List<FilterCadenceItem>() });
            fixedTarget.Designation.Should().BeNull();
            fixedTarget.BodyEphemeris.Should().BeNull();
            fixedTarget.CoordinatesAt(Start).Should().BeSameAs(fixedTarget.Coordinates);
        }

        [Test]
        public void UnknownPositionsAreReportedForBodiesOnly() {
            Positions("C/2025 R2");
            PlanningTarget body = new PlanningTarget { Name = "C/2025 R2", Guid = "g1", Coordinates = TestData.M42, Designation = "C/2025 R2" };
            body.PositionUnknownReason(Start).Should().BeNull();
            body.PositionUnknownReason(Start.AddHours(1)).Should().Contain("has no position");
            PlanningTarget fixedTarget = new PlanningTarget { Name = "M42", Guid = "g1", Coordinates = null };
            fixedTarget.PositionUnknownReason(Start.AddHours(1)).Should().BeNull();
        }

        [Test]
        public void StaleDesignationRowsAreIgnored() {
            Positions("C/2024 G3");
            PlanningTarget target = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Designation = "C/2025 R2" };
            target.CoordinatesAt(Start).Should().BeNull();
        }

        private static BodyPosition At(Coordinates c) {
            return new BodyPosition(c.RADegrees, c.Dec);
        }

        [Test]
        public void FixedTargetsAndUnframedBodiesKeepTheirCoordinatesAndRotation() {
            Positions("C/2025 R2");
            PlanningTarget fixedTarget = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Rotation = 33 };
            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2", RotationDeg = 84, OffsetFrac = -0.25 } });
            fixedTarget.Frame().Should().BeNull();
            fixedTarget.FramedCoordinatesAt(Start).Should().BeSameAs(TestData.M42);
            fixedTarget.FramedRotation(Start).Should().Be(33);

            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2" } });
            PlanningTarget body = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Designation = "C/2025 R2", Rotation = 33 };
            body.Frame().Should().BeNull();
            body.FramedCoordinatesAt(Start).RADegrees.Should().Be(body.CoordinatesAt(Start).RADegrees);
            body.FramedCoordinatesAt(Start).Dec.Should().Be(body.CoordinatesAt(Start).Dec);
            body.FramedCoordinatesAt(Start.AddHours(1)).Should().BeNull();
            body.FramedRotation(Start).Should().Be(33);
        }

        [Test]
        public void FramedBodiesSlewToTheOffsetCentreAndTurnTheLongSideAlongTheRotation() {
            Positions("C/2025 R2");
            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2", RotationDeg = 84, OffsetFrac = -0.25 } });
            PlanningTarget body = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Designation = "C/2025 R2", Rotation = 33 };
            BodyField wide = BodyField.From(3.76, 405, 6248, 4176);
            Coordinates head = body.CoordinatesAt(Start);
            Coordinates center = body.FramedCoordinatesAt(Start, wide);
            center.Epoch.Should().Be(Epoch.J2000);
            BodyDrift.SeparationDegrees(At(head), At(center)).Should().BeApproximately(0.25 * wide.LongSideDegrees, 1e-9);
            BodyFraming.PositionAngle(At(head), At(center)).Should().BeApproximately(84, 1e-6);
            body.FramedRotation(null, wide).Should().Be(174);
            body.FramedRotation(null, BodyField.From(3.76, 405, 4176, 6248)).Should().Be(84);
            double atCentre = body.FramedRotation(Start, wide);
            atCentre.Should().BeApproximately(174, 0.5);
            double axis = BodyFraming.Normalize(atCentre - 90);
            double toHead = BodyFraming.PositionAngle(At(center), At(head));
            BodyFraming.Normalize(axis - toHead).Should().BeApproximately(180, 1e-6);

            Coordinates later = body.FramedCoordinatesAt(Start.AddMinutes(10), wide);
            BodyDrift.SeparationDegrees(At(body.CoordinatesAt(Start.AddMinutes(10))), At(later)).Should().BeApproximately(0.25 * wide.LongSideDegrees, 1e-9);

            PlanningTarget other = new PlanningTarget { Guid = "g1", Coordinates = TestData.M42, Designation = "C/2024 G3", Rotation = 33 };
            other.Frame().Should().BeNull();
            other.FramedRotation(Start, wide).Should().Be(33);
        }

        [Test]
        public void PlanningTargetsCaptureTheFrameWithTheSnapshot() {
            Positions("C/2025 R2");
            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2", RotationDeg = 84, OffsetFrac = -0.25 } });
            IProject project = PlanMocks.GetMockPlanProject("comets", ProjectState.Active).Object;
            Target entity = new Target { Id = 7, Name = "C/2025 R2", Guid = "g1", ra = 5, dec = 10, OverrideExposureOrders = new List<OverrideExposureOrderItem>(), FilterCadences = new List<FilterCadenceItem>() };
            PlanningTarget target = new PlanningTarget(project, entity);
            target.BodyFrame.RotationDeg.Should().Be(84);
            TargetBodyTable.Current = TargetBodyTable.Empty;
            target.Frame().OffsetFrac.Should().Be(-0.25);

            PlanningTarget fixedTarget = new PlanningTarget(project, new Target { Id = 8, Name = "M42", Guid = "g2", ra = 5, dec = 10, OverrideExposureOrders = new List<OverrideExposureOrderItem>(), FilterCadences = new List<FilterCadenceItem>() });
            fixedTarget.BodyFrame.Should().BeNull();

            PlanningTarget unframed = new PlanningTarget(project, entity);
            target.Equals(unframed).Should().BeFalse();
            TargetBodyTable.Current = new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g1", Designation = "C/2025 R2", RotationDeg = 84, OffsetFrac = -0.25 } });
            PlanningTarget again = new PlanningTarget(project, entity);
            target.Equals(again).Should().BeTrue();
            again.BodyFrame = BodyFrame.From(84, 0);
            target.Equals(again).Should().BeFalse();
            again.BodyFrame = null;
            target.Equals(again).Should().BeFalse();
            unframed.Equals(again).Should().BeTrue();
        }
    }
}
