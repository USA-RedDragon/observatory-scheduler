using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
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
                SubLengthChoice choice = Apply(exposure, target.Guid, glare);
                CapForBody(exposure, choice, target, atTime);
            }
        }

        public static double? BodyMaxSeconds(ITarget target, DateTime? atTime, double seconds, BodyField field = null, int binning = 1) {
            if (target == null || !target.IsBody()) { return null; }
            BodyEphemeris ephemeris = target.Ephemeris();
            double? rate = atTime.HasValue ? ephemeris?.MaxRate(atTime.Value, atTime.Value.AddSeconds(Math.Max(0, seconds))) : null;
            return BodyTrail.MaxSeconds(rate ?? ephemeris?.MaxRate(), field ?? BodyField.Current, binning);
        }

        public static void CapForBody(IExposure exposure, SubLengthChoice choice, ITarget target, DateTime? atTime, BodyField field = null) {
            if (exposure == null || choice == null || target == null || !target.IsBody()) { return; }
            BodyField f = field ?? BodyField.Current;
            int binning = Math.Max(1, (int)(exposure.BinningMode?.X ?? 1));
            double before = choice.Seconds;
            if (BodyTrail.Cap(choice, BodyMaxSeconds(target, atTime, choice.Seconds, f, binning))) {
                exposure.ExposureLength = choice.Seconds;
                exposure.LengthSource = choice.Source;
                TSLogger.Debug($"moving body {target.Name} ({target.Designation}) {exposure.FilterName}: sub length capped from {before:0.###} s to {choice.Seconds:0.###} s to keep the trail under {BodyField.MaxTrailPixels:0.#} px at {f.ImageScale * binning:0.###}\"/px (bin {binning})");
            }
        }

        public static double Seconds(IExposure exposure, ITarget target, DateTime atTime) {
            if (exposure == null) { return 0; }
            double? glare = target == null ? null : Glare(SkyConditions.CurrentObserver, target.CoordinatesAt(atTime), atTime);
            double template = exposure.TemplateExposureLength > 0 ? exposure.TemplateExposureLength : exposure.ExposureLength;
            bool planFirst = target != null && TargetWindowTable.Current.Get(target.Guid) != null;
            SubLengthChoice choice = SubLength.Choose(SubLengthTable.Current, target?.Guid, exposure.FilterName, exposure.Gain ?? -1, glare, exposure.PlanExposureLength, template, planFirst);
            if (target != null && target.IsBody()) {
                int binning = Math.Max(1, (int)(exposure.BinningMode?.X ?? 1));
                BodyTrail.Cap(choice, BodyMaxSeconds(target, atTime, choice.Seconds, null, binning));
            }
            return choice.Seconds;
        }

        public static SubLengthChoice Apply(IExposure exposure, string targetGuid, double? glare) {
            if (exposure == null) { return null; }
            double template = exposure.TemplateExposureLength > 0 ? exposure.TemplateExposureLength : exposure.ExposureLength;
            return Apply(exposure, targetGuid, exposure.FilterName, exposure.Gain ?? -1, template, glare);
        }

        public static SubLengthChoice Apply(IExposure exposure, string targetGuid, string filter, int gain, double templateExposure, double? glare) {
            bool planFirst = TargetWindowTable.Current.Get(targetGuid) != null;
            SubLengthChoice choice = SubLength.Choose(SubLengthTable.Current, targetGuid, filter, gain, glare, exposure.PlanExposureLength, templateExposure, planFirst);
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
