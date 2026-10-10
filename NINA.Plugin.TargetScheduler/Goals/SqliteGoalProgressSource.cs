using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class SqliteGoalProgressSource : IGoalProgressSource {
        public static readonly SqliteGoalProgressSource Empty = new SqliteGoalProgressSource(new List<GoalRow>(), new List<GoalProgressRow>());

        private readonly Dictionary<string, GoalProgress> byKey = new Dictionary<string, GoalProgress>();
        private readonly Dictionary<string, List<GoalProgress>> byTarget = new Dictionary<string, List<GoalProgress>>();
        private readonly HashSet<string> goalTargets = new HashSet<string>();

        public SqliteGoalProgressSource(IGoalRowProvider provider)
            : this(provider.LoadGoals(), provider.LoadProgress()) {
        }

        public SqliteGoalProgressSource(IEnumerable<GoalRow> goals, IEnumerable<GoalProgressRow> progress) {
            Dictionary<string, GoalRow> goalsByKey = new Dictionary<string, GoalRow>();
            foreach (GoalRow goal in goals ?? Enumerable.Empty<GoalRow>()) {
                string key = Key(goal.TargetGuid, goal.Filter);
                if (key == null) { continue; }
                goalsByKey[key] = goal;
                goalTargets.Add(NormalizeGuid(goal.TargetGuid));
            }

            foreach (GoalProgressRow row in progress ?? Enumerable.Empty<GoalProgressRow>()) {
                string key = Key(row.TargetGuid, row.Filter);
                if (key == null || !goalsByKey.TryGetValue(key, out GoalRow goal)) { continue; }

                GoalProgress gp = Build(goal, row);
                byKey[key] = gp;

                string guid = NormalizeGuid(row.TargetGuid);
                if (!byTarget.TryGetValue(guid, out List<GoalProgress> list)) {
                    list = new List<GoalProgress>();
                    byTarget[guid] = list;
                }
                list.Add(gp);
            }
        }

        public int Count => byKey.Count;

        public bool IsGoalDriven(string targetGuid) {
            string guid = NormalizeGuid(targetGuid);
            return guid != null && goalTargets.Contains(guid);
        }

        public GoalProgress Get(string targetGuid, string filter) {
            string key = Key(targetGuid, filter);
            if (key == null) { return null; }
            return byKey.TryGetValue(key, out GoalProgress gp) ? gp : null;
        }

        public IList<GoalProgress> ForTarget(string targetGuid) {
            string guid = NormalizeGuid(targetGuid);
            if (guid != null && byTarget.TryGetValue(guid, out List<GoalProgress> list)) {
                return list.AsReadOnly();
            }

            return new List<GoalProgress>();
        }

        public static string NormalizeGuid(string guid) {
            if (string.IsNullOrWhiteSpace(guid)) { return null; }
            return guid.Trim().ToLowerInvariant();
        }

        public static string NormalizeFilter(string filter) {
            if (string.IsNullOrWhiteSpace(filter)) { return null; }
            return filter.Trim().ToUpperInvariant();
        }

        private static string Key(string targetGuid, string filter) {
            string guid = NormalizeGuid(targetGuid);
            string f = NormalizeFilter(filter);
            if (guid == null || f == null) { return null; }
            return guid + "\u001f" + f;
        }

        private static GoalProgress Build(GoalRow goal, GoalProgressRow row) {
            GoalKind kind = row.Kind.HasValue && Enum.IsDefined(typeof(GoalKind), row.Kind.Value) ? (GoalKind)row.Kind.Value : goal.Kind;
            double goalValue = row.GoalValue ?? (kind == GoalKind.Depth ? goal.DepthGoal ?? 0 : goal.SnrGoal);

            return new GoalProgress {
                TargetGuid = row.TargetGuid.Trim(),
                Filter = row.Filter.Trim(),
                Kind = kind,
                GoalValue = goalValue,
                AchievedValue = Finite(row.AchievedValue),
                Progress = Finite(row.Progress),
                EffectiveHours = Finite(row.EffectiveHours),
                HoursNeeded = Finite(row.HoursNeeded),
                GainPerHourPct = Finite(row.GainPerHourPct),
                Plateau = row.Plateau,
                LowConfidence = row.LowConfidence,
                Done = row.Done,
                MeasuredAt = row.MeasuredAt
            };
        }

        private static double Finite(double? value) {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) { return 0; }
            return value.Value;
        }
    }
}
