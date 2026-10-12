using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public class TargetBodyRow {
        public string TargetGuid { get; set; }
        public string Designation { get; set; }
        public string Kind { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class BodyPositionRow {
        public string TargetGuid { get; set; }
        public DateTime At { get; set; }
        public string Designation { get; set; }
        public string Night { get; set; }
        public double Ra { get; set; }
        public double Dec { get; set; }
        public double? RaApp { get; set; }
        public double? DecApp { get; set; }
        public double RaRate { get; set; }
        public double DecRate { get; set; }
        public double? Mag { get; set; }
        public double? Delta { get; set; }
        public double? UncRa { get; set; }
        public double? UncDec { get; set; }
        public DateTime? ComputedAt { get; set; }
    }

    public class TargetBodyTable {
        public static readonly TargetBodyTable Empty = new TargetBodyTable(null);

        private static volatile TargetBodyTable current = Empty;

        public static TargetBodyTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, TargetBodyRow> rows = new Dictionary<string, TargetBodyRow>(StringComparer.Ordinal);

        public TargetBodyTable(IEnumerable<TargetBodyRow> rows) {
            if (rows == null) { return; }
            foreach (TargetBodyRow row in rows) {
                if (row == null || string.IsNullOrWhiteSpace(row.TargetGuid) || string.IsNullOrWhiteSpace(row.Designation)) { continue; }
                this.rows[Key(row.TargetGuid)] = row;
            }
        }

        public int Count => rows.Count;

        public TargetBodyRow Get(string targetGuid) {
            if (string.IsNullOrWhiteSpace(targetGuid)) { return null; }
            return rows.TryGetValue(Key(targetGuid), out TargetBodyRow row) ? row : null;
        }

        public string Designation(string targetGuid) {
            return Get(targetGuid)?.Designation?.Trim();
        }

        public static bool SameDesignation(string a, string b) {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) { return false; }
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static string Key(string targetGuid) {
            return targetGuid.Trim().ToLowerInvariant();
        }
    }

    public class BodyPositionTable {
        public static readonly BodyPositionTable Empty = new BodyPositionTable(null);

        private static volatile BodyPositionTable current = Empty;

        public static BodyPositionTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<string, List<BodyPositionRow>> rows = new Dictionary<string, List<BodyPositionRow>>(StringComparer.Ordinal);

        public BodyPositionTable(IEnumerable<BodyPositionRow> rows) {
            if (rows == null) { return; }
            foreach (BodyPositionRow row in rows) {
                if (!Valid(row)) { continue; }
                string key = TargetBodyTable.Key(row.TargetGuid);
                if (!this.rows.TryGetValue(key, out List<BodyPositionRow> list)) {
                    list = new List<BodyPositionRow>();
                    this.rows[key] = list;
                }
                list.Add(row);
            }
            foreach (List<BodyPositionRow> list in this.rows.Values) {
                list.Sort((a, b) => a.At.CompareTo(b.At));
            }
            Count = this.rows.Values.Sum(l => l.Count);
        }

        public int Count { get; }

        public IReadOnlyList<BodyPositionRow> Rows(string targetGuid, string designation) {
            if (string.IsNullOrWhiteSpace(targetGuid) || string.IsNullOrWhiteSpace(designation)) { return Array.Empty<BodyPositionRow>(); }
            if (!rows.TryGetValue(TargetBodyTable.Key(targetGuid), out List<BodyPositionRow> list)) { return Array.Empty<BodyPositionRow>(); }
            return list.Where(r => TargetBodyTable.SameDesignation(r.Designation, designation)).ToList();
        }

        private static bool Valid(BodyPositionRow row) {
            return row != null && !string.IsNullOrWhiteSpace(row.TargetGuid) && !string.IsNullOrWhiteSpace(row.Designation)
                && Finite(row.Ra) && Finite(row.Dec) && Finite(row.RaRate) && Finite(row.DecRate) && row.Dec >= -90 && row.Dec <= 90;
        }

        private static bool Finite(double v) {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
