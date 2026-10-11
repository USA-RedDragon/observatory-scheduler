using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public class SteeringExposureSelector : BaseExposureSelector, IExposureSelector {
        private readonly IExposureSelector inner;

        public IExposureSelector Inner => inner;

        public SteeringExposureSelector(IProject project, ITarget target, IExposureSelector inner) : base(target) {
            this.inner = inner;
            DitherManager = GetDitherManager(project, target);
        }

        public IExposure Select(DateTime atTime, IProject project, ITarget target) {
            IExposure fallback = inner.Select(atTime, project, target);
            try {
                IExposure steered = Steer(atTime, project, target, fallback);
                if (steered == null || ReferenceEquals(steered, fallback)) { return fallback; }

                steered.PreDither = DitherManager.DitherRequired(steered);
                TSLogger.Info($"filter steering for {project.Name}/{target.Name}: {steered.FilterName} instead of {fallback?.FilterName ?? "none"}");
                return steered;
            } catch (Exception ex) {
                TSLogger.Warning($"filter steering failed for {target?.Name}, using the default order: {ex.Message}");
                return fallback;
            }
        }

        public static IExposure Steer(DateTime atTime, IProject project, ITarget target, IExposure current) {
            ExposureCompletionHelper helper = project.ExposureCompletionHelper;
            if (helper == null || target.ExposurePlans == null) { return null; }

            ObserverInfo observer = SkyConditions.CurrentObserver;
            double glare = observer == null ? 0 : SkyConditions.MoonGlare(observer, target.Coordinates, atTime);

            List<SteeringCandidate> candidates = new List<SteeringCandidate>();
            int currentIndex = -1;
            for (int i = 0; i < target.ExposurePlans.Count; i++) {
                IExposure plan = target.ExposurePlans[i];
                if (plan == null || plan.Rejected) { continue; }
                if (ReferenceEquals(plan, current)) { currentIndex = i; }
                candidates.Add(new SteeringCandidate {
                    Index = i,
                    Filter = plan.FilterName,
                    Goal = helper.GoalFor(plan),
                    PercentComplete = helper.PercentComplete(plan),
                    Narrowband = FilterKind.IsNarrowband(plan.FilterName),
                    MoonGlare = glare
                });
            }

            double? quality = SkyConditions.NightQuality(atTime);
            int chosen = FilterSteering.Choose(candidates, currentIndex, quality);
            TSLogger.Debug($"filter steering conditions for {target.Name}: glare {glare:0.00}, quality {(quality.HasValue ? quality.Value.ToString("0.00") : "unknown")}, favors {(ConditionMatch.NarrowbandFavored(glare, quality) ? "narrowband" : "broadband")}");
            return chosen < 0 ? null : target.ExposurePlans[chosen];
        }

        public void ExposureTaken(IExposure exposure) {
            inner.ExposureTaken(exposure);
        }

        public override void ExposureAborted(IExposure exposure) {
            inner.ExposureAborted(exposure);
        }

        public void TargetReset() {
            inner.TargetReset();
        }
    }
}
