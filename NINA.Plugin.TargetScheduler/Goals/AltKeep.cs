using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class AltKeepRow {
        public string Filter { get; set; }
        public bool Narrowband { get; set; }
        public double AltFrom { get; set; }
        public double AltTo { get; set; }
        public int Subs { get; set; }
        public int Kept { get; set; }
        public int Nights { get; set; }
        public double Rate { get; set; }
        public double RateHigh { get; set; }
        public double Worth { get; set; }
        public bool Usable { get; set; }
        public bool Skips { get; set; }

        public bool Contains(double altitude) {
            if (double.IsNaN(altitude) || altitude < AltFrom) { return false; }
            return altitude < AltTo || (altitude <= AltTo && AltTo >= AltKeep.AltMax);
        }
    }

    public class AltKeepTable {
        public static readonly AltKeepTable Empty = new AltKeepTable(null);

        private static volatile AltKeepTable current = Empty;

        public static AltKeepTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, List<AltKeepRow>> byFilter = new Dictionary<string, List<AltKeepRow>>();
        private readonly Dictionary<string, bool> filterClass = new Dictionary<string, bool>();
        private readonly Dictionary<bool, List<AltKeepRow>> byClass = new Dictionary<bool, List<AltKeepRow>> { [false] = new List<AltKeepRow>(), [true] = new List<AltKeepRow>() };

        public int Count { get; }

        public AltKeepTable(IEnumerable<AltKeepRow> rows) {
            if (rows == null) { return; }
            foreach (AltKeepRow row in rows) {
                if (row == null) { continue; }
                Count++;
                string f = MoonKeep.FilterKey(row.Filter);
                if (f == null) {
                    byClass[row.Narrowband].Add(row);
                    continue;
                }
                if (!byFilter.TryGetValue(f, out List<AltKeepRow> list)) {
                    list = new List<AltKeepRow>();
                    byFilter[f] = list;
                    filterClass[f] = row.Narrowband;
                }
                list.Add(row);
            }
        }

        public AltKeepRow Lookup(string filter, double altitude) {
            if (Count == 0) { return null; }
            string f = MoonKeep.FilterKey(filter);
            if (f != null && byFilter.TryGetValue(f, out List<AltKeepRow> own)) {
                AltKeepRow r = own.FirstOrDefault(x => x.Usable && x.Contains(altitude));
                if (r != null) { return r; }
            }
            bool narrowband = f != null && filterClass.TryGetValue(f, out bool nb) ? nb : FilterKind.IsNarrowband(filter);
            return byClass[narrowband].FirstOrDefault(x => x.Usable && x.Contains(altitude));
        }
    }

    public static class AltKeep {
        public const double AltMax = 90;
        public const string FilterReason = "low in the sky";
        public const string TargetReason = "all exposure plans rejected low in the sky";

        public static bool Waits(IEnumerable<string> exposureReasons, params string[] alongside) {
            List<string> list = exposureReasons?.ToList() ?? new List<string>();
            return list.Count > 0 && list.Contains(FilterReason)
                && list.All(r => r == FilterReason || (alongside != null && alongside.Contains(r)));
        }

        public static bool Applies(bool managed, bool inEventWindow) {
            return managed && !inEventWindow;
        }

        public static double? Worth(AltKeepRow row) {
            if (row == null || double.IsNaN(row.Worth)) { return null; }
            return GoalCompletion.Clamp01(row.Worth);
        }

        public static double Weight(double? worth) {
            return worth.HasValue && !double.IsNaN(worth.Value) ? GoalCompletion.Clamp01(worth.Value) : 1;
        }

        public static bool Skips(AltKeepRow row) {
            return row != null && row.Usable && row.Skips;
        }

        public static string Explain(AltKeepRow row, double altitude) {
            if (row == null) { return null; }
            string what = string.IsNullOrWhiteSpace(row.Filter) ? (row.Narrowband ? "narrowband" : "broadband") : row.Filter.Trim();
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "target {0:0}° up: past {1} subs at {2:0}-{3:0}° with the Moon down kept {4} of {5} (under {6:0.#}%)",
                altitude, what, row.AltFrom, row.AltTo, row.Kept, row.Subs, Math.Ceiling(row.RateHigh * 1000) / 10);
        }
    }
}
