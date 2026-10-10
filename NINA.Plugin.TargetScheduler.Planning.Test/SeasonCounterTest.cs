using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SeasonCounterTest {

        private class Nights : INightUsability {
            private readonly Func<int, double> minutes;
            public List<int> Asked { get; } = new List<int>();

            public Nights(Func<int, double> minutes) {
                this.minutes = minutes;
            }

            public double UsableMinutes(int night) {
                Asked.Add(night);
                return minutes(night);
            }
        }

        [Test]
        public void TonightUnusableIsOutOfSeason() {
            var nights = new Nights(d => d == 0 ? 30 : 500);
            SeasonResult r = SeasonCounter.Count(nights, 60);
            r.OutOfSeason.Should().BeTrue();
            r.NightsLeft.Should().Be(0);
            nights.Asked.Should().Equal(0);
        }

        [Test]
        public void ZeroMinutesIsNeverUsableEvenWithNoMinimum() {
            SeasonCounter.Count(new Nights(d => 0), 0).OutOfSeason.Should().BeTrue();
            SeasonCounter.Count(new Nights(d => 1), 0).OutOfSeason.Should().BeFalse();
        }

        [Test]
        public void SeasonEndsAtFirstFourteenNightGap() {
            var nights = new Nights(d => d < 40 ? 120 : 0);
            SeasonResult r = SeasonCounter.Count(nights, 60);
            r.OutOfSeason.Should().BeFalse();
            r.NightsLeft.Should().Be(40);
            r.LastUsableNight.Should().Be(39);
            r.Capped.Should().BeFalse();
            r.NightsScanned.Should().Be(54);
            nights.Asked.Count.Should().Be(54);
        }

        [Test]
        public void ShortGapsDoNotEndTheSeason() {
            var nights = new Nights(d => d < 60 && d % 10 >= 3 ? 90 : (d < 60 ? 10 : 0));
            SeasonResult r = SeasonCounter.Count(nights, 60);
            r.OutOfSeason.Should().BeTrue();

            nights = new Nights(d => d < 60 && (d == 0 || d % 10 >= 3) ? 90 : 10);
            r = SeasonCounter.Count(nights, 60);
            r.OutOfSeason.Should().BeFalse();
            r.NightsLeft.Should().Be(43);
            r.LastUsableNight.Should().Be(59);
        }

        [Test]
        public void ThirteenUnusableNightsKeepTheSeasonOpen() {
            var nights = new Nights(d => d >= 5 && d < 18 ? 0 : (d < 30 ? 100 : 0));
            SeasonResult r = SeasonCounter.Count(nights, 60);
            r.NightsLeft.Should().Be(5 + 12);
            r.LastUsableNight.Should().Be(29);

            nights = new Nights(d => d >= 5 && d < 19 ? 0 : 100);
            r = SeasonCounter.Count(nights, 60);
            r.NightsLeft.Should().Be(5);
            r.LastUsableNight.Should().Be(4);
        }

        [Test]
        public void CircumpolarTargetIsCappedAtAYear() {
            var nights = new Nights(d => 600);
            SeasonResult r = SeasonCounter.Count(nights, 60);
            r.NightsLeft.Should().Be(365);
            r.Capped.Should().BeTrue();
            nights.Asked.Count.Should().Be(365);
            GoalScoring.RarityScore(r).Should().Be(0);
        }

        [Test]
        public void RhoOphLateInItsSeasonScoresHigh() {
            SeasonResult r = SeasonCounter.Count(new Nights(d => d < 12 ? 75 : 20), 60);
            r.NightsLeft.Should().Be(12);
            GoalScoring.RarityScore(r).Should().BeApproximately(108.0 / 110.0, 0.00001);
        }

        [Test]
        public void SeasonRowCarriesTheLastUsableNight() {
            var night = new DateTime(2026, 10, 9);
            SeasonResult r = SeasonCounter.Count(new Nights(d => d < 40 ? 120 : 0), 60);
            TargetSeasonRow row = TargetSeasonRow.From("abc", r, night, new DateTime(2026, 10, 9, 23, 0, 0, DateTimeKind.Utc));
            row.NightsLeft.Should().Be(40);
            row.SeasonEnd.Should().Be(new DateTime(2026, 11, 17));
            row.ComputedFor.Should().Be(night);

            TargetSeasonRow.From("abc", SeasonCounter.Count(new Nights(d => 600), 60), night, DateTime.UtcNow).SeasonEnd.Should().BeNull();
            TargetSeasonRow.From("abc", SeasonCounter.Count(new Nights(d => 0), 60), night, DateTime.UtcNow).SeasonEnd.Should().BeNull();
        }
    }
}
