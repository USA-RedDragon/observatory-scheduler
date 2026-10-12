using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BodyMotionTest {

        [Test]
        public void FieldComesFromThePixelSizeFocalLengthAndSensor() {
            BodyField f = BodyField.From(3.76, 405, 6248, 4176);
            f.ImageScale.Should().BeApproximately(206.265 * 3.76 / 405, 1e-12);
            f.ImageScale.Should().BeApproximately(1.915, 0.001);
            f.ShortSideDegrees.Should().BeApproximately(4176 * f.ImageScale / 3600, 1e-12);
            f.RecenterDegrees.Should().BeApproximately(0.1 * f.ShortSideDegrees, 1e-12);
        }

        [Test]
        public void UnknownOpticsFallBackToTheRigDefaults() {
            foreach (BodyField f in new[] { BodyField.From(null, 405, 6248, 4176), BodyField.From(3.76, double.NaN, 6248, 4176), BodyField.From(0, 405, null, null), BodyField.Default }) {
                f.ImageScale.Should().Be(1.91);
                f.ShortSideDegrees.Should().Be(2.2);
            }
            BodyField noSensor = BodyField.From(3.76, 405, null, 4176);
            noSensor.ImageScale.Should().BeApproximately(1.915, 0.001);
            noSensor.ShortSideDegrees.Should().Be(2.2);
            BodyField.Current = null;
            BodyField.Current.Should().BeSameAs(BodyField.Default);
        }

        [Test]
        public void DriftBelowTenPercentOfTheShortSideContinuesAndAboveRecenters() {
            BodyField f = BodyField.Default;
            BodyPosition centered = new BodyPosition(76.2, 27.86);
            double limitArcsec = 0.22 * 3600;
            BodyPosition below = new BodyPosition(76.2, 27.86 + (limitArcsec - 1) / 3600);
            BodyPosition above = new BodyPosition(76.2, 27.86 + (limitArcsec + 1) / 3600);
            BodyDrift.NeedsRecenter(centered, below, f).Should().BeFalse();
            BodyDrift.NeedsRecenter(centered, above, f).Should().BeTrue();

            double raStep = (limitArcsec + 1) / 3600 / Math.Cos(27.86 * Math.PI / 180);
            BodyDrift.NeedsRecenter(centered, new BodyPosition(76.2 + raStep, 27.86), f).Should().BeTrue();
            BodyDrift.NeedsRecenter(new BodyPosition(359.95, 0), new BodyPosition(0.05, 0), f).Should().BeFalse();
            BodyDrift.SeparationDegrees(new BodyPosition(359.95, 0), new BodyPosition(0.05, 0)).Should().BeApproximately(0.1, 1e-9);
        }

        [Test]
        public void CentersAreKeptPerTargetAndDesignation() {
            BodyCenters centers = new BodyCenters();
            DateTime at = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);
            centers.Record("G1", "C/2025 R2", new BodyPosition(1, 2), at);
            centers.Get("g1", "c/2025 r2").Position.Ra.Should().Be(1);
            centers.Get("g1", "C/2024 G3").Should().BeNull();
            centers.Get("g2", "C/2025 R2").Should().BeNull();
            centers.Record("g1", null, new BodyPosition(5, 5), at);
            centers.Get("g1", "C/2025 R2").Position.Dec.Should().Be(2);
            centers.Remove(" G1 ");
            centers.Get("g1", "C/2025 R2").Should().BeNull();
            centers.Record("g1", "C/2025 R2", new BodyPosition(1, 2), at);
            centers.Clear();
            centers.Get("g1", "C/2025 R2").Should().BeNull();
        }
    }
}
