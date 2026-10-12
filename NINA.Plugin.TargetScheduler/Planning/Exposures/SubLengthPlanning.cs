using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public static class SubLengthPlanning {

        public static double? Glare(ObserverInfo observer, Coordinates coordinates, DateTime atTime) {
            if (observer == null || coordinates == null) { return null; }
            try {
                return SkyConditions.MoonGlare(observer, coordinates, atTime);
            } catch (Exception ex) {
                TSLogger.Warning($"moon glare for the sub length failed, using the plan or template length: {ex.Message}");
                return null;
            }
        }

        public static void Apply(ITarget target, DateTime atTime) {
            if (target == null) { return; }
            double? glare = Glare(SkyConditions.CurrentObserver, target.CoordinatesAt(atTime), atTime);
            foreach (IExposure exposure in Plans(target)) {
                Apply(exposure, target.Guid, glare);
            }
        }

        public static SubLengthChoice Apply(IExposure exposure, string targetGuid, double? glare) {
            if (exposure == null) { return null; }
            double template = exposure.TemplateExposureLength > 0 ? exposure.TemplateExposureLength : exposure.ExposureLength;
            return Apply(exposure, targetGuid, exposure.FilterName, exposure.Gain ?? -1, template, glare);
        }

        public static SubLengthChoice Apply(IExposure exposure, string targetGuid, string filter, int gain, double templateExposure, double? glare) {
            SubLengthChoice choice = SubLength.Choose(SubLengthTable.Current, targetGuid, filter, gain, glare, exposure.PlanExposureLength, templateExposure);
            exposure.ExposureLength = choice.Seconds;
            exposure.LengthSource = choice.Source;
            exposure.LengthBucket = choice.Bucket;
            return choice;
        }

        private static IEnumerable<IExposure> Plans(ITarget target) {
            HashSet<IExposure> seen = new HashSet<IExposure>(ReferenceEqualityComparer.Instance);
            foreach (List<IExposure> list in new[] { target.AllExposurePlans, target.ExposurePlans }) {
                if (list == null) { continue; }
                foreach (IExposure exposure in list) {
                    if (exposure != null && seen.Add(exposure)) { yield return exposure; }
                }
            }
        }
    }
}
