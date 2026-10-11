using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public static partial class MosaicCompletion {

        public static int UnfinishedPlans(ITarget target) {
            return PanelPlans(target).Count(plan => !IsPastGoal(Clamp(PlanProgress(target, plan))));
        }

        public static DateTime SeasonStart(IProject project, DateTime atUtc) {
            IEnumerable<double> ras = (project?.Targets ?? new List<ITarget>()).Where(t => t?.Coordinates != null).Select(t => AstroUtil.HoursToDegrees(t.Coordinates.RA));
            return EvenShare.SeasonStart(atUtc, EvenShare.MeanRightAscension(ras));
        }

        public static int PanelCount(IProject project) {
            return project?.Targets?.Count(t => t != null) ?? 0;
        }

        public static double EvenScore(ITarget target, DateTime atTime) {
            if (!IsMosaicPanel(target)) {
                return 0;
            }

            DateTime at = atTime.ToUniversalTime();
            DateTime start = SeasonStart(target.Project, at);
            return EvenShare.PanelScore(target.Project.Targets.Where(p => p != null).Select(p => {
                int unfinished = UnfinishedPlans(p);
                return (ReferenceEquals(p, target), unfinished, unfinished == 0 ? 0 : EvenShare.Hours(PanelBalance.Exposures(p), start, at));
            }));
        }
    }
}
