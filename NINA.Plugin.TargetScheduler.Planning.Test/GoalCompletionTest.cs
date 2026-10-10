using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GoalCompletionTest {

        private static GoalProgress Goal(double progress, bool done, double hoursNeeded = 0) {
            return new GoalProgress { TargetGuid = "t", Filter = "L", Progress = progress, Done = done, HoursNeeded = hoursNeeded };
        }

        [TestCase(0.0, false, 0.0)]
        [TestCase(0.42, false, 42.0)]
        [TestCase(0.999, false, 99.9)]
        [TestCase(1.0, false, 99.9)]
        [TestCase(1.7, false, 99.9)]
        [TestCase(-0.5, false, 0.0)]
        [TestCase(double.NaN, false, 0.0)]
        [TestCase(0.6, true, 100.0)]
        [TestCase(1.2, true, 100.0)]
        public void PercentCompleteFollowsProgressAndOnlyDoneReachesHundred(double progress, bool done, double expected) {
            GoalCompletion.PercentComplete(Goal(progress, done)).Should().BeApproximately(expected, 0.0001);
            (GoalCompletion.PercentComplete(Goal(progress, done)) < 100).Should().Be(!done);
            GoalCompletion.IsComplete(Goal(progress, done)).Should().Be(done);
        }

        [TestCase(0.3, true, 5.0, 300.0, 0)]
        [TestCase(1.5, false, 0.0, 300.0, 1)]
        [TestCase(0.5, false, 2.0, 300.0, 24)]
        [TestCase(0.5, false, 0.01, 300.0, 1)]
        [TestCase(0.5, false, 2.0, 0.0, 1)]
        [TestCase(0.5, false, double.NaN, 300.0, 1)]
        [TestCase(0.5, false, double.PositiveInfinity, 300.0, 1)]
        [TestCase(0.5, false, 1e9, 1.0, 100000)]
        public void RemainingExposuresNeverZeroUntilDone(double progress, bool done, double hoursNeeded, double exposureSeconds, int expected) {
            GoalCompletion.RemainingExposures(Goal(progress, done, hoursNeeded), exposureSeconds).Should().Be(expected);
        }

        private static GoalProgress Waiting(GoalState state, int? stackSubs, int? minSubs = 8, int? subLimit = 32) {
            return new GoalProgress { TargetGuid = "t", Filter = "L", State = state, StackSubs = stackSubs, MinSubs = minSubs, SubLimit = subLimit };
        }

        [TestCase(0, false, 8)]
        [TestCase(7, false, 1)]
        [TestCase(8, true, 0)]
        [TestCase(30, true, 0)]
        public void CollectingStaysOpenUntilTheStackersMinimum(int stackSubs, bool complete, int remaining) {
            GoalProgress goal = Waiting(GoalState.Collecting, stackSubs);
            GoalCompletion.IsComplete(goal, 500).Should().Be(complete);
            GoalCompletion.RemainingExposures(goal, 300, 500).Should().Be(remaining);
            GoalCompletion.PercentComplete(goal).Should().Be(0);
        }

        [Test]
        public void CollectingWithoutAPublishedMinimumStaysOpen() {
            GoalProgress goal = Waiting(GoalState.Collecting, 50, null);
            GoalCompletion.IsComplete(goal, 500).Should().BeFalse();
            GoalCompletion.RemainingExposures(goal, 300, 500).Should().Be(1);
        }

        [TestCase(31, false, 1)]
        [TestCase(32, true, 0)]
        [TestCase(12, false, 20)]
        public void UnmeasurableStopsAtThePublishedBound(int stackSubs, bool complete, int remaining) {
            GoalProgress goal = Waiting(GoalState.Unmeasurable, stackSubs);
            GoalCompletion.IsComplete(goal, 0).Should().Be(complete);
            GoalCompletion.RemainingExposures(goal, 300, 0).Should().Be(remaining);
        }

        [TestCase(3, false, 5)]
        [TestCase(8, true, 0)]
        public void PendingCountsLocalSubsAgainstTheMinimum(int acquired, bool complete, int remaining) {
            GoalProgress goal = Waiting(GoalState.Pending, null);
            GoalCompletion.IsComplete(goal, acquired).Should().Be(complete);
            GoalCompletion.RemainingExposures(goal, 300, acquired).Should().Be(remaining);
        }

        [Test]
        public void PendingWithoutAnyPublishedMinimumStaysOpen() {
            GoalProgress goal = Waiting(GoalState.Pending, null, null);
            GoalCompletion.IsComplete(goal, 10000).Should().BeFalse();
            GoalCompletion.RemainingExposures(goal, 300, 10000).Should().Be(1);
        }

        [Test]
        public void MeasuredIgnoresSubCounts() {
            GoalProgress goal = new GoalProgress { Progress = 0.5, HoursNeeded = 1, StackSubs = 500, MinSubs = 8, SubLimit = 32 };
            GoalCompletion.IsComplete(goal, 10000).Should().BeFalse();
            GoalCompletion.RemainingExposures(goal, 300, 10000).Should().Be(12);
            goal.Done = true;
            GoalCompletion.IsComplete(goal, 0).Should().BeTrue();
        }
    }
}
