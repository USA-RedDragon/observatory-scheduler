using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    /// <summary>
    /// Reads what-if overrides. A rule weight override has entity "ruleweight", the rule name as its field and a
    /// project id, or 0 for every project; a project's own override wins over the one for every project. A skip
    /// override has entity "skip", the scope ("target" or "project") as its field, the target or project id, and
    /// the minutes to skip, or 0 for the rest of the night; it applies when the running exposure ends, as a skip
    /// sent now would.
    /// </summary>
    public static class PreviewOverrides {
        public const string Project = "project";
        public const string RuleWeight = "ruleweight";
        public const string Skip = "skip";
        public const int EveryProject = 0;

        public static readonly IReadOnlyList<string> Kinds = new[] { Project, RuleWeight, Skip };

        public static List<SkipPayload> Skips(IEnumerable<PreviewOverride> overrides) {
            return (overrides ?? Enumerable.Empty<PreviewOverride>())
                .Where(o => o != null && o.Entity == Skip && o.Id > 0 && o.Value >= 0 && !double.IsNaN(o.Value))
                .Select(o => o.Field == "project"
                    ? new SkipPayload { Scope = "project", ProjectId = o.Id, Minutes = (int)o.Value }
                    : new SkipPayload { Scope = o.Field, TargetId = o.Id, Minutes = (int)o.Value })
                .Where(p => SkipHandler.CheckSkip(p) == null)
                .ToList();
        }

        public static Dictionary<string, double> RuleWeights(IEnumerable<PreviewOverride> overrides, int projectId) {
            var result = new Dictionary<string, double>();
            var valid = (overrides ?? Enumerable.Empty<PreviewOverride>())
                .Where(o => o != null && o.Entity == RuleWeight && !string.IsNullOrEmpty(o.Field) && o.Value >= 0 && o.Value <= 100 && !double.IsNaN(o.Value))
                .ToList();
            foreach (var o in valid.Where(o => o.Id == EveryProject)) {
                result[o.Field] = o.Value;
            }
            foreach (var o in valid.Where(o => o.Id == projectId && projectId != EveryProject)) {
                result[o.Field] = o.Value;
            }
            return result;
        }

        public static void Apply<T>(IList<T> rows, IReadOnlyDictionary<string, double> weights, Func<T, string> name, Action<T, double> set, Func<string, double, T> create) {
            foreach (var kv in weights) {
                var matched = false;
                foreach (var row in rows.Where(r => name(r) == kv.Key)) {
                    set(row, kv.Value);
                    matched = true;
                }
                if (!matched) {
                    rows.Add(create(kv.Key, kv.Value));
                }
            }
        }
    }
}
