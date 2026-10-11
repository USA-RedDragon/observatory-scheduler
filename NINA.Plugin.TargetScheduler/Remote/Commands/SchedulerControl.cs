using System;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class PauseState {
        public string Mount { get; set; }
        public DateTime? ResumeAt { get; set; }
        public DateTime RequestedAt { get; set; }
    }

    public class SchedulerControl : ISchedulerControl {
        private readonly object sync = new object();
        private string replanReason;
        private PauseState pause;

        public SkipList Skips { get; } = new SkipList();
        public CaptureGate Capture { get; } = new CaptureGate();
        public Func<DateTime, DateTime?> NightEndSource { get; set; }
        public Action<int?, int?> Invalidated { get; set; }
        public Action Changed { get; set; }

        public void RequestReplan(string reason) {
            lock (sync) {
                replanReason = reason ?? "replan requested";
            }
            Changed?.Invoke();
        }

        public string TakeReplan() {
            lock (sync) {
                var r = replanReason;
                replanReason = null;
                return r;
            }
        }

        public bool ReplanRequested {
            get {
                lock (sync) {
                    return replanReason != null;
                }
            }
        }

        public void Skip(SkipPayload skip, DateTime until) {
            Skips.Add(skip, until);
            Changed?.Invoke();
        }

        public bool Unskip(SkipPayload skip) {
            var had = Skips.Remove(skip);
            Changed?.Invoke();
            return had;
        }

        public void Pause(PausePayload p, DateTime? resumeAt) {
            lock (sync) {
                pause = new PauseState { Mount = p.Mount ?? "track", ResumeAt = resumeAt?.ToUniversalTime(), RequestedAt = DateTime.UtcNow };
            }
            Changed?.Invoke();
        }

        public bool Resume() {
            bool was;
            lock (sync) {
                was = pause != null;
                pause = null;
            }
            Changed?.Invoke();
            return was;
        }

        public PauseState Paused {
            get {
                lock (sync) {
                    return pause;
                }
            }
        }

        public bool ShouldResume(DateTime now) {
            lock (sync) {
                if (pause?.ResumeAt != null && pause.ResumeAt.Value <= now.ToUniversalTime()) {
                    pause = null;
                    return true;
                }
                return false;
            }
        }

        public AbortOutcome AbortExposure(string reason, int planId, DateTime startedAt) {
            return Capture.Abort(reason, planId, startedAt);
        }

        public DateTime? NightEnd(DateTime now) {
            return NightEndSource?.Invoke(now);
        }

        public void Invalidate(int? projectId, int? targetId) {
            Invalidated?.Invoke(projectId, targetId);
        }
    }
}
