using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class MoonParams {
        public bool Enabled { get; set; }
        public double Separation { get; set; }
        public int Width { get; set; }
        public double RelaxScale { get; set; }
        public double RelaxMaxAltitude { get; set; }
        public double RelaxMinAltitude { get; set; }
        public bool DownEnabled { get; set; }

        public string Key() {
            if (!Enabled) { return "off"; }
            return string.Join(",",
                Separation.ToString("0.###", CultureInfo.InvariantCulture),
                Width.ToString(CultureInfo.InvariantCulture),
                RelaxScale.ToString("0.###", CultureInfo.InvariantCulture),
                RelaxMaxAltitude.ToString("0.###", CultureInfo.InvariantCulture),
                RelaxMinAltitude.ToString("0.###", CultureInfo.InvariantCulture),
                DownEnabled ? "1" : "0");
        }
    }

    public class MoonSample {
        public double Altitude { get; set; }
        public double AgeDays { get; set; }
        public double RaDegrees { get; set; }
        public double Dec { get; set; }
    }

    public static class MoonAvoidance {

        public static double LorentzianSeparation(double moonAge, double distance, double width) {
            if (width == 0) { return 0; }
            return distance / (1 + Math.Pow((0.5 - (moonAge / ConditionMatch.LunarCycleDays)) / (width / ConditionMatch.LunarCycleDays), 2));
        }

        public static bool IsRejected(MoonParams p, double moonAltitude, double moonAge, double separation) {
            if (p == null || !p.Enabled) { return false; }

            double sepParam = p.Separation;
            double widthParam = p.Width;
            if (moonAltitude <= p.RelaxMaxAltitude && p.RelaxScale > 0) {
                sepParam += p.RelaxScale * (moonAltitude - p.RelaxMaxAltitude);
                widthParam *= (moonAltitude - p.RelaxMinAltitude) / (p.RelaxMaxAltitude - p.RelaxMinAltitude);
            }

            if (moonAltitude <= p.RelaxMinAltitude && p.RelaxScale > 0) { return false; }
            if (moonAltitude >= p.RelaxMaxAltitude && p.DownEnabled) { return true; }
            if (sepParam <= 0) { return false; }

            return separation < LorentzianSeparation(moonAge, sepParam, widthParam);
        }

        public static double Separation(double ra1Deg, double dec1Deg, double ra2Deg, double dec2Deg) {
            double d2r = Math.PI / 180;
            double a = Math.Sin(dec1Deg * d2r) * Math.Sin(dec2Deg * d2r)
                + Math.Cos(dec1Deg * d2r) * Math.Cos(dec2Deg * d2r) * Math.Cos((ra1Deg - ra2Deg) * d2r);
            return Math.Acos(Math.Max(-1, Math.Min(1, a))) / d2r;
        }
    }

    public class SeasonConstraints {
        public static readonly SeasonConstraints None = new SeasonConstraints();

        public double MaximumAltitude { get; set; }
        public int MeridianWindowMinutes { get; set; }
        public List<MoonParams> Moon { get; set; } = new List<MoonParams>();

        public bool NeedsMoon => Moon != null && Moon.Count > 0 && Moon.All(m => m != null && m.Enabled);

        public string Key() {
            IEnumerable<string> moon = (Moon ?? new List<MoonParams>()).Where(m => m != null).Select(m => m.Key()).Distinct().OrderBy(k => k, StringComparer.Ordinal);
            return string.Join(";",
                MaximumAltitude.ToString("0.###", CultureInfo.InvariantCulture),
                MeridianWindowMinutes.ToString(CultureInfo.InvariantCulture),
                string.Join("/", moon));
        }

        public bool PositionUsable(double altitude, double hourAngleHours) {
            if (MaximumAltitude > 0 && altitude > MaximumAltitude) { return false; }
            if (MeridianWindowMinutes > 0) {
                double ha = NormalizeHours(hourAngleHours);
                if (Math.Abs(ha) * 60 > MeridianWindowMinutes) { return false; }
            }

            return true;
        }

        public bool MoonAllows(MoonSample moon, double targetRaDeg, double targetDec) {
            if (!NeedsMoon || moon == null) { return true; }
            double sep = MoonAvoidance.Separation(moon.RaDegrees, moon.Dec, targetRaDeg, targetDec);
            return Moon.Any(p => !MoonAvoidance.IsRejected(p, moon.Altitude, moon.AgeDays, sep));
        }

        public static double NormalizeHours(double hours) {
            double h = hours % 24;
            if (h > 12) { h -= 24; }
            if (h < -12) { h += 24; }
            return h;
        }
    }
}
