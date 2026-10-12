using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class TemplateRoles {
        public const string Managed = "managed";
        public const string Retired = "retired";
    }

    public class TemplateRoleRow {
        public int TemplateId { get; set; }
        public string TemplateGuid { get; set; }
        public string ProfileId { get; set; }
        public string Filter { get; set; }
        public string Role { get; set; }
        public DateTime? ChangedAt { get; set; }
    }

    public class ManagedTemplateTable {
        public static readonly ManagedTemplateTable Empty = new ManagedTemplateTable(null);

        private static volatile ManagedTemplateTable current = Empty;

        public static ManagedTemplateTable Current {
            get => current;
            set => current = value ?? Empty;
        }

        private readonly Dictionary<int, TemplateRoleRow> rows = new Dictionary<int, TemplateRoleRow>();

        public ManagedTemplateTable(IEnumerable<TemplateRoleRow> rows) {
            if (rows == null) { return; }
            foreach (TemplateRoleRow row in rows) {
                if (row == null || row.TemplateId <= 0) { continue; }
                this.rows[row.TemplateId] = row;
            }
        }

        public int Count => rows.Count;

        public int ManagedCount => rows.Values.Count(r => IsRole(r, TemplateRoles.Managed));

        public bool IsManaged(int templateId) {
            return rows.TryGetValue(templateId, out TemplateRoleRow row) && IsRole(row, TemplateRoles.Managed);
        }

        public bool IsRetired(int templateId) {
            return rows.TryGetValue(templateId, out TemplateRoleRow row) && IsRole(row, TemplateRoles.Retired);
        }

        public bool HasManaged(string profileId) {
            return rows.Values.Any(r => IsRole(r, TemplateRoles.Managed) && string.Equals(r.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRole(TemplateRoleRow row, string role) {
            return string.Equals(row.Role?.Trim(), role, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class ManagedTemplateSpec {
        public const string Suffix = " (auto)";
        public const int BroadbandGain = 0;
        public const int NarrowbandGain = 100;
        public const int Offset = -1;
        public const int Bin = 1;
        public const int ReadoutMode = -1;
        public const int DitherEvery = -1;
        public const double MaximumHumidity = 0;
        public const int MinutesOffset = 0;
        public const double BroadbandDefaultExposure = 300;
        public const double NarrowbandDefaultExposure = 600;
        public const int TwilightLevel = 1;

        public static string Name(string filter) {
            return (filter ?? "").Trim() + Suffix;
        }

        public static bool Narrowband(string filter) {
            return FilterKind.IsNarrowband(filter);
        }

        public static int Gain(string filter) {
            return Narrowband(filter) ? NarrowbandGain : BroadbandGain;
        }

        public static double DefaultExposure(string filter) {
            return Narrowband(filter) ? NarrowbandDefaultExposure : BroadbandDefaultExposure;
        }
    }

    public static class TwilightRule {
        public const double BroadbandSunLimit = -15;

        public static bool Rejects(bool managed, string filter, bool nighttime, double sunAtStart, double sunAtEnd) {
            if (!managed || nighttime || FilterKind.IsNarrowband(filter)) { return false; }
            return sunAtStart > BroadbandSunLimit || sunAtEnd > BroadbandSunLimit;
        }

        public static bool Rejects(bool managed, string filter, bool nighttime, Func<DateTime, double> sunAltitude, DateTime atTime, double exposureSeconds) {
            if (!managed || nighttime || FilterKind.IsNarrowband(filter) || sunAltitude == null) { return false; }
            return Rejects(true, filter, false, sunAltitude(atTime), sunAltitude(atTime.AddSeconds(Math.Max(0, exposureSeconds))));
        }
    }

    public static class MoonFloor {
        public const double Degrees = 20;

        public static bool Rejects(bool managed, bool ignoreMoon, double moonAltitude, double separation) {
            if (!managed || ignoreMoon) { return false; }
            return moonAltitude > 0 && separation < Degrees;
        }

        public static DateTime MidExposure(DateTime atTime, double exposureSeconds) {
            return atTime.AddSeconds(Math.Max(0, exposureSeconds) / 2);
        }
    }
}
