using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public class ExposureCompletionHelper {
        private bool imageGradingEnabled;
        private double delayGrading;
        private double exposureThrottle;

        public ExposureCompletionHelper(bool imageGradingEnabled, double delayGrading, double exposureThrottle) {
            this.imageGradingEnabled = imageGradingEnabled;
            this.delayGrading = delayGrading;
            this.exposureThrottle = exposureThrottle / 100;
        }

        public GoalProgress GoalFor(IExposureCounts exposurePlan) {
            return GoalFor(exposurePlan, null);
        }

        private GoalProgress GoalFor(IExposureCounts exposurePlan, string targetGuid) {
            IGoalProgressSource goalSource = MosaicCompletion.GoalSource;
            if (goalSource == null || exposurePlan == null) { return null; }

            string filterName = null;
            if (exposurePlan is IExposure exposure) {
                targetGuid ??= exposure.PlanTarget?.Guid;
                filterName = exposure.FilterName;
            } else if (exposurePlan is ExposurePlan plan) {
                targetGuid ??= plan.Target?.Guid;
                filterName = plan.ExposureTemplate?.FilterName;
            }

            if (string.IsNullOrWhiteSpace(targetGuid) || string.IsNullOrWhiteSpace(filterName)) { return null; }
            return goalSource.Get(targetGuid, filterName);
        }

        private static double ExposureSeconds(IExposureCounts exposurePlan) {
            if (exposurePlan is IExposure exposure) { return exposure.ExposureLength; }
            if (exposurePlan is ExposurePlan plan) {
                return plan.Exposure > 0 ? plan.Exposure : plan.ExposureTemplate?.DefaultExposure ?? 0;
            }

            return 0;
        }

        public double PercentComplete(IExposureCounts exposurePlan) {
            return PercentComplete(exposurePlan, null);
        }

        private double PercentComplete(IExposureCounts exposurePlan, string targetGuid) {
            GoalProgress goal = GoalFor(exposurePlan, targetGuid);
            if (goal != null) {
                return GoalCompletion.PercentComplete(goal);
            }

            if (imageGradingEnabled) {
                // If delayed grading threshold has not been reached, then 'provisional' completion is based on raw number acquired
                if (IsProvisionalPercentComplete(exposurePlan)) {
                    return Percentage(exposurePlan.Acquired, exposurePlan.Desired);
                }

                return Percentage(exposurePlan.Accepted, exposurePlan.Desired);
            }

            if (exposurePlan.Acquired == 0) { return 0; }
            double throttleAt = (int)(exposureThrottle * exposurePlan.Desired);
            double percent = (exposurePlan.Acquired / throttleAt) * 100;
            return percent < 100 ? percent : 100;
        }

        public double PercentComplete(Target target, bool noExposurePlansIsComplete = false) {
            if (target.ExposurePlans.Count == 0) {
                return noExposurePlansIsComplete ? 100 : 0;
            }

            return target.ExposurePlans.Sum(ep => PercentComplete(ep, target.Guid)) / target.ExposurePlans.Count;
        }

        public double PercentComplete(ITarget target) {
            List<IExposure> list = new List<IExposure>();
            list.AddRange(target.ExposurePlans);
            list.AddRange(target.CompletedExposurePlans);

            if (list.Count == 0) { return 0; }
            return list.Sum(PercentComplete) / list.Count;
        }

        public bool HasEnabledPlans(Target target) {
            return target.ExposurePlans.Find(ep => ep.IsEnabled) != null;
        }

        public bool IsIncomplete(ITarget target) {
            List<IExposure> list = new List<IExposure>();
            list.AddRange(target.ExposurePlans);
            list.AddRange(target.CompletedExposurePlans);

            if (list.Count == 0) { return true; }
            return list.Any(IsIncomplete);
        }

        public bool IsProvisionalPercentComplete(IExposureCounts exposurePlan) {
            if (GoalFor(exposurePlan) != null) { return false; }
            return imageGradingEnabled && delayGrading > 0 && CurrentDelayThreshold(exposurePlan) < delayGrading;
        }

        public int RemainingExposures(IExposureCounts exposurePlan) {
            GoalProgress goal = GoalFor(exposurePlan);
            if (goal != null) {
                return GoalCompletion.RemainingExposures(goal, ExposureSeconds(exposurePlan), exposurePlan.Acquired);
            }

            return CountRemainingExposures(exposurePlan);
        }

        public int CountRemainingExposures(IExposureCounts exposurePlan) {
            if (imageGradingEnabled) {
                return exposurePlan.Accepted >= exposurePlan.Desired ? 0 : exposurePlan.Desired - exposurePlan.Accepted;
            }

            int throttleAt = (int)(exposureThrottle * exposurePlan.Desired);
            return exposurePlan.Acquired >= throttleAt ? 0 : throttleAt - exposurePlan.Acquired;
        }

        public bool IsIncomplete(IExposureCounts exposurePlan) {
            GoalProgress goal = GoalFor(exposurePlan);
            if (goal != null) {
                return !GoalCompletion.IsComplete(goal, exposurePlan.Acquired);
            }

            return PercentComplete(exposurePlan) < 100;
        }

        private double Percentage(double num, double denom) {
            if (denom == 0) { return 0; }
            double percent = (num / denom) * 100;
            return percent < 100 ? percent : 100;
        }

        private double CurrentDelayThreshold(IExposureCounts exposurePlan) {
            return exposurePlan.Desired == 0 ? 0 : ((double)exposurePlan.Acquired / (double)exposurePlan.Desired) * 100;
        }
    }
}