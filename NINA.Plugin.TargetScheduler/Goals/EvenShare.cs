using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class EvenShare {
        public const double DaysPerDegree = 365.2422 / 360;

        public static double Score(double panelHours, double totalHours, int panelUnfinished, int totalUnfinished) {
            if (panelUnfinished <= 0 || totalUnfinished <= 0) { return 0; }
            if (!(totalHours > 0)) { return 1; }
            double fair = (double)panelUnfinished / totalUnfinished;
            double actual = Math.Max(0, panelHours) / totalHours;
            return GoalCompletion.Clamp01(1 - actual / (2 * fair));
        }

        public static double Hours(IEnumerable<PanelExposure> exposures, DateTime fromUtc, DateTime toUtc) {
            return (exposures ?? Enumerable.Empty<PanelExposure>()).Where(e => e != null && e.At >= fromUtc && e.At <= toUtc).Sum(e => e.Seconds) / 3600;
        }

        public static double PanelScore(IEnumerable<(bool Self, int Unfinished, double Hours)> panels) {
            double panelHours = 0, totalHours = 0;
            int panelUnfinished = 0, totalUnfinished = 0;
            foreach ((bool self, int unfinished, double hours) in panels ?? Enumerable.Empty<(bool, int, double)>()) {
                if (unfinished <= 0) { continue; }
                totalUnfinished += unfinished;
                totalHours += hours;
                if (self) {
                    panelUnfinished = unfinished;
                    panelHours = hours;
                }
            }
            return Score(panelHours, totalHours, panelUnfinished, totalUnfinished);
        }

        public static double SunRightAscension(DateTime utc) {
            double n = (utc.ToUniversalTime() - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;
            double l = 280.460 + 0.9856474 * n;
            double g = (357.528 + 0.9856003 * n) * Math.PI / 180;
            double lambda = (l + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * Math.PI / 180;
            double eps = (23.439 - 0.0000004 * n) * Math.PI / 180;
            double ra = Math.Atan2(Math.Cos(eps) * Math.Sin(lambda), Math.Cos(lambda)) * 180 / Math.PI;
            return Wrap360(ra);
        }

        public static DateTime SeasonStart(DateTime nowUtc, double raDegrees) {
            DateTime start = nowUtc.ToUniversalTime();
            for (int i = 0; i < 3; i++) {
                double behind = Wrap360(SunRightAscension(start) - raDegrees);
                if (i > 0 && behind > 180) { behind -= 360; }
                start = start.AddDays(-behind * DaysPerDegree);
            }
            return start;
        }

        public static double MeanRightAscension(IEnumerable<double> raDegrees) {
            double x = 0, y = 0;
            foreach (double ra in raDegrees ?? Enumerable.Empty<double>()) {
                x += Math.Cos(ra * Math.PI / 180);
                y += Math.Sin(ra * Math.PI / 180);
            }
            return Wrap360(Math.Atan2(y, x) * 180 / Math.PI);
        }

        private static double Wrap360(double a) {
            a %= 360;
            return a < 0 ? a + 360 : a;
        }
    }
}
