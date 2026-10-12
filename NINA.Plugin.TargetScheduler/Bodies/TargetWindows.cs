using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public class TargetWindowRow {
        public const string Normal = "normal";
        public const string High = "high";

        public string TargetGuid { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public string Priority { get; set; }
        public string EventId { get; set; }
        public bool IgnoreMoon { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsHigh => string.Equals(Priority?.Trim(), High, StringComparison.OrdinalIgnoreCase);

        public bool Contains(DateTime time) {
            DateTime utc = TargetWindowTable.Utc(time);
            return utc >= StartUtc && utc < EndUtc;
        }
    }

    public class TargetWindowTable {
        public static readonly TargetWindowTable Empty = new TargetWindowTable(null);

        private static volatile TargetWindowTable current = Empty;

        public static TargetWindowTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private static readonly Regex Iso = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly Dictionary<string, TargetWindowRow> rows = new Dictionary<string, TargetWindowRow>(StringComparer.Ordinal);

        public TargetWindowTable(IEnumerable<TargetWindowRow> rows) {
            if (rows == null) { return; }
            foreach (TargetWindowRow row in rows) {
                if (row == null || string.IsNullOrWhiteSpace(row.TargetGuid) || row.StartUtc >= row.EndUtc) { continue; }
                this.rows[TargetBodyTable.Key(row.TargetGuid)] = row;
            }
        }

        public int Count => rows.Count;

        public TargetWindowRow Get(string targetGuid) {
            if (rows.Count == 0 || string.IsNullOrWhiteSpace(targetGuid)) { return null; }
            return rows.TryGetValue(TargetBodyTable.Key(targetGuid), out TargetWindowRow row) ? row : null;
        }

        public static DateTime Utc(DateTime time) {
            return time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        }

        public static bool TryParseUtc(string text, out DateTime utc) {
            utc = default;
            if (string.IsNullOrWhiteSpace(text)) { return false; }
            string s = text.Trim();
            if (!Iso.IsMatch(s)) { return false; }
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)) { return false; }
            utc = parsed.UtcDateTime;
            return true;
        }

        public static string FormatUtc(DateTime utc) {
            return Utc(utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }
    }
}
