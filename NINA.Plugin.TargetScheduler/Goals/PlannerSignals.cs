using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class PlannerSignals {
        public static readonly TimeSpan NightQualityMaxAge = TimeSpan.FromHours(3);
        public static readonly PlannerSignals Empty = new PlannerSignals(null);

        private static volatile PlannerSignals current = Empty;

        public static PlannerSignals Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, double> boosts = new Dictionary<string, double>();

        public double? Quality { get; }
        public DateTime? QualityAt { get; }

        public PlannerSignals(IEnumerable<GoalProgressRow> rows) {
            foreach (GoalProgressRow row in rows ?? Array.Empty<GoalProgressRow>()) {
                if (row == null) { continue; }

                string guid = SqliteGoalProgressSource.NormalizeGuid(row.TargetGuid);
                if (guid != null && row.SeasonBoost.HasValue && IsFinite(row.SeasonBoost.Value)) {
                    double boost = GoalCompletion.Clamp01(row.SeasonBoost.Value);
                    boosts[guid] = boosts.TryGetValue(guid, out double have) ? Math.Max(have, boost) : boost;
                }

                if (row.NightQuality.HasValue && IsFinite(row.NightQuality.Value) && row.NightQualityAt.HasValue
                    && (!QualityAt.HasValue || row.NightQualityAt.Value > QualityAt.Value)) {
                    Quality = GoalCompletion.Clamp01(row.NightQuality.Value);
                    QualityAt = row.NightQualityAt.Value;
                }
            }
        }

        public int BoostCount => boosts.Count;

        public double SeasonBoost(string targetGuid) {
            string guid = SqliteGoalProgressSource.NormalizeGuid(targetGuid);
            return guid != null && boosts.TryGetValue(guid, out double boost) ? boost : 0;
        }

        public double? NightQuality(DateTime nowUtc) {
            if (!Quality.HasValue || !QualityAt.HasValue) { return null; }
            TimeSpan age = nowUtc - QualityAt.Value;
            if (age > NightQualityMaxAge || age < -NightQualityMaxAge) { return null; }
            return Quality;
        }

        private static bool IsFinite(double value) {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
