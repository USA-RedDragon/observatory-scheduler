using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class CaptureOutcome<T> {
        public bool Aborted { get; private set; }
        public string Reason { get; private set; }
        public T Value { get; private set; }

        public static CaptureOutcome<T> Done(T value) => new CaptureOutcome<T> { Value = value };

        public static CaptureOutcome<T> Abort(string reason) => new CaptureOutcome<T> { Aborted = true, Reason = reason };
    }

    public class CaptureKey {
        public static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);

        public int PlanId { get; set; }
        public DateTime StartedAt { get; set; }

        public bool Matches(int planId, DateTime startedAt) {
            return planId == PlanId && (startedAt.ToUniversalTime() - StartedAt.ToUniversalTime()).Duration() <= StartTolerance;
        }
    }

    public enum AbortOutcome {
        Aborted,
        NotCapturing,
        OtherExposure,
        AlreadyAborting,
    }

    /// <summary>
    /// Runs one camera capture under a token linked to the sequence token, so a web abort cancels only
    /// the capture. NINA's capture path aborts the camera exposure when its token is cancelled; the
    /// sequence token, the plan container and the instruction status are left alone.
    /// </summary>
    public class CaptureGate {
        private readonly object sync = new object();
        private CancellationTokenSource current;
        private CaptureKey currentKey;
        private string abortReason;

        public bool Capturing {
            get {
                lock (sync) {
                    return current != null;
                }
            }
        }

        public async Task<CaptureOutcome<T>> Run<T>(Func<CancellationToken, Task<T>> capture, CaptureKey key, CancellationToken token) {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                lock (sync) {
                    current = cts;
                    currentKey = key;
                    abortReason = null;
                }
                try {
                    return CaptureOutcome<T>.Done(await capture(cts.Token));
                } catch (OperationCanceledException) when (!token.IsCancellationRequested && TakeReason(cts) is string reason) {
                    return CaptureOutcome<T>.Abort(reason);
                } finally {
                    lock (sync) {
                        if (ReferenceEquals(current, cts)) {
                            current = null;
                            currentKey = null;
                        }
                    }
                }
            }
        }

        public AbortOutcome Abort(string reason, int planId, DateTime startedAt) {
            CancellationTokenSource cts;
            lock (sync) {
                cts = current;
                if (cts == null) {
                    return AbortOutcome.NotCapturing;
                }
                if (abortReason != null) {
                    return AbortOutcome.AlreadyAborting;
                }
                if (currentKey == null || !currentKey.Matches(planId, startedAt)) {
                    return AbortOutcome.OtherExposure;
                }
                abortReason = reason ?? "abort requested";
            }
            Task.Run(() => {
                try {
                    cts.Cancel();
                } catch (ObjectDisposedException) {
                }
            });
            return AbortOutcome.Aborted;
        }

        private string TakeReason(CancellationTokenSource cts) {
            lock (sync) {
                return ReferenceEquals(current, cts) ? abortReason : null;
            }
        }
    }
}
