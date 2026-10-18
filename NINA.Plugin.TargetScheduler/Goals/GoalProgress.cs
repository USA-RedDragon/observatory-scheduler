using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public enum GoalKind {
        FaintSnr = 0,
        Depth = 1
    }

    public enum GoalState {
        Measured = 0,
        Collecting = 1,
        Unmeasurable = 2,
        Pending = 3
    }

    public class GoalProgress {
        public string TargetGuid { get; set; }
        public string Filter { get; set; }
        public GoalKind Kind { get; set; }
        public double GoalValue { get; set; }
        public double AchievedValue { get; set; }
        public double Progress { get; set; }
        public double EffectiveHours { get; set; }
        public double? HoursNeeded { get; set; }
        public double GainPerHourPct { get; set; }
        public bool Plateau { get; set; }
        public bool LowConfidence { get; set; }
        public bool Done { get; set; }
        public DateTime MeasuredAt { get; set; }
        public GoalState State { get; set; }
        public string Reason { get; set; }
        public int? StackSubs { get; set; }
        public int? MinSubs { get; set; }
        public int? SubLimit { get; set; }

        /// <summary>
        /// The score a new sub of this filter needs to go into the target's masters: the stacker's low-score cut,
        /// a fraction of the target's best score in the filter (of the filter's usual best while the target has
        /// not settled its own). Null when the stacker has not published it.
        /// </summary>
        public double? ScoreCut { get; set; }

        /// <summary>
        /// The score the stacker expects a sub of this filter to get at the night quality published with it: the
        /// median score of its past subs taken at about that night quality. Null when it is not known.
        /// </summary>
        public double? ExpectedScore { get; set; }

        /// <summary>
        /// The night quality published with ExpectedScore, the one it was estimated for. Null when not known.
        /// </summary>
        public double? ExpectedAtQuality { get; set; }

        public GoalProgress Copy() {
            return (GoalProgress)MemberwiseClone();
        }
    }

    public interface IGoalProgressSource {

        GoalProgress Get(string targetGuid, string filter);

        IList<GoalProgress> ForTarget(string targetGuid);
    }
}
