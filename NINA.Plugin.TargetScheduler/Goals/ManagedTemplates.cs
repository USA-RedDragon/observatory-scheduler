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
}
