using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public enum GoalKind {
        FaintSnr = 0,
        Depth = 1
    }

    public class GoalProgress {
        public string TargetGuid { get; set; }
        public string Filter { get; set; }
        public GoalKind Kind { get; set; }
        public double GoalValue { get; set; }
        public double AchievedValue { get; set; }
        public double Progress { get; set; }
        public double EffectiveHours { get; set; }
        public double HoursNeeded { get; set; }
        public double GainPerHourPct { get; set; }
        public bool Plateau { get; set; }
        public bool LowConfidence { get; set; }
        public bool Done { get; set; }
        public DateTime MeasuredAt { get; set; }
    }

    public interface IGoalProgressSource {

        GoalProgress Get(string targetGuid, string filter);

        IList<GoalProgress> ForTarget(string targetGuid);
    }
}
