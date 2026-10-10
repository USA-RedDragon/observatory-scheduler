using FluentAssertions;
using NUnit.Framework;
using System;
using System.IO;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class ImagingLimitTest {
        private static readonly DateTime Dawn = new DateTime(2026, 10, 10, 6, 35, 19);

        private static Func<DateTime, double> Sun(DateTime crossesMinus14, double degreesPerMinute) => t => -14 + (t - crossesMinus14).TotalMinutes * degreesPerMinute;

        private static ImagingLimit Limit(DateTime end) => new ImagingLimit { End = end, Basis = "the Sun Altitude loop condition on 'Imaging' ends the loop when the sun is above -14°" };

        private static FlipSettings Flip(bool sideOfPier = true) => new FlipSettings { MinutesAfterMeridian = 5, MaxMinutesAfterMeridian = 15, UseSideOfPier = sideOfPier };

        [Test]
        public void FindEndFindsWhenASunAboveConditionEnds() {
            DateTime? end = ImagingLimit.FindEnd(Sun(Dawn, 0.18), -14, true, Dawn.AddHours(-3), TimeSpan.FromHours(24));
            end.Should().NotBeNull();
            (end.Value - Dawn).Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
        }

        [Test]
        public void FindEndHandlesTheOtherComparatorANowAndNever() {
            DateTime dusk = Dawn.AddHours(-12);
            DateTime? end = ImagingLimit.FindEnd(Sun(dusk, -0.18), -14, false, dusk.AddHours(-1), TimeSpan.FromHours(24));
            (end.Value - dusk).Duration().Should().BeLessThan(TimeSpan.FromSeconds(2));
            ImagingLimit.FindEnd(Sun(Dawn, 0.18), -14, true, Dawn.AddMinutes(1), TimeSpan.FromHours(24)).Should().Be(Dawn.AddMinutes(1));
            ImagingLimit.FindEnd(t => -40, -14, true, Dawn, TimeSpan.FromHours(2)).Should().BeNull();
        }

        [Test]
        public void TheIncidentExposureDoesNotFitBeforeTheSequenceEnds() {
            ImagingLimit limit = Limit(Dawn);
            limit.FitReason(new DateTime(2026, 10, 10, 6, 34, 21), 600, DateTime.MinValue).Should().Contain("after the sequence ends imaging at 06:35:19");
            limit.FitReason(new DateTime(2026, 10, 10, 6, 23, 55), 600, DateTime.MinValue).Should().BeNull();
        }

        [Test]
        public void AFlipDueBeforeTheExposureNeedsAMeasuredDurationNearTheEnd() {
            DateTime at = Dawn.AddMinutes(-40);
            DateTime transit = at.AddMinutes(-10);
            ImagingLimit limit = Limit(Dawn);
            limit.Flip = Flip();
            limit.FlipDue(at, 600, transit).Should().BeTrue();
            limit.FitReason(at, 600, transit).Should().Contain("has not been measured");

            limit.FlipOverhead = TimeSpan.FromSeconds(180);
            limit.FitReason(at, 600, transit).Should().BeNull();
            limit.FitReason(Dawn.AddMinutes(-12), 600, Dawn.AddMinutes(-18)).Should().Contain("after NINA's meridian flip").And.Contain("180 s measured");

            limit.FlipOverhead = null;
            limit.End = at.AddHours(3);
            limit.FitReason(at, 600, transit).Should().BeNull();
        }

        [Test]
        public void FlipDueFollowsNinasTriggerWindow() {
            DateTime at = Dawn.AddHours(-3);
            ImagingLimit limit = Limit(Dawn);
            limit.Flip = Flip();
            limit.FlipDue(at, 600, at.AddMinutes(-6)).Should().BeTrue();
            limit.FlipDue(at, 600, at.AddMinutes(-15)).Should().BeTrue();
            limit.FlipDue(at, 600, at.AddMinutes(-16)).Should().BeFalse();
            limit.FlipDue(at, 600, at.AddMinutes(-4)).Should().BeFalse();
            limit.Flip = Flip(false);
            limit.FlipDue(at, 600, at.AddMinutes(-4)).Should().BeTrue();
            limit.FlipDue(at, 600, at.AddMinutes(-2)).Should().BeFalse();
            limit.Flip.PauseTimeBeforeMeridian = 10;
            limit.FlipDue(at, 600, at.AddMinutes(15)).Should().BeTrue();
            limit.FlipDue(at, 600, at.AddMinutes(25)).Should().BeFalse();
            limit.FlipDue(at, 600, DateTime.MinValue).Should().BeFalse();
        }

        [Test]
        public void FlipWaitAndMeasuredOverheadMatchNinasMinimumWait() {
            DateTime start = Dawn.AddHours(-3);
            ImagingLimit.FlipWait(start, start.AddMinutes(-2), Flip()).Should().Be(TimeSpan.FromMinutes(3));
            ImagingLimit.FlipWait(start, start.AddMinutes(-10), Flip()).Should().Be(TimeSpan.Zero);
            ImagingLimit.FlipWait(start, start.AddHours(3), Flip()).Should().Be(TimeSpan.Zero);
            ImagingLimit.NinaFlipWait(13.0 / 60, Flip()).Should().Be(TimeSpan.FromMinutes(3));
            ImagingLimit.NinaFlipWait(5.0 / 60, Flip()).Should().Be(TimeSpan.Zero);
            ImagingLimit.NinaFlipWait(23.5, Flip()).Should().Be(TimeSpan.Zero);
            ImagingLimit.NinaFlipWait(double.NaN, Flip()).Should().Be(TimeSpan.Zero);
            ImagingLimit.MeasuredOverhead(TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(3)).Should().Be(TimeSpan.FromMinutes(4));
            ImagingLimit.MeasuredOverhead(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Should().BeNull();
        }

        [Test]
        public void AFlipIsDueAtOnceBetweenTheMinimumAndMaximumAfterTheMeridian() {
            DateTime at = Dawn.AddHours(-3);
            ImagingLimit limit = Limit(Dawn);
            limit.Flip = Flip();
            limit.FlipDue(at, 60, at.AddMinutes(-6)).Should().BeTrue();
            limit.FlipDue(at, 60, at.AddMinutes(-14)).Should().BeTrue();
            limit.FlipDue(at, 60, at.AddMinutes(-4)).Should().BeFalse();

            limit.End = at.AddMinutes(5);
            limit.FitReason(at, 60, at.AddMinutes(-6)).Should().Contain("has not been measured");
            limit.FlipOverhead = TimeSpan.FromMinutes(5);
            limit.FitReason(at, 60, at.AddMinutes(-6)).Should().Contain("after NINA's meridian flip").And.Contain("0 s waiting");
        }

        [Test]
        public void ANonZeroPauseBeforeTheMeridianFollowsNinasPauseBranch() {
            DateTime at = Dawn.AddHours(-3);
            ImagingLimit limit = Limit(Dawn);
            limit.Flip = Flip();
            limit.Flip.PauseTimeBeforeMeridian = -3;
            limit.FlipDue(at, 60, at.AddMinutes(-2)).Should().BeTrue();
            limit.FlipDue(at, 60, at.AddMinutes(5)).Should().BeFalse();
        }

        [Test]
        public void FlipStateKeepsTheLongestOfTheLastFive() {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"flips-{Guid.NewGuid():N}.json");
            try {
                var s = new FlipState(path);
                s.Overhead().Should().BeNull();
                s.Record(TimeSpan.FromSeconds(400), Dawn.AddDays(-6));
                for (int i = 0; i < 5; i++) {
                    s.Record(TimeSpan.FromSeconds(100 + i), Dawn.AddDays(-5 + i));
                }
                var again = new FlipState(path);
                again.Overhead().Should().Be(TimeSpan.FromSeconds(104));
            } finally {
                File.Delete(path);
            }
        }
    }
}
