using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public static class PanelBalance {
        private static readonly ConcurrentDictionary<long, double> glareCache = new ConcurrentDictionary<long, double>();
        private static readonly ConditionalWeakTable<PanelHistory, ConcurrentDictionary<(int, string), double[]>> liveHours = new ConditionalWeakTable<PanelHistory, ConcurrentDictionary<(int, string), double[]>>();

        public static IEnumerable<PanelExposure> Exposures(ITarget target) {
            if (target == null) { return Enumerable.Empty<PanelExposure>(); }
            IEnumerable<PanelExposure> live = PanelHistory.Current.For(target.DatabaseId);
            PanelHistory preview = target.Project?.ExposureCompletionHelper?.PreviewHistory;
            return preview == null ? live : live.Concat(preview.For(target.DatabaseId));
        }

        public static double Glare(PanelExposure exposure, Coordinates coordinates, ObserverInfo observer) {
            if (exposure.Glare.HasValue) { return exposure.Glare.Value; }
            if (exposure.Id > 0 && glareCache.TryGetValue(exposure.Id, out double cached)) { return cached; }
            double glare = SkyConditions.MoonGlare(observer, coordinates, DateTime.SpecifyKind(exposure.At, DateTimeKind.Utc));
            if (exposure.Id > 0) { glareCache[exposure.Id] = glare; }
            return glare;
        }

        public static double[] SiblingHours(ITarget panel, string filter, ObserverInfo observer) {
            string want = SqliteGoalProgressSource.NormalizeFilter(filter);
            if (want == null || observer == null || !MosaicCompletion.IsMosaicPanel(panel)) { return null; }
            PanelHistory live = PanelHistory.Current;
            ConcurrentDictionary<(int, string), double[]> cache = liveHours.GetValue(live, _ => new ConcurrentDictionary<(int, string), double[]>());
            PanelHistory preview = panel.Project.ExposureCompletionHelper?.PreviewHistory;
            double[] total = new double[PanelConditions.Buckets];
            foreach (ITarget sibling in panel.Project.Targets.Where(s => s != null && !ReferenceEquals(s, panel) && s.DatabaseId != panel.DatabaseId && s.Coordinates != null)) {
                double[] h = cache.GetOrAdd((sibling.DatabaseId, want), _ => PanelConditions.SiblingHours(live.For(sibling.DatabaseId), want, e => Glare(e, sibling.Coordinates, observer)));
                Add(total, h);
                if (preview != null) {
                    Add(total, PanelConditions.SiblingHours(preview.For(sibling.DatabaseId), want, e => Glare(e, sibling.Coordinates, observer)));
                }
            }
            return total;
        }

        private static void Add(double[] total, double[] h) {
            if (h == null) { return; }
            for (int i = 0; i < total.Length; i++) { total[i] += h[i]; }
        }

        public static double ConditionFactor(ITarget panel, string filter, double glareNow, ObserverInfo observer) {
            double[] siblings = SiblingHours(panel, filter, observer);
            return siblings == null ? 1 : PanelConditions.Factor(siblings, glareNow);
        }

        public static void Record(ITarget target, IExposure exposure, DateTime startTime) {
            PanelHistory preview = target?.Project?.ExposureCompletionHelper?.PreviewHistory;
            if (preview == null || exposure == null || !MosaicCompletion.IsMosaicPanel(target)) { return; }
            DateTime at = startTime.ToUniversalTime();
            ObserverInfo observer = SkyConditions.CurrentObserver;
            preview.Add(new PanelExposure {
                TargetId = target.DatabaseId,
                Filter = exposure.FilterName,
                At = at,
                Seconds = exposure.ExposureLength,
                Glare = observer == null || target.Coordinates == null ? (double?)null : SkyConditions.MoonGlare(observer, target.Coordinates, at)
            });
        }
    }
}
