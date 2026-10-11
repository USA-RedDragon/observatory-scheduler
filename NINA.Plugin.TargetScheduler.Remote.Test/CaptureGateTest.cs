using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class CaptureGateTest {
        private static readonly DateTime Start = new DateTime(2026, 10, 10, 6, 45, 0, 123, DateTimeKind.Utc);
        private static readonly CaptureKey Key = new CaptureKey { PlanId = 40, StartedAt = Start };

        private static async Task<int> Expose(CancellationToken token, TaskCompletionSource<bool> started, TaskCompletionSource<int> finish) {
            started.TrySetResult(true);
            using (token.Register(() => finish.TrySetCanceled(token))) {
                return await finish.Task;
            }
        }

        [Test]
        public async Task AbortCancelsOnlyTheCaptureAndReportsADiscardedFrame() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sequence = new CancellationTokenSource();
            var run = gate.Run(t => Expose(t, started, finish), Key, sequence.Token);
            await started.Task;
            gate.Capturing.Should().BeTrue();

            gate.Abort("abort by web", 40, Start).Should().Be(AbortOutcome.Aborted);
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

            outcome.Aborted.Should().BeTrue();
            outcome.Reason.Should().Be("abort by web");
            sequence.IsCancellationRequested.Should().BeFalse();
            gate.Capturing.Should().BeFalse();
        }

        [Test]
        public void AnUngatedSyncClientCaptureSaysWhyItCannotBeAborted() {
            var gate = new CaptureGate();
            gate.Blocked = CaptureGate.SyncClientBlocked;
            gate.Abort("abort by web", 40, Start).Should().Be(AbortOutcome.Blocked);
            gate.Blocked = null;
            gate.Abort("abort by web", 40, Start).Should().Be(AbortOutcome.NotCapturing);
        }

        [Test]
        public void AbortWithNothingCapturingDoesNothing() {
            var gate = new CaptureGate();
            gate.Abort("abort by web", 40, Start).Should().Be(AbortOutcome.NotCapturing);
        }

        [Test]
        public async Task AnAbortForAnotherExposureLeavesTheRunningOneAlone() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = gate.Run(t => Expose(t, started, finish), Key, CancellationToken.None);
            await started.Task;
            gate.Abort("stale", 40, Start.AddMinutes(-10)).Should().Be(AbortOutcome.OtherExposure);
            gate.Abort("other plan", 41, Start).Should().Be(AbortOutcome.OtherExposure);
            gate.Abort("ms rounding", 40, Start.AddMilliseconds(-123)).Should().Be(AbortOutcome.Aborted);
            (await run.WaitAsync(TimeSpan.FromSeconds(5))).Aborted.Should().BeTrue();
        }

        [Test]
        public async Task ACompletedCaptureKeepsItsFrame() {
            var gate = new CaptureGate();
            var outcome = await gate.Run(t => Task.FromResult(42), Key, CancellationToken.None);
            outcome.Aborted.Should().BeFalse();
            outcome.Value.Should().Be(42);
            gate.Abort("late", 40, Start).Should().Be(AbortOutcome.NotCapturing);
        }

        [Test]
        public async Task StoppingTheSequenceStillCancelsTheInstruction() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sequence = new CancellationTokenSource();
            var run = gate.Run(t => Expose(t, started, finish), Key, sequence.Token);
            await started.Task;
            sequence.Cancel();
            Func<Task> act = () => run.WaitAsync(TimeSpan.FromSeconds(5));
            await act.Should().ThrowAsync<OperationCanceledException>();
            gate.Capturing.Should().BeFalse();
        }

        [Test]
        public async Task ASequenceStopDuringAnAbortIsNotSwallowed() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sequence = new CancellationTokenSource();
            var run = gate.Run(t => Expose(t, started, finish), Key, sequence.Token);
            await started.Task;
            gate.Abort("abort by web", 40, Start).Should().Be(AbortOutcome.Aborted);
            sequence.Cancel();
            Func<Task> act = () => run.WaitAsync(TimeSpan.FromSeconds(5));
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task CameraFailuresAreNotTurnedIntoAborts() {
            var gate = new CaptureGate();
            Func<Task> act = () => gate.Run<int>(t => throw new InvalidOperationException("download failed"), Key, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task ASecondAbortForTheSameExposureIsANoOp() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = gate.Run(t => Expose(t, started, finish), Key, CancellationToken.None);
            await started.Task;
            gate.Abort("first", 40, Start).Should().Be(AbortOutcome.Aborted);
            gate.Abort("second", 40, Start).Should().Be(AbortOutcome.AlreadyAborting);
            (await run.WaitAsync(TimeSpan.FromSeconds(5))).Reason.Should().Be("first");
        }

        [Test]
        public async Task TheNextExposureStartsWithACleanGate() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = gate.Run(t => Expose(t, started, finish), Key, CancellationToken.None);
            await started.Task;
            gate.Abort("first", 40, Start);
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            var next = await gate.Run(t => Task.FromResult(7), Key, CancellationToken.None);
            next.Aborted.Should().BeFalse();
            next.Value.Should().Be(7);
        }

        [Test]
        public async Task AnAbortThatLosesTheRaceKeepsTheFrame() {
            var gate = new CaptureGate();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = gate.Run(async t => {
                started.TrySetResult(true);
                return await finish.Task;
            }, Key, CancellationToken.None);
            await started.Task;
            gate.Abort("late", 40, Start).Should().Be(AbortOutcome.Aborted);
            finish.SetResult(9);
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));
            outcome.Aborted.Should().BeFalse();
            outcome.Value.Should().Be(9);
        }

        [Test]
        public async Task SchedulerControlLeavesTheReplanToTheAbortedInstruction() {
            var control = new SchedulerControl();
            control.AbortExposure("exposure aborted by web", 40, Start).Should().Be(AbortOutcome.NotCapturing);

            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = control.Capture.Run(t => Expose(t, started, finish), Key, CancellationToken.None);
            await started.Task;
            control.AbortExposure("exposure aborted by web", 40, Start).Should().Be(AbortOutcome.Aborted);
            control.ReplanRequested.Should().BeFalse();
            (await run.WaitAsync(TimeSpan.FromSeconds(5))).Reason.Should().Be("exposure aborted by web");
        }

        [Test]
        public async Task AbortKeepsARemotePauseInPlace() {
            var control = new SchedulerControl();
            control.Pause(new PausePayload { Mount = "track" }, null);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = control.Capture.Run(t => Expose(t, started, finish), Key, CancellationToken.None);
            await started.Task;
            control.AbortExposure("exposure aborted by web", 40, Start).Should().Be(AbortOutcome.Aborted);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            control.Paused.Should().NotBeNull();
        }
    }
}
