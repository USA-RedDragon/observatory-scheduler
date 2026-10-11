using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class PanelExposure {
        public long Id { get; set; }
        public int TargetId { get; set; }
        public string Filter { get; set; }
        public DateTime At { get; set; }
        public double Seconds { get; set; }
        public double? Glare { get; set; }
    }

    public class PanelHistory {
        public static readonly PanelHistory Empty = new PanelHistory(null);

        private static volatile PanelHistory current = Empty;

        public static PanelHistory Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<int, List<PanelExposure>> byTarget = new Dictionary<int, List<PanelExposure>>();

        public PanelHistory(IEnumerable<PanelExposure> exposures) {
            foreach (PanelExposure e in exposures ?? Enumerable.Empty<PanelExposure>()) {
                Add(e);
            }
        }

        public int Count { get; private set; }

        public void Add(PanelExposure exposure) {
            if (exposure == null || exposure.Seconds <= 0 || double.IsNaN(exposure.Seconds)) { return; }
            if (!byTarget.TryGetValue(exposure.TargetId, out List<PanelExposure> list)) {
                list = new List<PanelExposure>();
                byTarget[exposure.TargetId] = list;
            }
            list.Add(exposure);
            Count++;
        }

        public IReadOnlyList<PanelExposure> For(int targetId) {
            return byTarget.TryGetValue(targetId, out List<PanelExposure> list) ? list : (IReadOnlyList<PanelExposure>)Array.Empty<PanelExposure>();
        }
    }

    public static class PanelConditions {
        public const int Buckets = 4;
        public const double AdjacentWeight = 0.5;
        public const double MinPrecedentHours = 1.0;
        public const double Floor = 0.25;
        private static readonly double[] edges = { 0.1, 0.35, 0.65 };

        public static int Bucket(double glare) {
            if (double.IsNaN(glare)) { return 0; }
            for (int i = 0; i < edges.Length; i++) {
                if (glare < edges[i]) { return i; }
            }
            return Buckets - 1;
        }

        public static double[] Histogram(IEnumerable<(double Glare, double Hours)> samples) {
            double[] hours = new double[Buckets];
            foreach ((double glare, double h) in samples ?? Enumerable.Empty<(double, double)>()) {
                if (h > 0 && !double.IsNaN(h) && !double.IsInfinity(h)) {
                    hours[Bucket(glare)] += h;
                }
            }
            return hours;
        }

        public static double Kernel(int a, int b) {
            int d = Math.Abs(a - b);
            return d == 0 ? 1 : d == 1 ? AdjacentWeight : 0;
        }

        public static double Match(double[] siblingHours, int bucket) {
            if (siblingHours == null || siblingHours.Sum() < MinPrecedentHours) { return 1; }
            double Near(int b) => Enumerable.Range(0, Buckets).Sum(i => siblingHours[i] * Kernel(b, i));
            double best = Enumerable.Range(0, Buckets).Max(Near);
            return best > 0 ? GoalCompletion.Clamp01(Near(bucket) / best) : 1;
        }

        public static double[] SiblingHours(IEnumerable<PanelExposure> siblingExposures, string filter, Func<PanelExposure, double> glare) {
            string want = SqliteGoalProgressSource.NormalizeFilter(filter);
            if (want == null || glare == null) { return null; }
            return Histogram((siblingExposures ?? Enumerable.Empty<PanelExposure>())
                .Where(e => e != null && SqliteGoalProgressSource.NormalizeFilter(e.Filter) == want)
                .Select(e => (glare(e), e.Seconds / 3600)));
        }

        public static double Factor(double[] siblingHours, double glare) {
            return Floor + (1 - Floor) * Match(siblingHours, Bucket(glare));
        }
    }
}
