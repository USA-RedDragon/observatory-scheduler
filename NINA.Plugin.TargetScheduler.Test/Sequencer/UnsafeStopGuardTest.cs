using FluentAssertions;
using Moq;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Test.Sequencer {

    [TestFixture]
    public class UnsafeStopGuardTest {

        public class WhenUnsafe : SequenceTrigger {

            public void SetRunning(bool running) {
                Status = running ? SequenceEntityStatus.RUNNING : SequenceEntityStatus.CREATED;
            }

            public override object Clone() {
                return new WhenUnsafe();
            }

            public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
                return Task.CompletedTask;
            }

            public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
                return false;
            }
        }

        private class Step : SequenceItem {

            public void SetStatus(SequenceEntityStatus status) {
                Status = status;
            }

            public override object Clone() {
                return new Step();
            }

            public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
                return Task.CompletedTask;
            }
        }

        private static Mock<ISafetyMonitorMediator> Monitor(Func<SafetyMonitorInfo> info) {
            var m = new Mock<ISafetyMonitorMediator>();
            m.Setup(x => x.GetInfo()).Returns(() => info());
            return m;
        }

        [Test]
        public void FindsARunningUnsafeTriggerInAnAncestor() {
            var outer = new SequentialContainer();
            var owner = new SequentialContainer();
            outer.Add(owner);
            var trigger = new WhenUnsafe();
            owner.Add(trigger);

            UnsafeStopGuard.UnsafeTriggerRunning(owner).Should().BeFalse();
            trigger.SetRunning(true);
            UnsafeStopGuard.UnsafeTriggerRunning(owner).Should().BeTrue();
        }

        [Test]
        public void SkipRemainingSkipsOnlyWaitingInstructions() {
            var plan = new SequentialContainer();
            var done = new Step();
            var waiting = new Step();
            plan.Add(done);
            plan.Add(waiting);
            done.SetStatus(SequenceEntityStatus.FINISHED);

            UnsafeStopGuard.SkipRemaining(plan).Should().Be(1);
            done.Status.Should().Be(SequenceEntityStatus.FINISHED);
            waiting.Status.Should().Be(SequenceEntityStatus.SKIPPED);
        }

        [Test]
        public async Task CancelsOnlyThePlanAfterTheMonitorStaysUnsafe() {
            bool safe = true;
            var monitor = Monitor(() => new SafetyMonitorInfo { Connected = true, IsSafe = safe });
            var owner = new SequentialContainer();
            owner.Add(new WhenUnsafe());
            var plan = new SequentialContainer();
            using var sequence = new CancellationTokenSource();
            using var planCts = CancellationTokenSource.CreateLinkedTokenSource(sequence.Token);

            using (var guard = new UnsafeStopGuard(monitor.Object, owner, plan, planCts, TimeSpan.FromSeconds(1))) {
                safe = false;
                await Task.Delay(TimeSpan.FromSeconds(4));
                guard.Cancelled.Should().BeTrue();
            }

            planCts.IsCancellationRequested.Should().BeTrue();
            sequence.IsCancellationRequested.Should().BeFalse();
        }

        public static class WithSafeSymbol {

            public class WhenUnsafe : UnsafeStopGuardTest.WhenUnsafe {

                public bool Check() {
                    return true;
                }
            }
        }

        [Test]
        public async Task DoesNothingWithoutAnUnsafeTrigger() {
            bool safe = true;
            var monitor = Monitor(() => new SafetyMonitorInfo { Connected = true, IsSafe = safe });
            using var planCts = new CancellationTokenSource();

            using (var guard = new UnsafeStopGuard(monitor.Object, new SequentialContainer(), new SequentialContainer(), planCts, TimeSpan.FromSeconds(1))) {
                safe = false;
                await Task.Delay(TimeSpan.FromSeconds(3));
                guard.Interrupted.Should().BeFalse();
            }

            planCts.IsCancellationRequested.Should().BeFalse();
        }

        [Test]
        public async Task FollowsTheTriggersOwnSafeCheck() {
            bool safe = true;
            var monitor = Monitor(() => new SafetyMonitorInfo { Connected = true, IsSafe = safe });
            var owner = new SequentialContainer();
            owner.Add(new WithSafeSymbol.WhenUnsafe());
            using var planCts = new CancellationTokenSource();

            using (var guard = new UnsafeStopGuard(monitor.Object, owner, new SequentialContainer(), planCts, TimeSpan.FromSeconds(1))) {
                safe = false;
                await Task.Delay(TimeSpan.FromSeconds(3));
                guard.Interrupted.Should().BeFalse();
            }

            planCts.IsCancellationRequested.Should().BeFalse();
        }

        [Test]
        public async Task DoesNothingWithoutAConnectedMonitor() {
            var monitor = Monitor(() => new SafetyMonitorInfo { Connected = false, IsSafe = false });
            var plan = new SequentialContainer();
            var owner = new SequentialContainer();
            owner.Add(new WhenUnsafe());
            using var planCts = new CancellationTokenSource();

            using (var guard = new UnsafeStopGuard(monitor.Object, owner, plan, planCts, TimeSpan.FromSeconds(1))) {
                await Task.Delay(TimeSpan.FromSeconds(3));
                guard.Interrupted.Should().BeFalse();
            }

            planCts.IsCancellationRequested.Should().BeFalse();
        }

        [Test]
        public async Task SkipsTheRestInsteadOfCancellingTheUnsafeTrigger() {
            var monitor = Monitor(() => new SafetyMonitorInfo { Connected = true, IsSafe = false });
            var owner = new SequentialContainer();
            var trigger = new WhenUnsafe();
            owner.Add(trigger);
            var plan = new SequentialContainer();
            var next = new Step();
            plan.Add(next);
            using var planCts = new CancellationTokenSource();

            trigger.SetRunning(true);
            using (var guard = new UnsafeStopGuard(monitor.Object, owner, plan, planCts, TimeSpan.FromSeconds(1))) {
                await Task.Delay(TimeSpan.FromSeconds(3));
                guard.Action.Should().Be(UnsafeStopAction.SkipRest);
            }

            planCts.IsCancellationRequested.Should().BeFalse();
            next.Status.Should().Be(SequenceEntityStatus.SKIPPED);
        }
    }
}
