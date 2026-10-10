using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public static partial class MosaicCompletion {

        public static IGoalProgressSource GoalSource { get; set; }

        public static double? GoalProgress(ITarget target, IExposure plan) {
            IGoalProgressSource source = GoalSource;
            if (source == null || target == null || plan == null || string.IsNullOrEmpty(target.Guid)) {
                return null;
            }

            GoalProgress goal = source.Get(target.Guid, plan.FilterName);
            if (goal == null) {
                return null;
            }

            return goal.Done || goal.Plateau ? 1 : goal.Progress;
        }

        public static double PlanProgress(ITarget target, IExposure plan) {
            double? goal = GoalProgress(target, plan);
            if (goal.HasValue) {
                return goal.Value;
            }

            ExposureCompletionHelper helper = target?.Project?.ExposureCompletionHelper;
            return helper == null ? 0 : helper.PercentComplete(plan) / 100;
        }

        public static double PanelProgress(ITarget target) {
            return PanelProgress(PanelPlans(target).Select(plan => PlanProgress(target, plan)));
        }

        public static double MosaicProgress(IProject project) {
            if (project?.Targets == null) {
                return 0;
            }

            return MosaicProgress(project.Targets.Select(PanelProgress));
        }

        public static bool IsMosaicPanel(ITarget target) {
            IProject project = target?.Project;
            return project != null && project.IsMosaic && project.Targets != null && project.Targets.Count > 1;
        }

        public static double DeficitScore(ITarget target) {
            if (!IsMosaicPanel(target)) {
                return 0;
            }

            return DeficitScore(PanelProgress(target), MosaicProgress(target.Project));
        }

        public static bool IsPanelPastGoalWhileMosaicIncomplete(ITarget target) {
            if (!IsMosaicPanel(target)) {
                return false;
            }

            return IsPastGoal(PanelProgress(target)) && !IsPastGoal(MosaicProgress(target.Project));
        }

        private static IEnumerable<IExposure> PanelPlans(ITarget target) {
            if (target == null) {
                return Enumerable.Empty<IExposure>();
            }

            IEnumerable<IExposure> plans = (target.ExposurePlans ?? new List<IExposure>()).Concat(target.CompletedExposurePlans ?? new List<IExposure>());
            return plans.Where(plan => plan != null && plan.IsEnabled);
        }
    }
}
