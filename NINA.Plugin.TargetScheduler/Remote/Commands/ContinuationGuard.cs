using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class TargetSnapshot {
        public int ProjectId { get; set; }
        public int TargetId { get; set; }
        public int ProjectState { get; set; }
        public int ProjectPriority { get; set; }
        public bool TargetActive { get; set; }
        public ISet<int> EnabledPlanIds { get; set; } = new HashSet<int>();
    }

    public static class ContinuationGuard {
        public const int ActiveState = 1;

        public static string Reason(TargetSnapshot picked, TargetSnapshot now) {
            if (now == null) {
                return "the target no longer exists";
            }
            if (now.ProjectState != ActiveState) {
                return "its project is no longer active";
            }
            if (!now.TargetActive) {
                return "the target was made inactive";
            }
            if (picked != null && now.ProjectPriority != picked.ProjectPriority) {
                return $"its project priority changed from {picked.ProjectPriority} to {now.ProjectPriority}";
            }
            if (picked != null && picked.EnabledPlanIds != null) {
                var disabled = picked.EnabledPlanIds.Where(id => !now.EnabledPlanIds.Contains(id)).ToList();
                if (disabled.Count > 0) {
                    return $"exposure plan {string.Join(", ", disabled)} was disabled";
                }
                var enabled = now.EnabledPlanIds.Where(id => !picked.EnabledPlanIds.Contains(id)).ToList();
                if (enabled.Count > 0) {
                    return $"exposure plan {string.Join(", ", enabled)} was enabled";
                }
            }
            return null;
        }
    }
}
