using System;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    public enum UnsafeStopAction {
        None,
        SkipRest,
        Cancel,
    }

    /// <summary>
    /// Decides, once per plan, when the safety monitor should stop the running plan. The plan is
    /// cancelled after the monitor has reported unsafe for the hold time, but only if this plan saw it
    /// safe first; a plan that starts unsafe leaves the work to the unsafe trigger before its first
    /// instruction. When the unsafe trigger itself is running, the rest of the plan is skipped instead,
    /// so the trigger is never cancelled. A monitor that is not connected never stops a plan.
    /// </summary>
    public class UnsafeStopRule {
        public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(4);

        private readonly TimeSpan hold;
        private bool sawSafe;

        public UnsafeStopRule(TimeSpan hold) {
            this.hold = hold;
        }

        public UnsafeStopAction Action { get; private set; } = UnsafeStopAction.None;

        public DateTime? UnsafeSince { get; private set; }

        public void Downgrade() {
            if (Action == UnsafeStopAction.Cancel) {
                Action = UnsafeStopAction.SkipRest;
            }
        }

        public UnsafeStopAction Observe(DateTime now, bool connected, bool safe, bool unsafeTriggerRunning) {
            if (Action != UnsafeStopAction.None) {
                return UnsafeStopAction.None;
            }
            if (unsafeTriggerRunning) {
                Action = UnsafeStopAction.SkipRest;
                return Action;
            }
            if (!connected || safe) {
                sawSafe |= connected;
                UnsafeSince = null;
                return UnsafeStopAction.None;
            }
            UnsafeSince ??= now;
            if (sawSafe && now - UnsafeSince.Value >= hold) {
                Action = UnsafeStopAction.Cancel;
            }
            return Action;
        }
    }
}
