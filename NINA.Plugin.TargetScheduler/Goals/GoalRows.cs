using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class GoalRow {
        public string TargetGuid { get; set; }
        public string Filter { get; set; }
        public GoalKind Kind { get; set; }
        public double SnrGoal { get; set; } = 10;
        public double? DepthGoal { get; set; }
        public bool PlateauStop { get; set; } = true;
        public string Region { get; set; }
        public string UpdatedAt { get; set; }
    }

    public class GoalProgressRow {
        public string TargetGuid { get; set; }
        public string Filter { get; set; }
        public int? Kind { get; set; }
        public double? GoalValue { get; set; }
        public double? AchievedValue { get; set; }
        public double? Progress { get; set; }
        public double? Snr { get; set; }
        public double? Depth { get; set; }
        public double? EffectiveHours { get; set; }
        public double? HoursNeeded { get; set; }
        public double? GainPerHourPct { get; set; }
        public bool Plateau { get; set; }
        public bool LowConfidence { get; set; }
        public bool Done { get; set; }
        public DateTime MeasuredAt { get; set; }
        public double? NightQuality { get; set; }
        public DateTime? NightQualityAt { get; set; }
        public double? SeasonBoost { get; set; }
    }

    public interface IGoalRowProvider {

        IList<GoalRow> LoadGoals();

        IList<GoalProgressRow> LoadProgress();
    }
}
