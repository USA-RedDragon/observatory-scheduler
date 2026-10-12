using NINA.Plugin.TargetScheduler.Goals;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public class BodyNightRow {
        public string TargetGuid { get; set; }
        public string Night { get; set; }
        public DateTime At { get; set; }
        public string Designation { get; set; }
        public double Ra { get; set; }
        public double Dec { get; set; }
        public double RaRate { get; set; }
        public double DecRate { get; set; }
        public double? Mag { get; set; }
        public double? MagLimit { get; set; }
        public DateTime? ComputedAt { get; set; }

        public bool Bright => !MagLimit.HasValue || (Mag.HasValue && Mag.Value <= MagLimit.Value);
    }

    public class BodyNightTrack {
        public static readonly BodyNightTrack Empty = new BodyNightTrack(null);

        private readonly Dictionary<string, BodyNightRow> nights = new Dictionary<string, BodyNightRow>(StringComparer.Ordinal);
        private readonly BodyEphemeris ephemeris;

        public BodyNightTrack(IEnumerable<BodyNightRow> rows) {
            List<BodyNightRow> list = (rows ?? Enumerable.Empty<BodyNightRow>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Night)).ToList();
            foreach (BodyNightRow row in list) {
                nights[row.Night.Trim()] = row;
            }
            ephemeris = new BodyEphemeris(list.Select(r => new BodyPositionRow {
                TargetGuid = r.TargetGuid,
                At = r.At,
                Designation = r.Designation,
                Night = r.Night,
                Ra = r.Ra,
                Dec = r.Dec,
                RaRate = r.RaRate,
                DecRate = r.DecRate,
                Mag = r.Mag,
                ComputedAt = r.ComputedAt
            }));
            Version = list.Count == 0 ? "empty"
                : string.Join(":", ephemeris.Version, list.Count(r => r.Bright).ToString(CultureInfo.InvariantCulture), list.Max(r => r.ComputedAt?.Ticks ?? 0).ToString(CultureInfo.InvariantCulture));
        }

        public int Count => nights.Count;

        public string Version { get; }

        public bool HasNight(DateTime nightDate) {
            return nights.ContainsKey(NightKey(nightDate));
        }

        public bool Bright(DateTime nightDate) {
            return nights.TryGetValue(NightKey(nightDate), out BodyNightRow row) && row.Bright;
        }

        public BodyPosition? At(DateTime time) {
            return ephemeris.At(time);
        }

        public static bool IsEvent(string designation) {
            return designation != null && designation.Trim().StartsWith("event:", StringComparison.OrdinalIgnoreCase);
        }

        public static string NightKey(DateTime nightDate) {
            return nightDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    public class BodyNightTable {
        public static readonly BodyNightTable Empty = new BodyNightTable(null);

        private readonly Dictionary<string, List<BodyNightRow>> rows = new Dictionary<string, List<BodyNightRow>>(StringComparer.Ordinal);

        public BodyNightTable(IEnumerable<BodyNightRow> rows) {
            if (rows == null) { return; }
            foreach (BodyNightRow row in rows) {
                if (!Valid(row)) { continue; }
                string key = TargetBodyTable.Key(row.TargetGuid);
                if (!this.rows.TryGetValue(key, out List<BodyNightRow> list)) {
                    list = new List<BodyNightRow>();
                    this.rows[key] = list;
                }
                list.Add(row);
            }
            Count = this.rows.Values.Sum(l => l.Count);
        }

        public int Count { get; }

        public BodyNightTrack Track(string targetGuid, string designation) {
            if (string.IsNullOrWhiteSpace(targetGuid) || string.IsNullOrWhiteSpace(designation)) { return BodyNightTrack.Empty; }
            if (!rows.TryGetValue(TargetBodyTable.Key(targetGuid), out List<BodyNightRow> list)) { return BodyNightTrack.Empty; }
            return new BodyNightTrack(list.Where(r => TargetBodyTable.SameDesignation(r.Designation, designation)));
        }

        private static bool Valid(BodyNightRow row) {
            return row != null && !string.IsNullOrWhiteSpace(row.TargetGuid) && !string.IsNullOrWhiteSpace(row.Designation) && !string.IsNullOrWhiteSpace(row.Night)
                && Finite(row.Ra) && Finite(row.Dec) && Finite(row.RaRate) && Finite(row.DecRate) && row.Dec >= -90 && row.Dec <= 90;
        }

        private static bool Finite(double v) {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }

    public class BodyNightUsability : INightUsability {
        private readonly INightUsability sky;
        private readonly BodyNightTrack track;
        private readonly DateTime firstNight;

        public BodyNightUsability(INightUsability sky, BodyNightTrack track, DateTime firstNight) {
            this.sky = sky;
            this.track = track ?? BodyNightTrack.Empty;
            this.firstNight = firstNight.Date;
        }

        public double UsableMinutes(int night) {
            return track.Bright(firstNight.AddDays(night)) ? sky.UsableMinutes(night) : 0;
        }
    }
}
