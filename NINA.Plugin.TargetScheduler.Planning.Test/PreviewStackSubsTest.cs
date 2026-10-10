using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class PreviewStackSubsTest {

        private static GoalProgress Goal(GoalState state, int? stackSubs, double? hoursNeeded = null, bool done = false) {
            return new GoalProgress { TargetGuid = "ABC", Filter = "Ha", State = state, StackSubs = stackSubs, MinSubs = 8, SubLimit = 32, HoursNeeded = hoursNeeded, Done = done, Progress = 0.4 };
        }

        [Test]
        public void PreviewGrowsStackSubsForTheSameTargetAndFilter() {
            var sim = new SimulatedStackSubs();
            GoalProgress goal = Goal(GoalState.Collecting, 0);
            for (int i = 0; i < 7; i++) { sim.Add("abc", " HA "); }
            sim.Add("abc", "OIII");
            sim.Add("other", "Ha");

            GoalProgress grown = sim.Apply(goal, "ABC", "Ha");
            grown.StackSubs.Should().Be(7);
            goal.StackSubs.Should().Be(0);
            GoalCompletion.IsComplete(grown, 0).Should().BeFalse();

            sim.Add("ABC", "Ha");
            GoalCompletion.IsComplete(sim.Apply(goal, "ABC", "Ha"), 0).Should().BeTrue();
            sim.Taken("abc", "oiii").Should().Be(1);
        }

        [Test]
        public void PreviewLeavesUntouchedGoalsAndLocalCountsAlone() {
            var sim = new SimulatedStackSubs();
            GoalProgress goal = Goal(GoalState.Collecting, 3);
            sim.Apply(goal, "ABC", "Ha").Should().BeSameAs(goal);
            sim.Apply(null, "ABC", "Ha").Should().BeNull();

            sim.Add("ABC", "Ha");
            GoalProgress pending = Goal(GoalState.Pending, null);
            sim.Apply(pending, "ABC", "Ha").Should().BeSameAs(pending);
            sim.Apply(goal, null, null).StackSubs.Should().Be(4);
        }

        [Test]
        public void PreviewGrowthFeedsTheUnmeasurableBound() {
            var sim = new SimulatedStackSubs();
            GoalProgress goal = Goal(GoalState.Unmeasurable, 30);
            sim.Add("ABC", "Ha");
            GoalCompletion.IsComplete(sim.Apply(goal, "ABC", "Ha"), 0).Should().BeFalse();
            sim.Add("ABC", "Ha");
            GoalCompletion.IsComplete(sim.Apply(goal, "ABC", "Ha"), 0).Should().BeTrue();
        }
    }
}
