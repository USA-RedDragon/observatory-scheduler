using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GoalCompletionLogTest {

        [Test]
        public void LogsTheFirstDecisionAndThenOnlyChanges() {
            var sut = new GoalCompletionLog();
            var goal = new GoalProgress { State = GoalState.Collecting, StackSubs = 5, MinSubs = 8, SubLimit = 32, Reason = "insufficient data" };

            string first = sut.Observe("Lagoon Nebula", "T1", "Red", 7, goal, 0, 300, out bool firstSeen);
            firstSeen.Should().BeTrue();
            first.Should().Be("goal completion Lagoon Nebula/Red: none -> collecting open; stack_subs=5, min_subs=8, sub_limit=32, done=False, plateau=False, hours_needed=unknown, remaining_subs=3, reason=insufficient data");

            sut.Observe("Lagoon Nebula", "t1", "RED ", 7, goal, 0, 300, out _).Should().BeNull();
            goal.StackSubs = 6;
            sut.Observe("Lagoon Nebula", "T1", "Red", 7, goal, 0, 300, out _).Should().BeNull();

            goal.StackSubs = 8;
            string closed = sut.Observe("Lagoon Nebula", "T1", "Red", 7, goal, 0, 300, out firstSeen);
            firstSeen.Should().BeFalse();
            closed.Should().StartWith("goal completion Lagoon Nebula/Red: collecting open -> collecting closed; stack_subs=8, min_subs=8");
            sut.Observe("Lagoon Nebula", "T1", "Red", 7, goal, 0, 300, out _).Should().BeNull();

            var measured = new GoalProgress { State = GoalState.Measured, HoursNeeded = 2.5, Progress = 0.4, StackSubs = 12, MinSubs = 8, SubLimit = 32 };
            sut.Observe("Lagoon Nebula", "T1", "Red", 7, measured, 0, 300, out _)
                .Should().Be("goal completion Lagoon Nebula/Red: collecting closed -> measured open; stack_subs=12, min_subs=8, sub_limit=32, done=False, plateau=False, hours_needed=2.50h measured, remaining_subs=30");

            measured.Done = true;
            measured.Plateau = true;
            sut.Observe("Lagoon Nebula", "T1", "Red", 7, measured, 0, 300, out _).Should().Contain("measured open -> measured closed").And.Contain("done=True, plateau=True");
        }

        [Test]
        public void KeepsEachPlanApartAndNamesPendingAndUnmeasurable() {
            var sut = new GoalCompletionLog();
            var pending = new GoalProgress { State = GoalState.Pending, MinSubs = 8 };
            sut.Observe("M 92", "T2", "L", 1, pending, 8, 300, out _).Should().Contain("none -> pending closed").And.Contain("stack_subs=8 acquired (no stack_subs)");
            sut.Observe("M 92", "T2", "L", 2, pending, 3, 300, out _).Should().Contain("none -> pending open");
            sut.Observe("M 92", "T2", "L", 1, pending, 8, 300, out _).Should().BeNull();

            var unmeasurable = new GoalProgress { State = GoalState.Unmeasurable, StackSubs = 32, SubLimit = 32, Reason = "no plate solution" };
            sut.Observe("M 92", "T2", "OIII", 3, unmeasurable, 0, 300, out _).Should().Contain("none -> unmeasurable closed").And.Contain("reason=no plate solution");
            sut.Observe(null, "T2", "OIII", 3, null, 0, 300, out _).Should().BeNull();
        }
    }
}
