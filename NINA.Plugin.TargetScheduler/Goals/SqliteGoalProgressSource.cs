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

            List<GoalProgressRow> rows = (progress ?? Enumerable.Empty<GoalProgressRow>()).Where(r => r != null).ToList();
            int? minSubs = rows.Where(r => r.MinSubs.HasValue && r.MinSubs.Value > 0).Select(r => r.MinSubs).DefaultIfEmpty(null).Max();

            foreach (GoalProgressRow row in rows) {
                string key = Key(row.TargetGuid, row.Filter);
                if (key == null || !goalsByKey.TryGetValue(key, out GoalRow goal)) { continue; }
                Add(key, Build(goal, row));
            }

            if (!minSubs.HasValue) { return; }
            foreach (KeyValuePair<string, GoalRow> entry in goalsByKey) {
                if (byKey.ContainsKey(entry.Key)) { continue; }
                Add(entry.Key, Pending(entry.Value, minSubs));
            }
        }

        private void Add(string key, GoalProgress gp) {
            byKey[key] = gp;
            string guid = NormalizeGuid(gp.TargetGuid);
            if (!byTarget.TryGetValue(guid, out List<GoalProgress> list)) {
                list = new List<GoalProgress>();
                byTarget[guid] = list;
            }
            list.Add(gp);
        }

        public static GoalState ParseState(string state) {
            switch (state?.Trim().ToLowerInvariant()) {
                case null:
                case "":
                case "measured":
                    return GoalState.Measured;

                case "unmeasurable":
                    return GoalState.Unmeasurable;

                default:
                    return GoalState.Collecting;
            }
        }

        private static GoalProgress Pending(GoalRow goal, int? minSubs) {
            return new GoalProgress {
                TargetGuid = goal.TargetGuid.Trim(),
                Filter = goal.Filter.Trim(),
                Kind = goal.Kind,
                GoalValue = goal.Kind == GoalKind.Depth ? goal.DepthGoal ?? 0 : goal.SnrGoal,
                State = GoalState.Pending,
                Reason = "no goal progress from the stacker yet",
                MinSubs = minSubs
            };
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
                HoursNeeded = KnownHours(row.HoursNeeded),
                GainPerHourPct = Finite(row.GainPerHourPct),
                Plateau = row.Plateau,
                LowConfidence = row.LowConfidence,
                Done = row.Done,
                MeasuredAt = row.MeasuredAt,
                State = ParseState(row.State),
                Reason = row.Reason,
                StackSubs = row.StackSubs,
                MinSubs = row.MinSubs,
                SubLimit = row.SubLimit
            };
        }

        private static double? KnownHours(double? value) {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0) { return null; }
            return value.Value;
        }

        private static double Finite(double? value) {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) { return 0; }
            return value.Value;
        }
    }
}
