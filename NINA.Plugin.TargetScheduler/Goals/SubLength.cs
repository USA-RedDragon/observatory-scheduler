using System;
using System.Collections.Generic;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class SubLengthRow {
        public string TargetGuid { get; set; }
        public string Filter { get; set; }
        public int Gain { get; set; }
        public string Bucket { get; set; }
        public double Exposure { get; set; }
        public double? Ideal { get; set; }
        public double? Current { get; set; }
        public double? SkyRate { get; set; }
        public double? SnrChange { get; set; }
        public string Basis { get; set; }
        public DateTime? ComputedAt { get; set; }
    }

    public class SubLengthChoice {
        public double Seconds { get; set; }
        public string Source { get; set; }
        public string Bucket { get; set; }
        public double? Glare { get; set; }
    }

    public class SubLengthTable {
        public static readonly SubLengthTable Empty = new SubLengthTable(null);

        private static volatile SubLengthTable current = Empty;

        public static SubLengthTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, SubLengthRow> rows = new Dictionary<string, SubLengthRow>(StringComparer.Ordinal);

        public SubLengthTable(IEnumerable<SubLengthRow> rows) {
            if (rows == null) { return; }
            foreach (SubLengthRow row in rows) {
                if (row == null || string.IsNullOrEmpty(row.TargetGuid) || row.Filter == null || row.Bucket == null) { continue; }
                this.rows[Key(row.TargetGuid, row.Filter, row.Gain, row.Bucket)] = row;
            }
        }

        public int Count => rows.Count;

        public SubLengthRow Get(string targetGuid, string filter, int gain, string bucket) {
            if (string.IsNullOrEmpty(targetGuid) || filter == null || bucket == null) { return null; }
            return rows.TryGetValue(Key(targetGuid, filter, gain, bucket), out SubLengthRow row) ? row : null;
        }

        private static string Key(string targetGuid, string filter, int gain, string bucket) {
            return string.Create(CultureInfo.InvariantCulture, $"{targetGuid.Trim().ToLowerInvariant()}\u001f{filter}\u001f{(gain < 0 ? -1 : gain)}\u001f{bucket.Trim().ToLowerInvariant()}");
        }
    }

    public static class SubLength {
        public const string Dark = "dark";
        public const string Low = "low";
        public const string Mid = "mid";
        public const string Bright = "bright";

        public const string SourceSystem = "system";
        public const string SourcePlan = "plan";
        public const string SourceTemplate = "template";

        public const double DarkBelow = 0.05;
        public const double LowBelow = 0.25;
        public const double MidBelow = 0.5;

        public static string Bucket(double glare) {
            if (double.IsNaN(glare)) { return null; }
            if (glare < DarkBelow) { return Dark; }
            if (glare < LowBelow) { return Low; }
            if (glare < MidBelow) { return Mid; }
            return Bright;
        }

        public static SubLengthChoice Choose(SubLengthTable table, string targetGuid, string filter, int gain, double? glare, double planExposure, double templateExposure) {
            return Choose(table, targetGuid, filter, gain, glare, planExposure, templateExposure, false);
        }

        public static SubLengthChoice Choose(SubLengthTable table, string targetGuid, string filter, int gain, double? glare, double planExposure, double templateExposure, bool planFirst) {
            double? g = glare.HasValue && !double.IsNaN(glare.Value) && !double.IsInfinity(glare.Value) ? glare : null;
            string bucket = g.HasValue ? Bucket(g.Value) : null;
            SubLengthRow row = bucket == null || (planFirst && planExposure > 0) ? null : table?.Get(targetGuid, filter, gain, bucket);
            if (row != null && row.Exposure > 0 && !double.IsNaN(row.Exposure) && !double.IsInfinity(row.Exposure)) {
                return new SubLengthChoice { Seconds = row.Exposure, Source = SourceSystem, Bucket = bucket, Glare = g };
            }

            if (planExposure > 0) {
                return new SubLengthChoice { Seconds = planExposure, Source = SourcePlan, Bucket = bucket, Glare = g };
            }

            return new SubLengthChoice { Seconds = templateExposure, Source = SourceTemplate, Bucket = bucket, Glare = g };
        }
    }
}
