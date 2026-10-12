using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class FilterCondition {
        public string Filter { get; set; }
        public bool Narrowband { get; set; }
        public double MoonGlare { get; set; }
        public double PanelMatch { get; set; } = 1;
        public double? Worth { get; set; }
        public double? BroadbandWorth { get; set; }
    }

    public static class ConditionMatch {
        public const double LunarCycleDays = 29.53059;
        public const double GlareFarSeparation = 150;
        public const double GlareFullSeparation = 30;
        public const double NarrowbandMoonTolerance = 0.25;
        public const double NarrowbandQualityFloor = 0.5;
        public const double BroadbandSuitableScore = 0.5;

        public static double MoonIllumination(double moonAgeDays) {
            if (double.IsNaN(moonAgeDays)) { return 0; }
            return (1 - Math.Cos(2 * Math.PI * moonAgeDays / LunarCycleDays)) / 2;
        }

        public static double MoonGlare(double moonAltitude, double illumination, double separation) {
            if (double.IsNaN(moonAltitude) || double.IsNaN(illumination) || double.IsNaN(separation)) { return 0; }
            double up = GoalCompletion.Clamp01((moonAltitude + 5) / 10);
            double near = GoalCompletion.Clamp01((GlareFarSeparation - separation) / (GlareFarSeparation - GlareFullSeparation));
            return GoalCompletion.Clamp01(illumination) * up * near;
        }

        public static double PeakAltitude(double latitude, double declination, double maximumAltitude) {
            double peak = 90 - Math.Abs(latitude - declination);
            if (maximumAltitude > 0 && maximumAltitude < peak) { peak = maximumAltitude; }
            return peak;
        }

        public static double AltitudeScore(double altitude, double peakAltitude) {
            if (double.IsNaN(altitude) || altitude <= 0 || peakAltitude <= 0) { return 0; }
            double now = Math.Sin(Math.Min(altitude, 90) * Math.PI / 180);
            double best = Math.Sin(Math.Min(peakAltitude, 90) * Math.PI / 180);
            return GoalCompletion.Clamp01(now / best);
        }

        public static double MoonScore(bool narrowband, double glare) {
            double g = GoalCompletion.Clamp01(glare);
            return narrowband ? 1 - NarrowbandMoonTolerance * g : 1 - g;
        }

        public static double MoonScore(bool narrowband, double glare, double? worth) {
            if (worth.HasValue && !double.IsNaN(worth.Value)) { return GoalCompletion.Clamp01(worth.Value); }
            return MoonScore(narrowband, glare);
        }

        public static double SkyScore(bool narrowband, double? quality) {
            if (!quality.HasValue) { return 1; }
            double q = GoalCompletion.Clamp01(quality.Value);
            return narrowband ? NarrowbandQualityFloor + (1 - NarrowbandQualityFloor) * q : q;
        }

        public static double FilterScore(FilterCondition filter, double? quality) {
            if (filter == null) { return 0; }
            return MoonScore(filter.Narrowband, filter.MoonGlare, filter.Worth) * SkyScore(filter.Narrowband, quality) * GoalCompletion.Clamp01(filter.PanelMatch);
        }

        public static bool NarrowbandFavored(double glare, double? quality) {
            return NarrowbandFavored(glare, quality, null);
        }

        public static bool NarrowbandFavored(double glare, double? quality, double? broadbandWorth) {
            return MoonScore(false, glare, broadbandWorth) * SkyScore(false, quality) < BroadbandSuitableScore;
        }

        public static bool Favored(FilterCondition filter, double? quality) {
            return filter != null && filter.Narrowband == NarrowbandFavored(filter.MoonGlare, quality, filter.BroadbandWorth);
        }

        public static double Score(double altitude, double peakAltitude, IEnumerable<FilterCondition> filters, double? quality) {
            List<FilterCondition> list = filters?.Where(f => f != null).ToList() ?? new List<FilterCondition>();
            if (list.Count == 0) { return 0; }
            List<FilterCondition> favored = list.Where(f => Favored(f, quality)).ToList();
            if (favored.Count > 0) { list = favored; }
            return AltitudeScore(altitude, peakAltitude) * list.Max(f => FilterScore(f, quality));
        }
    }
}
