using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
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
        public void HoursNeededCountsEachMeasuredGoalFilterOnceSkipsDoneAndCountsUnknown() {
            var filters = new List<RunwayFilter> {
                new RunwayFilter { Filter = "Ha", Goal = new GoalProgress { HoursNeeded = 4 }, CountHours = 50 },
                new RunwayFilter { Filter = "HA ", Goal = new GoalProgress { HoursNeeded = 4 }, CountHours = 50 },
                new RunwayFilter { Filter = "OIII", Goal = new GoalProgress { HoursNeeded = -1 }, CountHours = 20 },
                new RunwayFilter { Filter = "SII", Goal = new GoalProgress { HoursNeeded = 3, Done = true }, CountHours = 20 },
                new RunwayFilter { Filter = "L", CountHours = 2.5 },
                null
            };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(4 + 20 + 2.5, 1e-9);
            SeasonalRunway.HoursNeeded(null).Should().Be(0);
        }

        private static GoalProgress Collecting(int? stackSubs) {
            return new GoalProgress { State = GoalState.Collecting, StackSubs = stackSubs, MinSubs = 8, SubLimit = 32, Reason = "no master yet" };
        }

        private static RunwayFilter Plan(string filter, GoalProgress goal, int desired, int accepted, double exposureSeconds, int acquired = 0) {
            return new RunwayFilter {
                Filter = filter,
                Goal = goal,
                CountHours = Math.Max(0, desired - accepted) * exposureSeconds / 3600,
                ExposureSeconds = exposureSeconds,
                Acquired = acquired
            };
        }

        [Test]
        public void UnmeasuredGoalsNeedTheirRemainingDesiredCount() {
            var lagoon = new List<RunwayFilter> {
                Plan("Luminance", Collecting(3), 30, 0, 300),
                Plan("Red", Collecting(5), 30, 0, 300),
                Plan("Green", Collecting(3), 30, 0, 300),
                Plan("Blue", Collecting(2), 30, 0, 300)
            };
            SeasonalRunway.HoursNeeded(lagoon).Should().BeApproximately(10, 1e-9);
            SeasonalRunway.Score(SeasonalRunway.HoursNeeded(lagoon), new SeasonResult { NightsLeft = 13, UsableHours = 20.2 }).Should().Be(1);

            var barnardsE = new List<RunwayFilter> {
                Plan("Luminance", Collecting(0), 100, 0, 300),
                Plan("Red", Collecting(0), 100, 0, 600),
                Plan("Green", Collecting(0), 100, 0, 600),
                Plan("Blue", Collecting(0), 100, 0, 600)
            };
            SeasonalRunway.HoursNeeded(barnardsE).Should().BeApproximately(58.333333333, 1e-6);
            SeasonalRunway.Score(SeasonalRunway.HoursNeeded(barnardsE), new SeasonResult { NightsLeft = 48, UsableHours = 142.7 }).Should().Be(1);
        }

        [Test]
        public void MeasuredGoalWithoutHoursNeededFallsBackToTheCount() {
            var filters = new List<RunwayFilter> { Plan("Ha", new GoalProgress { State = GoalState.Measured, Progress = 0.4 }, 40, 10, 600) };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(5, 1e-9);
        }

        [Test]
        public void PendingGoalWithoutAProgressRowFallsBackToTheCount() {
            var goal = new GoalProgress { State = GoalState.Pending, MinSubs = 8 };
            SeasonalRunway.HoursNeeded(new[] { Plan("L", goal, 20, 2, 300) }).Should().BeApproximately(1.5, 1e-9);
        }

        [Test]
        public void UnmeasuredGoalNeedsAtLeastTheFirstMeasurementWhenTheCountIsUsedUp() {
            var filters = new List<RunwayFilter> { Plan("L", Collecting(3), 1, 1, 300, 1) };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(5 * 300 / 3600.0, 1e-9);
        }

        [Test]
        public void UnmeasuredGoalSumsTheCountsOfEveryPlanOfItsFilter() {
            var goal = Collecting(0);
            var filters = new List<RunwayFilter> { Plan("Ha", goal, 10, 0, 600), Plan("HA", goal, 20, 0, 300) };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(10 * 600 / 3600.0 + 20 * 300 / 3600.0, 1e-9);
        }

        [Test]
        public void UnmeasurableGoalNeedsTheSubsLeftToItsBound() {
            var goal = new GoalProgress { State = GoalState.Unmeasurable, StackSubs = 20, MinSubs = 8, SubLimit = 32 };
            var filters = new List<RunwayFilter> { Plan("OIII", goal, 300, 0, 600) };
            SeasonalRunway.HoursNeeded(filters).Should().BeApproximately(2, 1e-9);

            goal.StackSubs = 32;
            SeasonalRunway.HoursNeeded(filters).Should().Be(0);
        }

        [Test]
        public void UnfinishedGoalNeedsAtLeastOneSubWhenTheCountIsUsedUp() {
            SeasonalRunway.HoursNeeded(new[] { Plan("Ha", new GoalProgress { State = GoalState.Measured, HoursNeeded = -1 }, 10, 10, 600) }).Should().BeApproximately(600 / 3600.0, 1e-9);
            SeasonalRunway.HoursNeeded(new[] { Plan("Ha", new GoalProgress { State = GoalState.Measured }, 10, 10, 600) }).Should().BeApproximately(600 / 3600.0, 1e-9);
            SeasonalRunway.HoursNeeded(new[] { Plan("Ha", new GoalProgress { State = GoalState.Unmeasurable }, 10, 10, 600) }).Should().BeApproximately(600 / 3600.0, 1e-9);
            SeasonalRunway.HoursNeeded(new[] { Plan("Ha", new GoalProgress { State = GoalState.Measured, HoursNeeded = 0 }, 10, 0, 600) }).Should().Be(0);
        }

        [Test]
        public void DoneGoalNeedsNothingWhateverItsCount() {
            var goal = new GoalProgress { State = GoalState.Collecting, Done = true, MinSubs = 8 };
            SeasonalRunway.HoursNeeded(new[] { Plan("L", goal, 100, 0, 300) }).Should().Be(0);
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
