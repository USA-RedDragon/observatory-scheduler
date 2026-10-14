namespace NINA.Plugin.TargetScheduler.Sequencer {

    public static class PlanOutcome {

        /// <summary>
        /// The after each exposure instructions run when the plan saved a frame. A plan that ended
        /// before its exposure (stopped by the target end time, cancelled, skipped by a guard, or a
        /// failed download) took no frame, so counters kept in those instructions do not move. A
        /// cancelled sequence keeps the old behavior and runs them.
        /// </summary>
        public static bool RunsAfterEachExposure(bool sequenceCancelled, bool frameCompleted) {
            return sequenceCancelled || frameCompleted;
        }
    }
}
