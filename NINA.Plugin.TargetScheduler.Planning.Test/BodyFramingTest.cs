using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BodyFramingTest {
        private const double Long = 3.32;

        private static double Separation(BodyPosition a, BodyPosition b) {
            return BodyDrift.SeparationDegrees(a, b);
        }

        [Test]
        public void MovesAlongPositionAnglesEastOfNorth() {
            BodyPosition p = new BodyPosition(100, 0);
            BodyFraming.Move(p, 0, 1).Dec.Should().BeApproximately(1, 1e-9);
            BodyFraming.Move(p, 0, 1).Ra.Should().BeApproximately(100, 1e-9);
            BodyFraming.Move(p, 90, 1).Ra.Should().BeApproximately(101, 1e-9);
            BodyFraming.Move(p, 90, 1).Dec.Should().BeApproximately(0, 1e-9);
            BodyFraming.Move(p, 180, 1).Dec.Should().BeApproximately(-1, 1e-9);
            BodyFraming.Move(p, 270, 1).Ra.Should().BeApproximately(99, 1e-9);
            BodyFraming.Move(p, 90, -1).Ra.Should().BeApproximately(99, 1e-9);
            BodyFraming.Move(p, 45, 0).Should().Be(p);
        }

        [Test]
        public void WrapsRightAscensionAtZero() {
            BodyPosition east = BodyFraming.Move(new BodyPosition(359.8, 0), 90, 0.5);
            east.Ra.Should().BeApproximately(0.3, 1e-9);
            BodyPosition west = BodyFraming.Move(new BodyPosition(0.2, 0), 270, 0.5);
            west.Ra.Should().BeApproximately(359.7, 1e-9);
            BodyFraming.Normalize(-0.0000001).Should().BeInRange(0, 360);
            BodyFraming.Normalize(360).Should().Be(0);
            BodyFraming.Normalize(-90).Should().Be(270);
        }

        [Test]
        public void KeepsTheDistanceAndAngleNearThePole() {
            BodyPosition body = new BodyPosition(10, 87.5);
            foreach (double pa in new[] { 0.0, 45, 90, 135, 200, 300 }) {
                BodyPosition moved = BodyFraming.Move(body, pa, 0.83);
                Separation(body, moved).Should().BeApproximately(0.83, 1e-9);
                BodyFraming.PositionAngle(body, moved).Should().BeApproximately(pa, 1e-6);
            }
            BodyPosition overThePole = BodyFraming.Move(new BodyPosition(10, 89.5), 0, 1);
            overThePole.Dec.Should().BeApproximately(89.5, 1e-9);
            overThePole.Ra.Should().BeApproximately(190, 1e-9);
        }

        [Test]
        public void TailFramingPutsTheFrameCentreTowardTheTailAndTheHeadSunward() {
            BodyPosition head = new BodyPosition(321.33029, 4.81158);
            BodyFrame tail = BodyFrame.From(84.063, -0.25);
            BodyPosition center = tail.Center(head, Long);
            Separation(head, center).Should().BeApproximately(0.25 * Long, 1e-9);
            BodyFraming.PositionAngle(head, center).Should().BeApproximately(84.063, 1e-6);
            center.Ra.Should().BeGreaterThan(head.Ra);

            BodyFrame flipped = BodyFrame.From(264.063, 0.25);
            BodyPosition same = flipped.Center(head, Long);
            same.Ra.Should().BeApproximately(center.Ra, 1e-9);
            same.Dec.Should().BeApproximately(center.Dec, 1e-9);

            BodyPosition sunward = BodyFrame.From(84.063, 0.25).Center(head, Long);
            BodyFraming.PositionAngle(head, sunward).Should().BeApproximately(264.063, 1e-6);

            BodyFrame centered = BodyFrame.From(84.063, 0);
            centered.Center(head, Long).Should().Be(head);
        }

        [Test]
        public void NinaPositionAngleLaysTheLongSideAlongTheRotation() {
            BodyFrame f = BodyFrame.From(84, -0.25);
            f.NinaPositionAngle(6248, 4176).Should().Be(174);
            f.NinaPositionAngle(null, null).Should().Be(174);
            f.NinaPositionAngle(4176, 6248).Should().Be(84);
            BodyFrame.From(300, 0).NinaPositionAngle(6248, 4176).Should().Be(30);
            BodyFrame.From(270, 0).NinaPositionAngle(6248, 4176).Should().Be(0);
        }

        [Test]
        public void ChecksTheRanges() {
            BodyFrame.Check(null, null).Should().BeNull();
            BodyFrame.Check(84, -0.25).Should().BeNull();
            BodyFrame.Check(0, 0.5).Should().BeNull();
            BodyFrame.Check(359.99, -0.5).Should().BeNull();
            BodyFrame.Check(84, null).Should().Contain("together");
            BodyFrame.Check(null, 0.1).Should().Contain("together");
            BodyFrame.Check(360, 0).Should().Contain("rotation_deg");
            BodyFrame.Check(-1, 0).Should().Contain("rotation_deg");
            BodyFrame.Check(double.NaN, 0).Should().Contain("rotation_deg");
            BodyFrame.Check(10, 0.51).Should().Contain("offset_frac");
            BodyFrame.Check(10, double.PositiveInfinity).Should().Contain("offset_frac");
            BodyFrame.From(84, null).Should().BeNull();
            BodyFrame.From(400, 0).Should().BeNull();
            BodyFrame.From(84, -0.25).RotationDeg.Should().Be(84);
        }

        [Test]
        public void LongSideComesFromTheSensorOrTheRigDefault() {
            BodyField f = BodyField.From(3.76, 405, 6248, 4176);
            f.LongSideDegrees.Should().BeApproximately(6248 * f.ImageScale / 3600, 1e-12);
            f.LongSideDegrees.Should().BeApproximately(3.32, 0.01);
            BodyField.From(3.76, 405, 4176, 6248).LongSideDegrees.Should().BeApproximately(f.LongSideDegrees, 1e-12);
            BodyField.Default.LongSideDegrees.Should().Be(3.3);
            BodyField.From(3.76, 405, null, 4176).LongSideDegrees.Should().Be(3.3);
        }
    }
}
