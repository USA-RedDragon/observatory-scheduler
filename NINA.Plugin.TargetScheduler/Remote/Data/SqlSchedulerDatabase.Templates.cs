using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public partial class SqlSchedulerDatabase {

        private static string Stamp(DateTime now) {
            return now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static void EnsureTemplateRoles(Session s) {
            s.Exec(TemplateRoleStore.CreateRoleTable);
            s.Exec(TemplateRoleStore.CreateMigrationTable);
        }

        private static bool IsManagedTemplate(Session s, long templateId) {
            return s.TableExists(TemplateRoleStore.RoleTable)
                && s.Count("SELECT COUNT(*) FROM ts_template_role WHERE template_id = @id AND role = @r", ("@id", templateId), ("@r", TemplateRoles.Managed)) > 0;
        }

        private static bool IsManagedTemplate(DbConnection conn, DbTransaction tx, long templateId) {
            using var exists = conn.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n";
            SqlCommandStore.Add(exists, "@n", TemplateRoleStore.RoleTable);
            if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) {
                return false;
            }
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM ts_template_role WHERE template_id = @id AND role = @r";
            SqlCommandStore.Add(cmd, "@id", templateId);
            SqlCommandStore.Add(cmd, "@r", TemplateRoles.Managed);
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        public static string ManagedReason(string action) {
            return $"it is managed by the scheduler, so it cannot be {action}";
        }

        private static List<string> KnownFilters(Session s, string profile, string activeProfile, IReadOnlyCollection<string> wheelFilters) {
            var known = new List<string>();
            if (wheelFilters != null && !string.IsNullOrEmpty(activeProfile) && string.Equals(activeProfile, profile, StringComparison.OrdinalIgnoreCase)) {
                known.AddRange(wheelFilters.Where(f => !string.IsNullOrWhiteSpace(f)));
            }
            foreach (var row in s.Rows("SELECT DISTINCT filtername FROM exposuretemplate WHERE profileId = @p AND filtername IS NOT NULL AND trim(filtername) <> '' ORDER BY Id", ("@p", profile))) {
                known.Add(Str(row["filtername"]));
            }
            return known.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string ResolveFilter(Session s, string profile, string filter, string activeProfile, IReadOnlyCollection<string> wheelFilters, out string error) {
            error = null;
            var wanted = filter?.Trim();
            var known = KnownFilters(s, profile, activeProfile, wheelFilters);
            var match = known.FirstOrDefault(f => f == filter) ?? known.FirstOrDefault(f => f.Trim() == wanted)
                ?? known.FirstOrDefault(f => string.Equals(f.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (match == null) {
                error = known.Count == 0
                    ? $"{wanted} is not a filter of this profile, and the profile has no known filters."
                    : $"{wanted} is not a filter of this profile. Known filters: {string.Join(", ", known.Select(f => f.Trim()).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))}.";
            }
            return match;
        }

        private static long? FindManagedTemplate(Session s, string profile, string filter) {
            if (!s.TableExists(TemplateRoleStore.RoleTable)) {
                return null;
            }
            return Long(s.Scalar("SELECT r.template_id FROM ts_template_role r JOIN exposuretemplate t ON t.Id = r.template_id " +
                "WHERE r.role = @r AND r.profile_id = @p AND r.filter = @f COLLATE NOCASE ORDER BY r.filter = @f DESC, r.template_id LIMIT 1",
                ("@r", TemplateRoles.Managed), ("@p", profile), ("@f", filter)));
        }

        private static long CreateManagedTemplate(Session s, string profile, string filter, string stamp) {
            EnsureTemplateRoles(s);
            var guid = Guid.NewGuid().ToString();
            var id = s.Insert("INSERT INTO exposuretemplate (profileId, name, filtername, gain, \"offset\", bin, readoutmode, twilightlevel, moonavoidanceenabled, moonavoidanceseparation, moonavoidancewidth, " +
                "maximumhumidity, defaultexposure, moonrelaxscale, moonrelaxmaxaltitude, moonrelaxminaltitude, moondownenabled, ditherevery, minutesOffset, guid) " +
                "VALUES (@p, @n, @f, @gain, @offset, @bin, @readout, @twilight, 0, 0, 0, @humidity, @exposure, 0, 5, -15, 0, @dither, @minutes, @g)",
                ("@p", profile), ("@n", ManagedTemplateSpec.Name(filter)), ("@f", filter), ("@gain", ManagedTemplateSpec.Gain(filter)),
                ("@offset", ManagedTemplateSpec.Offset), ("@bin", ManagedTemplateSpec.Bin), ("@readout", ManagedTemplateSpec.ReadoutMode),
                ("@twilight", ManagedTemplateSpec.TwilightLevel), ("@humidity", ManagedTemplateSpec.MaximumHumidity),
                ("@exposure", ManagedTemplateSpec.DefaultExposure(filter)), ("@dither", ManagedTemplateSpec.DitherEvery),
                ("@minutes", ManagedTemplateSpec.MinutesOffset), ("@g", guid));
            s.Exec("INSERT OR REPLACE INTO ts_template_role (template_id, template_guid, profile_id, filter, role, changed_at) VALUES (@id, @g, @p, @f, @r, @at)",
                ("@id", id), ("@g", guid), ("@p", profile), ("@f", filter), ("@r", TemplateRoles.Managed), ("@at", stamp));
            return id;
        }

        private static long ManagedTemplate(Session s, string profile, string filter, string stamp, List<string> created) {
            var id = FindManagedTemplate(s, profile, filter);
            if (id.HasValue) {
                return id.Value;
            }
            created?.Add(ManagedTemplateSpec.Name(filter));
            return CreateManagedTemplate(s, profile, filter, stamp);
        }

        private static string CreatedText(List<string> created) {
            return created.Count == 0 ? null : $"Created managed {(created.Count == 1 ? "template" : "templates")} {string.Join(", ", created)}.";
        }
    }
}
