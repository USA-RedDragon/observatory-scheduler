using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GoalDelayTest {

        private static GoalProgress Goal(GoalState state, int? stackSubs, double? hoursNeeded = null, bool done = false) {
            return new GoalProgress { TargetGuid = "ABC", Filter = "Ha", State = state, StackSubs = stackSubs, MinSubs = 8, SubLimit = 32, HoursNeeded = hoursNeeded, Done = done, Progress = 0.4 };
        }

        [TestCase(0, 0.0)]
        [TestCase(1, 12.5)]
        [TestCase(6, 75.0)]
        [TestCase(7, 87.5)]
        [TestCase(8, 100.0)]
        public void CollectingDelayFollowsStackSubsAgainstTheMinimumNotDesired(int stackSubs, double expected) {
            GoalCompletion.DelayPercent(Goal(GoalState.Collecting, stackSubs), 300, 119).Should().BeApproximately(expected, 1e-9);
        }

        [Test]
        public void PendingDelayCountsLocalSubs() {
            GoalCompletion.DelayPercent(Goal(GoalState.Pending, null), 300, 4).Should().BeApproximately(50, 1e-9);
        }

        [TestCase(16, 50.0)]
        [TestCase(32, 100.0)]
        public void UnmeasurableDelayFollowsTheSubLimit(int stackSubs, double expected) {
            GoalCompletion.DelayPercent(Goal(GoalState.Unmeasurable, stackSubs), 300, 0).Should().BeApproximately(expected, 1e-9);
        }

        [Test]
        public void MeasuredDelayUsesTheSubsTheMeasuredHoursStillNeed() {
            GoalCompletion.DelayPercent(Goal(GoalState.Measured, 20, 1.0), 300, 119).Should().BeApproximately(62.5, 1e-9);
            GoalCompletion.DelayPercent(Goal(GoalState.Measured, 20, 10.0), 300, 119).Should().BeApproximately(100.0 * 20 / 140, 1e-9);
        }

        [Test]
        public void DoneGoalGradesEverythingPending() {
            GoalCompletion.DelayPercent(Goal(GoalState.Measured, 22, 30.0, done: true), 300, 3).Should().Be(100);
        }
    }
}
