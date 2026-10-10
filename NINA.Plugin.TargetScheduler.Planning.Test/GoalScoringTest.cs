using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GoalScoringTest {

        [TestCase(0.0, 0.0, 1.0)]
        [TestCase(0.0, 0.99, 1.0)]
        [TestCase(0.0, 1.0, 0.8)]
        [TestCase(0.25, 0.5, 0.75)]
        [TestCase(0.25, 3.0, 0.6)]
        [TestCase(1.0, 0.0, 0.0)]
        [TestCase(1.4, 10.0, 0.0)]
        [TestCase(-0.2, 0.0, 1.0)]
        [TestCase(double.NaN, 0.0, 1.0)]
        public void NoveltyTable(double progress, double effectiveHours, double expected) {
            GoalScoring.NoveltyScore(progress, effectiveHours).Should().BeApproximately(expected, 0.00001);
        }

        [Test]
        public void NoveltyUsesWeakestFilterAndMinimumGoalHours() {
            var filters = new List<FilterStanding> {
                new FilterStanding { Goal = new GoalProgress { Progress = 0.9, EffectiveHours = 6 } },
                new FilterStanding { Goal = new GoalProgress { Progress = 0.4, EffectiveHours = 0.5 } },
                new FilterStanding { PercentComplete = 70 }
            };
            GoalScoring.NoveltyScore(filters).Should().BeApproximately(0.6, 0.00001);
        }

        [Test]
        public void NoveltyWithoutGoalsUsesPercentAndAcceptedTime() {
            var fresh = new List<FilterStanding> {
                new FilterStanding { PercentComplete = 20, Accepted = 5, ExposureSeconds = 300 },
                new FilterStanding { PercentComplete = 50, Accepted = 5, ExposureSeconds = 300 }
            };
            GoalScoring.NoveltyScore(fresh).Should().BeApproximately(0.8, 0.00001);

            var refined = new List<FilterStanding> {
                new FilterStanding { PercentComplete = 20, Accepted = 10, ExposureSeconds = 300 },
                new FilterStanding { PercentComplete = 50, Accepted = 5, ExposureSeconds = 300 }
            };
            GoalScoring.NoveltyScore(refined).Should().BeApproximately(0.64, 0.00001);
        }

        [Test]
        public void NoveltyWithNoPlansIsFresh() {
            GoalScoring.NoveltyScore(new List<FilterStanding>()).Should().Be(1);
            GoalScoring.NoveltyScore((IList<FilterStanding>)null).Should().Be(1);
        }

        [TestCase(false, 1, 1.0)]
        [TestCase(false, 10, 1.0)]
        [TestCase(false, 11, 109.0 / 110.0)]
        [TestCase(false, 65, 0.5)]
        [TestCase(false, 120, 0.0)]
        [TestCase(false, 300, 0.0)]
        [TestCase(true, 0, 0.0)]
        public void RarityTable(bool outOfSeason, int nightsLeft, double expected) {
            GoalScoring.RarityScore(new SeasonResult { OutOfSeason = outOfSeason, NightsLeft = nightsLeft }).Should().BeApproximately(expected, 0.00001);
        }
    }
}
