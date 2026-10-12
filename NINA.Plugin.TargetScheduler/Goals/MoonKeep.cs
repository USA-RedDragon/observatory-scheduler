using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public struct MoonState {
        public double Illumination { get; set; }
        public double Altitude { get; set; }
        public double Separation { get; set; }
    }

    public class MoonKeepRow {
        public string Filter { get; set; }
        public bool Narrowband { get; set; }
        public bool MoonDown { get; set; }
        public double IllumFrom { get; set; }
        public double IllumTo { get; set; }
        public double AltFrom { get; set; }
        public double AltTo { get; set; }
        public double SepFrom { get; set; }
        public double SepTo { get; set; }
        public int Subs { get; set; }
        public int Kept { get; set; }
        public int Nights { get; set; }
        public double Rate { get; set; }
        public double RateHigh { get; set; }
        public double Worth { get; set; }
        public bool Usable { get; set; }
        public bool Skips { get; set; }

        public bool Contains(MoonState s) {
            if (MoonDown) { return s.Altitude <= 0; }
            if (s.Altitude <= 0) { return false; }
            return In(s.Illumination, IllumFrom, IllumTo, MoonKeep.IllumMax)
                && In(Math.Min(s.Altitude, MoonKeep.AltMax), AltFrom, AltTo, MoonKeep.AltMax)
                && In(s.Separation, SepFrom, SepTo, MoonKeep.SepMax);
        }

        private static bool In(double v, double from, double to, double max) {
            if (double.IsNaN(v)) { return false; }
            return v >= from && (v < to || (v <= to && to >= max));
        }
    }

    public class MoonKeepTable {
        public static readonly MoonKeepTable Empty = new MoonKeepTable(null);

        private static volatile MoonKeepTable current = Empty;

        public static MoonKeepTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, List<MoonKeepRow>> byFilter = new Dictionary<string, List<MoonKeepRow>>();
        private readonly Dictionary<string, bool> filterClass = new Dictionary<string, bool>();
        private readonly Dictionary<bool, List<MoonKeepRow>> byClass = new Dictionary<bool, List<MoonKeepRow>> { [false] = new List<MoonKeepRow>(), [true] = new List<MoonKeepRow>() };

        public int Count { get; }

        public MoonKeepTable(IEnumerable<MoonKeepRow> rows) {
            if (rows == null) { return; }
            foreach (MoonKeepRow row in rows) {
                if (row == null) { continue; }
                Count++;
                string f = MoonKeep.FilterKey(row.Filter);
                if (f == null) {
                    byClass[row.Narrowband].Add(row);
                    continue;
                }
                if (!byFilter.TryGetValue(f, out List<MoonKeepRow> list)) {
                    list = new List<MoonKeepRow>();
                    byFilter[f] = list;
                    filterClass[f] = row.Narrowband;
                }
                list.Add(row);
            }
        }

        public MoonKeepRow Lookup(string filter, MoonState state) {
            if (Count == 0) { return null; }
            string f = MoonKeep.FilterKey(filter);
            if (f != null && byFilter.TryGetValue(f, out List<MoonKeepRow> own)) {
                MoonKeepRow r = own.FirstOrDefault(x => x.Usable && x.Contains(state));
                if (r != null) { return r; }
            }
            bool narrowband = f != null && filterClass.TryGetValue(f, out bool nb) ? nb : FilterKind.IsNarrowband(filter);
            return byClass[narrowband].FirstOrDefault(x => x.Usable && x.Contains(state));
        }

        public MoonKeepRow Broadband(MoonState state) {
            if (Count == 0) { return null; }
            return byClass[false].FirstOrDefault(x => x.Usable && x.Contains(state));
        }
    }

    public static class MoonKeep {
        public const double IllumMax = 1;
        public const double AltMax = 90;
        public const double SepMax = 180;
        public const double GoodNightQuality = 0.3;
        public const string FilterReason = "bright Moon";
        public const string TargetReason = "all exposure plans rejected for a bright Moon";

        public static bool Waits(IEnumerable<string> exposureReasons, params string[] alongside) {
            List<string> list = exposureReasons?.ToList() ?? new List<string>();
            return list.Count > 0 && list.Contains(FilterReason)
                && list.All(r => r == FilterReason || (alongside != null && alongside.Contains(r)));
        }

        /// <summary>
        /// The key a filter name is matched on, the same for the stacker's names (H-a, O-III, S-II) and the
        /// template's (Ha, OIII, SII and so on): letters and digits only, lower case, line aliases folded.
        /// </summary>
        public static string FilterKey(string filter) {
            if (string.IsNullOrWhiteSpace(filter)) { return null; }
            string k = new string(filter.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (k.Length == 0) { return null; }
            switch (k) {
                case "halpha":
                case "hydrogenalpha":
                    return "ha";
                case "o3":
                    return "oiii";
                case "s2":
                    return "sii";
            }
            return k;
        }

        public static double? Worth(MoonKeepRow row) {
            if (row == null || double.IsNaN(row.Worth)) { return null; }
            return GoalCompletion.Clamp01(row.Worth);
        }

        public static bool Skips(MoonKeepRow row, double? nightQuality) {
            if (row == null || !row.Usable || !row.Skips || row.MoonDown) { return false; }
            return !nightQuality.HasValue || nightQuality.Value < GoodNightQuality;
        }

        public static string Explain(MoonKeepRow row, MoonState state) {
            if (row == null) { return null; }
            string what = string.IsNullOrWhiteSpace(row.Filter) ? (row.Narrowband ? "narrowband" : "broadband") : row.Filter.Trim();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Moon {0:0}% lit, {1:0}° up, {2:0}° away: past {3} subs like this kept {4} of {5} (under {6:0.#}%)",
                state.Illumination * 100, state.Altitude, state.Separation, what, row.Kept, row.Subs, Math.Ceiling(row.RateHigh * 1000) / 10);
        }
    }
}
