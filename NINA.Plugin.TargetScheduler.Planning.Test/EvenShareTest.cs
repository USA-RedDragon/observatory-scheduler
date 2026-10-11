using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class EvenShareTest {

        private static PanelExposure Sub(string filter, double seconds, int day) {
            return new PanelExposure { Filter = filter, Seconds = seconds, At = new DateTime(2026, 10, day, 6, 0, 0, DateTimeKind.Utc) };
        }

        [Test]
        public void EvenShareFavoursThePanelBelowItsShare() {
            EvenShare.Score(0, 0, 2, 6).Should().Be(1);
            EvenShare.Score(0, 10, 2, 6).Should().Be(1);
            EvenShare.Score(10.0 / 3, 10, 2, 6).Should().BeApproximately(0.5, 1e-9);
            EvenShare.Score(20.0 / 3, 10, 2, 6).Should().Be(0);
            EvenShare.Score(5, 10, 1, 3).Should().BeApproximately(0.25, 1e-9);
            EvenShare.Score(7, 10, 1, 3).Should().Be(0);
            EvenShare.Score(1, 10, 1, 3).Should().BeApproximately(0.85, 1e-9);
            EvenShare.Score(0, 10, 0, 3).Should().Be(0);
        }

        [Test]
        public void EvenShareWeighsPanelsByUnfinishedFilters() {
            double fourFilters = EvenShare.Score(4, 10, 4, 5);
            double oneFilter = EvenShare.Score(4, 10, 1, 5);
            fourFilters.Should().BeGreaterThan(oneFilter);
        }

        [TestCase("2026-06-21T12:00:00Z", 90)]
        [TestCase("2026-03-20T15:00:00Z", 0)]
        [TestCase("2026-09-23T00:00:00Z", 180)]
        [TestCase("2026-12-21T15:00:00Z", 270)]
        public void SunRightAscensionMatchesTheSolstices(string at, double ra) {
            double got = EvenShare.SunRightAscension(DateTime.Parse(at, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
            double d = Math.Abs(((got - ra) % 360 + 540) % 360 - 180);
            d.Should().BeLessThan(1);
        }

        [TestCase(0, "2026-03-20")]
        [TestCase(180, "2026-09-23")]
        [TestCase(300, "2026-01-18")]
        public void SeasonStartsWhenTheSunLastPassedTheMosaic(double ra, string start) {
            DateTime now = new DateTime(2026, 10, 11, 6, 0, 0, DateTimeKind.Utc);
            DateTime got = EvenShare.SeasonStart(now, ra);
            got.Should().BeOnOrBefore(now);
            (got - DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal)).Duration().TotalDays.Should().BeLessThan(2);
        }

        [Test]
        public void MeanRightAscensionWrapsAtZero() {
            double wrapped = EvenShare.MeanRightAscension(new[] { 359.0, 1.0 });
            Math.Min(wrapped, 360 - wrapped).Should().BeApproximately(0, 1e-6);
            EvenShare.MeanRightAscension(new[] { 10.0, 20.0 }).Should().BeApproximately(15, 1e-6);
        }


        [Test]
        public void HoursCountsTheWindowOnly() {
            var subs = new[] { Sub("L", 3600, 1), Sub("L", 1800, 5), Sub("R", 1800, 9) };
            EvenShare.Hours(subs, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 9, 6, 0, 0, DateTimeKind.Utc)).Should().Be(1);
            EvenShare.Hours(null, DateTime.MinValue, DateTime.MaxValue).Should().Be(0);
        }

        [Test]
        public void PanelScoreSkipsFinishedPanels() {
            EvenShare.PanelScore(new[] { (true, 2, 0.0), (false, 2, 6.0), (false, 0, 30.0) }).Should().Be(1);
            EvenShare.PanelScore(new[] { (false, 2, 0.0), (true, 2, 6.0), (false, 0, 30.0) }).Should().Be(0);
            EvenShare.PanelScore(new[] { (false, 2, 3.0), (true, 2, 3.0) }).Should().BeApproximately(0.5, 1e-9);
            EvenShare.PanelScore(new[] { (true, 0, 0.0), (false, 2, 3.0) }).Should().Be(0);
        }
    }
}
