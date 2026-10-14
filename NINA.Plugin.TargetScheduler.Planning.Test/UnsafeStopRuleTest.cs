using FluentAssertions;
using NINA.Plugin.TargetScheduler.Sequencer;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class UnsafeStopRuleTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 14, 2, 54, 27);
        private static readonly TimeSpan Hold = TimeSpan.FromSeconds(4);

        [Test]
        public void CancelsAfterTheMonitorStaysUnsafeForTheHoldTime() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(1), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.UnsafeSince.Should().Be(T0.AddSeconds(1));
            rule.Observe(T0.AddSeconds(4), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(5), true, false, false).Should().Be(UnsafeStopAction.Cancel);
            rule.Action.Should().Be(UnsafeStopAction.Cancel);
        }

        [Test]
        public void ActsOnlyOncePerPlan() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false);
            rule.Observe(T0.AddSeconds(1), true, false, false);
            rule.Observe(T0.AddSeconds(6), true, false, false).Should().Be(UnsafeStopAction.Cancel);
            rule.Observe(T0.AddSeconds(7), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(8), true, false, true).Should().Be(UnsafeStopAction.None);
            rule.Action.Should().Be(UnsafeStopAction.Cancel);
        }

        [Test]
        public void ABriefBlipDoesNotCancel() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false);
            rule.Observe(T0.AddSeconds(1), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(3), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(4), true, true, false).Should().Be(UnsafeStopAction.None);
            rule.UnsafeSince.Should().BeNull();
            rule.Observe(T0.AddSeconds(5), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(8), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(9), true, false, false).Should().Be(UnsafeStopAction.Cancel);
        }

        [Test]
        public void NoMonitorConnectedNeverCancels() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            for (int s = 0; s < 600; s++) {
                rule.Observe(T0.AddSeconds(s), false, false, false).Should().Be(UnsafeStopAction.None);
            }
            rule.UnsafeSince.Should().BeNull();
        }

        [Test]
        public void ADisconnectResetsTheHold() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false);
            rule.Observe(T0.AddSeconds(1), true, false, false);
            rule.Observe(T0.AddSeconds(3), false, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(4), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(7), true, false, false).Should().Be(UnsafeStopAction.None);
            rule.Observe(T0.AddSeconds(8), true, false, false).Should().Be(UnsafeStopAction.Cancel);
        }

        [Test]
        public void APlanThatStartsUnsafeIsLeftToTheUnsafeTrigger() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            for (int s = 0; s < 120; s++) {
                rule.Observe(T0.AddSeconds(s), true, false, false).Should().Be(UnsafeStopAction.None);
            }
            rule.Action.Should().Be(UnsafeStopAction.None);
        }

        [Test]
        public void TheRunningUnsafeTriggerIsNeverCancelled() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false);
            rule.Observe(T0.AddSeconds(1), true, false, false);
            rule.Observe(T0.AddSeconds(2), true, false, true).Should().Be(UnsafeStopAction.SkipRest);
            rule.Observe(T0.AddSeconds(30), true, false, true).Should().Be(UnsafeStopAction.None);
            rule.Action.Should().Be(UnsafeStopAction.SkipRest);
        }

        [Test]
        public void ADowngradedCancelBecomesASkip() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, true, true, false);
            rule.Observe(T0.AddSeconds(1), true, false, false);
            rule.Observe(T0.AddSeconds(5), true, false, false).Should().Be(UnsafeStopAction.Cancel);
            rule.Downgrade();
            rule.Action.Should().Be(UnsafeStopAction.SkipRest);
            rule.Observe(T0.AddSeconds(6), true, false, false).Should().Be(UnsafeStopAction.None);
        }

        [Test]
        public void TheUnsafeTriggerRunningSkipsTheRestEvenWithoutAMonitor() {
            UnsafeStopRule rule = new UnsafeStopRule(Hold);
            rule.Observe(T0, false, false, true).Should().Be(UnsafeStopAction.SkipRest);
        }

        [Test]
        public void TheDefaultHoldIsAFewSeconds() {
            UnsafeStopRule.DefaultHold.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
            UnsafeStopRule.DefaultHold.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
        }
    }
}
