using FluentAssertions;
using NINA.Plugin.TargetScheduler.Sequencer;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class PlanOutcomeTest {

        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        public void AfterEachExposureRunsOnlyForASavedFrame(bool sequenceCancelled, bool frameCompleted, bool runs) {
            PlanOutcome.RunsAfterEachExposure(sequenceCancelled, frameCompleted).Should().Be(runs);
        }
    }
}
