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
    }
}
