using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public static partial class MosaicCompletion {

        public static double Clamp(double progress) {
            if (double.IsNaN(progress) || progress < 0) {
                return 0;
            }

            return progress > 1 ? 1 : progress;
        }

        public static bool IsPastGoal(double progress) {
            return !double.IsNaN(progress) && progress >= 1;
        }

        public static double PanelProgress(IEnumerable<double> planProgress) {
            List<double> list = planProgress?.ToList() ?? new List<double>();
            return list.Count == 0 ? 0 : list.Select(Clamp).Min();
        }

        public static double MosaicProgress(IEnumerable<double> panelProgress) {
            List<double> list = panelProgress?.ToList() ?? new List<double>();
            return list.Count == 0 ? 0 : list.Select(Clamp).Min();
        }

        public static double DeficitScore(double panelProgress, double mosaicProgress) {
            double p = Clamp(panelProgress);
            if (IsPastGoal(p)) {
                return 0;
            }

            double pmin = Math.Min(Clamp(mosaicProgress), p);
            return (1 - p) * (1 - p) / (1 - pmin);
        }
    }
}
