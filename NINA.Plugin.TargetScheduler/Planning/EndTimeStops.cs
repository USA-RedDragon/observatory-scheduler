using System;

namespace NINA.Plugin.TargetScheduler.Planning {

    /// <summary>
    /// Remembers the target the end time trigger last stopped. The trigger stops a plan when the next
    /// instruction would run past the target's end time, which counts the dither or settle before the
    /// exposure; the planner only checks the exposure. Without this, the planner continues the target
    /// it just stopped, and every new plan is stopped again at its first instruction until the end time.
    /// </summary>
    public static class EndTimeStops {
        private static readonly object lockObj = new object();
        private static int targetId = -1;
        private static DateTime endTime = DateTime.MinValue;

        public static void Record(int stoppedTargetId, DateTime stoppedEndTime) {
            lock (lockObj) {
                targetId = stoppedTargetId;
                endTime = stoppedEndTime;
            }
        }

        public static void Clear() {
            Record(-1, DateTime.MinValue);
        }

        /// <summary>
        /// The end time the trigger stopped this target at, while that time is still ahead; null otherwise.
        /// </summary>
        public static DateTime? StoppedUntil(int id, DateTime atTime) {
            lock (lockObj) {
                return id == targetId && atTime < endTime ? endTime : (DateTime?)null;
            }
        }
    }
}
