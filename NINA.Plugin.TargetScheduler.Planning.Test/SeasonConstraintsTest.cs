using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SeasonConstraintsTest {

        private static MoonParams Classic(double separation = 120, int width = 14) {
            return new MoonParams { Enabled = true, Separation = separation, Width = width };
        }

        [Test]
        public void MaximumAltitudeAndMeridianWindowLimitPositions() {
            var c = new SeasonConstraints { MaximumAltitude = 80, MeridianWindowMinutes = 60 };
            c.PositionUsable(85, 0).Should().BeFalse();
            c.PositionUsable(70, 0.5).Should().BeTrue();
            c.PositionUsable(70, 1.5).Should().BeFalse();
            c.PositionUsable(70, 23.5).Should().BeTrue();
            c.PositionUsable(70, 22.5).Should().BeFalse();
            SeasonConstraints.None.PositionUsable(89, 6).Should().BeTrue();
        }

        [Test]
        public void HourAnglesNormaliseAroundTheMeridian() {
            SeasonConstraints.NormalizeHours(23).Should().Be(-1);
            SeasonConstraints.NormalizeHours(1).Should().Be(1);
            SeasonConstraints.NormalizeHours(-13).Should().Be(11);
            SeasonConstraints.NormalizeHours(36).Should().Be(12);
        }

        [Test]
        public void LorentzianMatchesTheAcpFormula() {
            MoonAvoidance.LorentzianSeparation(ConditionMatch.LunarCycleDays / 2, 120, 14).Should().BeApproximately(120, 1e-9);
            MoonAvoidance.LorentzianSeparation(0, 120, 14).Should().BeApproximately(120 / (1 + System.Math.Pow(0.5 / (14 / ConditionMatch.LunarCycleDays), 2)), 1e-9);
            MoonAvoidance.LorentzianSeparation(10, 120, 0).Should().Be(0);
        }

        [Test]
        public void MoonAvoidanceFollowsTheExpertRules() {
            double full = ConditionMatch.LunarCycleDays / 2;
            MoonAvoidance.IsRejected(Classic(), 30, full, 60).Should().BeTrue();
            MoonAvoidance.IsRejected(Classic(), 30, full, 130).Should().BeFalse();
            MoonAvoidance.IsRejected(Classic(), 30, 1, 70).Should().BeFalse();
            MoonAvoidance.IsRejected(new MoonParams { Enabled = false }, 30, full, 1).Should().BeFalse();
            MoonAvoidance.IsRejected(null, 30, full, 1).Should().BeFalse();

            var relaxed = new MoonParams { Enabled = true, Separation = 120, Width = 14, RelaxScale = 2, RelaxMaxAltitude = 5, RelaxMinAltitude = -15 };
            MoonAvoidance.IsRejected(relaxed, -20, full, 10).Should().BeFalse();
            MoonAvoidance.IsRejected(relaxed, -5, full, 110).Should().BeFalse();
            MoonAvoidance.IsRejected(relaxed, -5, full, 60).Should().BeTrue();

            var down = new MoonParams { Enabled = true, Separation = 120, Width = 14, DownEnabled = true, RelaxMaxAltitude = 5 };
            MoonAvoidance.IsRejected(down, 10, 1, 179).Should().BeTrue();
        }

        [Test]
        public void SeparationIsTheGreatCircleAngle() {
            MoonAvoidance.Separation(0, 0, 90, 0).Should().BeApproximately(90, 1e-9);
            MoonAvoidance.Separation(10, 89, 190, 89).Should().BeApproximately(2, 1e-9);
            MoonAvoidance.Separation(45, 20, 45, 20).Should().BeApproximately(0, 1e-6);
        }

        [Test]
        public void TheMoonOnlyBlocksWhenEveryPlanAvoidsIt() {
            var moon = new MoonSample { Altitude = 40, AgeDays = ConditionMatch.LunarCycleDays / 2, RaDegrees = 0, Dec = 0 };

            var allAvoid = new SeasonConstraints { Moon = new List<MoonParams> { Classic(), Classic(100) } };
            allAvoid.NeedsMoon.Should().BeTrue();
            allAvoid.MoonAllows(moon, 30, 0).Should().BeFalse();
            allAvoid.MoonAllows(moon, 110, 0).Should().BeTrue();

            var tolerant = new SeasonConstraints { Moon = new List<MoonParams> { Classic(), Classic(20, 7) } };
            tolerant.MoonAllows(moon, 30, 0).Should().BeTrue();

            var oneOff = new SeasonConstraints { Moon = new List<MoonParams> { Classic(), new MoonParams { Enabled = false } } };
            oneOff.NeedsMoon.Should().BeFalse();
            oneOff.MoonAllows(moon, 1, 0).Should().BeTrue();
            SeasonConstraints.None.NeedsMoon.Should().BeFalse();
        }

        [Test]
        public void KeyIgnoresPlanOrderAndDuplicates() {
            var a = new SeasonConstraints { MaximumAltitude = 80, MeridianWindowMinutes = 30, Moon = new List<MoonParams> { Classic(), Classic(60, 7), Classic() } };
            var b = new SeasonConstraints { MaximumAltitude = 80, MeridianWindowMinutes = 30, Moon = new List<MoonParams> { Classic(60, 7), Classic() } };
            a.Key().Should().Be(b.Key());
            a.Key().Should().NotBe(new SeasonConstraints { MaximumAltitude = 80, MeridianWindowMinutes = 45, Moon = b.Moon }.Key());
            a.Key().Should().NotBe(new SeasonConstraints { MaximumAltitude = 80, MeridianWindowMinutes = 30 }.Key());
        }
    }
}
