using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SeasonalRunwayTest {

        private class Nights : INightUsability {
            private readonly double[] minutes;

            public Nights(params double[] minutes) {
                this.minutes = minutes;
            }

            public double UsableMinutes(int night) {
                return night < minutes.Length ? minutes[night] : 0;
            }
        }

        [Test]
        public void CounterAddsUpTheUsableHoursOfUsableNights() {
            SeasonResult season = SeasonCounter.Count(new Nights(120, 30, 180, 0, 60), 60, 30, 14);
            season.NightsLeft.Should().Be(3);
            season.UsableHours.Should().BeApproximately(6, 1e-9);

            SeasonCounter.Count(new Nights(30, 120), 60, 30, 14).UsableHours.Should().Be(0);
        }

        [Test]
        public void HoursNeededCountsEachGoalFilterOnceAndSkipsDoneOrUnknown() {
            var filters = new List<RunwayFilter> {
                new RunwayFilter { Filter = "Ha", Goal = new GoalProgress { HoursNeeded = 4 }, CountHours = 50 },
                new RunwayFilter { Filter = "HA ", Goal = new GoalProgress { HoursNeeded = 4 }, CountHours = 50 },
                new RunwayFilter { Filter = "OIII", Goal = new GoalProgress { HoursNeeded = -1 } },
                new RunwayFilter { Filter = "SII", Goal = new GoalProgress { HoursNeeded = 3, Done = true } },
                new RunwayFilter { Filter = "L", CountHours = 2.5 },
                null
            };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(6.5, 1e-9);
            SeasonalRunway.HoursNeeded(null).Should().Be(0);
        }

        [Test]
        public void SeasonHoursFallBackToNightsForStoredSeasons() {
            SeasonalRunway.SeasonHours(new SeasonResult { NightsLeft = 10, UsableHours = 25 }).Should().Be(25);
            SeasonalRunway.SeasonHours(new SeasonResult { NightsLeft = 10 }).Should().Be(30);
            SeasonalRunway.SeasonHours(new SeasonResult { NightsLeft = 10, OutOfSeason = true }).Should().Be(0);
            SeasonalRunway.SeasonHours(null).Should().Be(0);
        }

        [TestCase(0, 100, 0)]
        [TestCase(3, 100, 0.1)]
        [TestCase(15, 100, 0.5)]
        [TestCase(30, 100, 1)]
        [TestCase(90, 100, 1)]
        [TestCase(5, 0, 1)]
        public void ScoreIsHoursNeededAgainstTheExpectedShareOfTheSeason(double needed, double seasonHours, double expected) {
            SeasonalRunway.Score(needed, new SeasonResult { NightsLeft = 1, UsableHours = seasonHours }).Should().BeApproximately(expected, 1e-9);
        }

        [Test]
        public void OutOfSeasonScoresZero() {
            SeasonalRunway.Score(50, new SeasonResult { OutOfSeason = true }).Should().Be(0);
            SeasonalRunway.Score(50, null).Should().Be(0);
        }
    }
}
