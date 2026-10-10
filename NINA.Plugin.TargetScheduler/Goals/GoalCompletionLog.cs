using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class GoalCompletionLog {
        public static GoalCompletionLog Instance { get; } = new GoalCompletionLog();

        private readonly Dictionary<string, string> last = new Dictionary<string, string>();

        public static string StateName(GoalState state) {
            switch (state) {
                case GoalState.Collecting: return "collecting";
                case GoalState.Unmeasurable: return "unmeasurable";
                case GoalState.Pending: return "pending";
                default: return "measured";
            }
        }

        public static string Decision(GoalProgress goal, int localSubs) {
            return StateName(goal.State) + " " + (GoalCompletion.IsComplete(goal, localSubs) ? "closed" : "open");
        }

        public string Observe(string targetName, string targetGuid, string filter, int planId, GoalProgress goal, int localSubs, double exposureSeconds, out bool firstSeen) {
            firstSeen = false;
            string guid = SqliteGoalProgressSource.NormalizeGuid(targetGuid ?? goal?.TargetGuid);
            string f = SqliteGoalProgressSource.NormalizeFilter(filter ?? goal?.Filter);
            if (goal == null || guid == null || f == null) { return null; }

            string now = Decision(goal, localSubs);
            string key = guid + "\u001f" + f + "\u001f" + planId.ToString(CultureInfo.InvariantCulture);
            string before;
            lock (last) {
                if (last.TryGetValue(key, out before) && before == now) { return null; }
                last[key] = now;
            }

            firstSeen = before == null;
            return Message(targetName ?? guid, filter ?? goal.Filter, before ?? "none", now, goal, localSubs, exposureSeconds);
        }

        public static string Message(string target, string filter, string before, string now, GoalProgress goal, int localSubs, double exposureSeconds) {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string subs = goal.StackSubs.HasValue ? goal.StackSubs.Value.ToString(ci) : $"{localSubs.ToString(ci)} acquired (no stack_subs)";
            string min = goal.MinSubs.HasValue ? goal.MinSubs.Value.ToString(ci) : "none";
            string limit = goal.SubLimit.HasValue ? goal.SubLimit.Value.ToString(ci) : "none";
            string hours = SeasonalRunway.KnownHours(goal)
                ? goal.HoursNeeded.Value.ToString("0.00", ci) + "h measured"
                : "unknown";
            int remaining = GoalCompletion.RemainingExposures(goal, exposureSeconds, localSubs);
            return $"goal completion {target}/{filter}: {before} -> {now}; stack_subs={subs}, min_subs={min}, sub_limit={limit}, done={goal.Done}, plateau={goal.Plateau}, hours_needed={hours}, remaining_subs={remaining.ToString(ci)}"
                + (string.IsNullOrWhiteSpace(goal.Reason) ? "" : $", reason={goal.Reason}");
        }

        public void Clear() {
            lock (last) { last.Clear(); }
        }
    }
}
