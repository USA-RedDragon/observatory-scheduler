using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class ManagedRulesTest {
        private const double DegreesPerMinute = 0.2;
        private static readonly DateTime T0 = new DateTime(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc);

        private static Func<DateTime, double> Dusk(double atT0) {
            return t => atT0 - DegreesPerMinute * (t - T0).TotalMinutes;
        }

        private static Func<DateTime, double> Dawn(double atT0) {
            return t => atT0 + DegreesPerMinute * (t - T0).TotalMinutes;
        }

        [Test]
        public void ConstantsMatchTheMeasuredRules() {
            TwilightRule.BroadbandSunLimit.Should().Be(-15);
            MoonFloor.Degrees.Should().Be(20);
            ManagedTemplateSpec.TwilightLevel("Red").Should().Be(1);
            ManagedTemplateSpec.TwilightLevel("H-a").Should().Be(2);
        }

        [TestCase(-16.0, false)]
        [TestCase(-15.0, false)]
        [TestCase(-14.9, true)]
        public void DuskBroadbandNeedsTheSunAtOrBelowTheLimit(double sun, bool rejected) {
            TwilightRule.Rejects(true, "Red", false, Dusk(sun), T0, 300).Should().Be(rejected);
            TwilightRule.Rejects(true, "Luminance", false, Dusk(sun), T0, 300).Should().Be(rejected);
        }

        [Test]
        public void DawnBroadbandIsRejectedWhenTheExposureEndsAboveTheLimit() {
            TwilightRule.Rejects(true, "Red", false, Dawn(-16), T0, 240).Should().BeFalse();
            TwilightRule.Rejects(true, "Red", false, Dawn(-16), T0, 300).Should().BeFalse();
            TwilightRule.Rejects(true, "Red", false, Dawn(-16), T0, 303).Should().BeTrue();
            TwilightRule.Rejects(true, "Red", false, Dawn(-16), T0, 600).Should().BeTrue();
            TwilightRule.Rejects(true, "Red", false, Dawn(-14.9), T0, 1).Should().BeTrue();
            TwilightRule.Rejects(true, "Blue", false, -16, -14.9).Should().BeTrue();
            TwilightRule.Rejects(true, "Blue", false, -16, -15.1).Should().BeFalse();
        }

        [Test]
        public void NighttimeIsNeverGated() {
            TwilightRule.Rejects(true, "Red", true, Dawn(-14), T0, 600).Should().BeFalse();
        }

        [TestCase(-14.9)]
        [TestCase(-13.0)]
        [TestCase(-12.1)]
        public void NarrowbandIsAllowedInTwilight(double sun) {
            TwilightRule.Rejects(true, "H-a", false, Dawn(sun), T0, 600).Should().BeFalse();
            TwilightRule.Rejects(true, "O-III", false, Dusk(sun), T0, 600).Should().BeFalse();
            TwilightRule.Rejects(true, "S-II", false, sun, sun + 2).Should().BeFalse();
        }

        [Test]
        public void NonManagedExposuresAreUnchanged() {
            TwilightRule.Rejects(false, "Red", false, Dawn(-13), T0, 600).Should().BeFalse();
            TwilightRule.Rejects(false, "Red", false, -13, -12).Should().BeFalse();
            MoonFloor.Rejects(false, false, 40, 1).Should().BeFalse();
        }

        [TestCase(19.9, true)]
        [TestCase(20.0, false)]
        [TestCase(20.1, false)]
        public void MoonFloorAt20Degrees(double separation, bool rejected) {
            MoonFloor.Rejects(true, false, 30, separation).Should().Be(rejected);
        }

        [Test]
        public void MoonBelowTheHorizonIsIgnored() {
            MoonFloor.Rejects(true, false, -0.1, 5).Should().BeFalse();
            MoonFloor.Rejects(true, false, 0, 5).Should().BeFalse();
            MoonFloor.Rejects(true, false, 0.1, 5).Should().BeTrue();
        }

        [Test]
        public void IgnoreMoonWindowsSkipTheFloor() {
            MoonFloor.Rejects(true, true, 30, 5).Should().BeFalse();
        }

        [Test]
        public void MoonFloorUsesMidExposure() {
            MoonFloor.MidExposure(T0, 600).Should().Be(T0.AddMinutes(5));
            MoonFloor.MidExposure(T0, -1).Should().Be(T0);
        }

        [Test]
        public void RoleTableAnswersManagedAndRetired() {
            var table = new ManagedTemplateTable(new[] {
                new TemplateRoleRow { TemplateId = 40, ProfileId = "P", Filter = "Red", Role = "managed" },
                new TemplateRoleRow { TemplateId = 10, ProfileId = "P", Filter = "Red", Role = "retired" },
                new TemplateRoleRow { TemplateId = 0, ProfileId = "P", Filter = "Red", Role = "managed" },
                null
            });
            table.Count.Should().Be(2);
            table.ManagedCount.Should().Be(1);
            table.IsManaged(40).Should().BeTrue();
            table.IsManaged(10).Should().BeFalse();
            table.IsRetired(10).Should().BeTrue();
            table.IsManaged(99).Should().BeFalse();
            table.HasManaged("p").Should().BeTrue();
            ManagedTemplateTable.Empty.IsManaged(40).Should().BeFalse();
        }
    }
}
