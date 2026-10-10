using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class SimulatedStackSubs {
        private readonly Dictionary<string, int> taken = new Dictionary<string, int>();

        public void Add(string targetGuid, string filter) {
            string key = Key(targetGuid, filter);
            if (key == null) { return; }
            taken[key] = Taken(targetGuid, filter) + 1;
        }

        public int Taken(string targetGuid, string filter) {
            string key = Key(targetGuid, filter);
            return key != null && taken.TryGetValue(key, out int n) ? n : 0;
        }

        public GoalProgress Apply(GoalProgress goal, string targetGuid, string filter) {
            if (goal == null || !goal.StackSubs.HasValue) { return goal; }
            int n = Taken(targetGuid ?? goal.TargetGuid, filter ?? goal.Filter);
            if (n == 0) { return goal; }
            GoalProgress grown = goal.Copy();
            grown.StackSubs = goal.StackSubs.Value + n;
            return grown;
        }

        private static string Key(string targetGuid, string filter) {
            string guid = SqliteGoalProgressSource.NormalizeGuid(targetGuid);
            string f = SqliteGoalProgressSource.NormalizeFilter(filter);
            if (guid == null || f == null) { return null; }
            return guid + "\u001f" + f;
        }
    }
}
