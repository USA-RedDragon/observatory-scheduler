using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public partial class SqlSchedulerDatabase {

        public Action<Func<string, int>> AfterMigrationRead { get; set; }

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

        private static bool NeededForRevert(Session s, long templateId) {
            return s.TableExists(TemplateRoleStore.MigrationTable)
                && s.Count("SELECT COUNT(*) FROM ts_template_migration WHERE old_template_id = @id AND reverted_at IS NULL", ("@id", templateId)) > 0;
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

        private sealed class TemplateInfo {
            public long Id;
            public string Profile;
            public string Name;
            public string Filter;
            public string Guid;
        }

        private sealed class PlanInfo {
            public long Id;
            public string Guid;
            public long? TargetId;
            public string Profile;
            public long? TemplateId;
        }

        public ChangeOutcome MigrateTemplates(bool dryRun, DateTime now) {
            var o = new ChangeOutcome();
            using var s = Open();
            var stamp = Stamp(now);
            var hasRoles = s.TableExists(TemplateRoleStore.RoleTable);
            var templates = s.Rows("SELECT Id, profileId, name, filtername, guid FROM exposuretemplate").Select(r => new TemplateInfo {
                Id = Long(r["Id"]).Value, Profile = Str(r["profileId"]), Name = Str(r["name"]), Filter = Str(r["filtername"]), Guid = Str(r["guid"])
            }).ToDictionary(t => t.Id);
            var roles = new Dictionary<long, string>();
            if (hasRoles) {
                foreach (var r in s.Rows("SELECT template_id, role FROM ts_template_role")) {
                    roles[Long(r["template_id"]).Value] = Str(r["role"]);
                }
            }
            var plans = s.Rows("SELECT Id, guid, targetid, profileId, exposureTemplateId FROM exposureplan ORDER BY Id").Select(r => new PlanInfo {
                Id = Long(r["Id"]).Value, Guid = Str(r["guid"]), TargetId = Long(r["targetid"]), Profile = Str(r["profileId"]), TemplateId = Long(r["exposureTemplateId"])
            }).ToList();
            var targetNames = new Dictionary<long, (string name, int? project)>();
            foreach (var r in s.Rows("SELECT Id, name, projectid FROM target")) {
                targetNames[Long(r["Id"]).Value] = (Str(r["name"]), (int?)Long(r["projectid"]));
            }
            AfterMigrationRead?.Invoke(sql => s.Exec(sql));

            bool IsRole(long id, string role) => roles.TryGetValue(id, out var r) && string.Equals(r?.Trim(), role, StringComparison.OrdinalIgnoreCase);
            bool IsManaged(long id) => IsRole(id, TemplateRoles.Managed);
            (string, string) Key(string profile, string filter) => (profile, filter.ToLowerInvariant());
            var managed = new Dictionary<(string, string), long>();
            foreach (var t in templates.Values.Where(t => IsManaged(t.Id) && !string.IsNullOrWhiteSpace(t.Filter)).OrderBy(t => t.Id)) {
                var key = Key(t.Profile, t.Filter);
                if (!managed.ContainsKey(key)) {
                    managed[key] = t.Id;
                }
            }

            var profiles = new HashSet<string>(StringComparer.Ordinal);
            var created = new List<Dictionary<string, object>>();
            var skipped = new List<Dictionary<string, object>>();
            var perTemplate = new Dictionary<long, int>();
            var remapped = 0;
            var alreadyManaged = 0;
            var newIds = new Dictionary<long, long?>();
            if (!dryRun) {
                EnsureTemplateRoles(s);
            }
            foreach (var pl in plans) {
                if (!pl.TemplateId.HasValue || !templates.TryGetValue(pl.TemplateId.Value, out var tmpl)) {
                    skipped.Add(new Dictionary<string, object> { ["plan_id"] = pl.Id, ["target_id"] = pl.TargetId, ["template_id"] = pl.TemplateId, ["reason"] = "template missing" });
                    continue;
                }
                if (string.IsNullOrWhiteSpace(tmpl.Filter)) {
                    skipped.Add(new Dictionary<string, object> { ["plan_id"] = pl.Id, ["target_id"] = pl.TargetId, ["template_id"] = pl.TemplateId, ["reason"] = "template has no filter" });
                    continue;
                }
                if (IsManaged(tmpl.Id)) {
                    alreadyManaged++;
                    newIds[pl.Id] = tmpl.Id;
                    continue;
                }
                var profile = string.IsNullOrEmpty(pl.Profile) ? tmpl.Profile : pl.Profile;
                profiles.Add(profile ?? "");
                var key = Key(profile, tmpl.Filter);
                if (!managed.TryGetValue(key, out var target)) {
                    target = dryRun ? 0 : CreateManagedTemplate(s, profile, tmpl.Filter, stamp);
                    managed[key] = target;
                    created.Add(new Dictionary<string, object> {
                        ["profile_id"] = profile, ["filter"] = tmpl.Filter, ["name"] = ManagedTemplateSpec.Name(tmpl.Filter),
                        ["template_id"] = dryRun ? null : (object)target, ["gain"] = ManagedTemplateSpec.Gain(tmpl.Filter),
                        ["narrowband"] = ManagedTemplateSpec.Narrowband(tmpl.Filter)
                    });
                }
                if (!dryRun) {
                    var n = s.Exec("UPDATE exposureplan SET exposureTemplateId = @new WHERE Id = @id AND exposureTemplateId = @old", ("@new", target), ("@id", pl.Id), ("@old", tmpl.Id));
                    if (n == 0) {
                        skipped.Add(new Dictionary<string, object> { ["plan_id"] = pl.Id, ["target_id"] = pl.TargetId, ["template_id"] = tmpl.Id, ["reason"] = "plan changed during the migration" });
                        continue;
                    }
                    s.Exec("INSERT INTO ts_template_migration (plan_id, plan_guid, target_id, profile_id, filter, old_template_id, old_template_guid, old_template_name, new_template_id, migrated_at, reverted_at) " +
                        "VALUES (@id, @pg, @t, @p, @f, @old, @og, @on, @new, @at, NULL) ON CONFLICT(plan_id, migrated_at) DO UPDATE SET plan_guid = excluded.plan_guid, target_id = excluded.target_id, " +
                        "profile_id = excluded.profile_id, filter = excluded.filter, old_template_id = excluded.old_template_id, old_template_guid = excluded.old_template_guid, " +
                        "old_template_name = excluded.old_template_name, new_template_id = excluded.new_template_id, reverted_at = NULL",
                        ("@id", pl.Id), ("@pg", Db(pl.Guid)), ("@t", Db(pl.TargetId)), ("@p", profile), ("@f", tmpl.Filter), ("@old", tmpl.Id),
                        ("@og", Db(tmpl.Guid)), ("@on", Db(tmpl.Name)), ("@new", target), ("@at", stamp));
                    if (pl.TargetId.HasValue && targetNames.TryGetValue(pl.TargetId.Value, out var tn)) {
                        o.Touch(tn.project, (int)pl.TargetId.Value);
                    }
                }
                newIds[pl.Id] = target;
                perTemplate[tmpl.Id] = perTemplate.TryGetValue(tmpl.Id, out var c) ? c + 1 : 1;
                remapped++;
            }

            var duplicates = new List<Dictionary<string, object>>();
            foreach (var g in plans.Where(pl => pl.TargetId.HasValue && newIds.ContainsKey(pl.Id))
                .GroupBy(pl => (pl.TargetId.Value, templates[pl.TemplateId.Value].Filter.ToLowerInvariant()))
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2, StringComparer.Ordinal)) {
                duplicates.Add(new Dictionary<string, object> {
                    ["target_id"] = g.Key.Item1,
                    ["target_name"] = targetNames.TryGetValue(g.Key.Item1, out var tn) ? tn.name : null,
                    ["filter"] = templates[g.First().TemplateId.Value].Filter,
                    ["plan_ids"] = g.Select(pl => pl.Id).ToList(),
                    ["old_templates"] = g.Select(pl => templates[pl.TemplateId.Value].Name).ToList(),
                });
            }

            profiles.UnionWith(plans.Select(pl => pl.Profile).Where(p => !string.IsNullOrEmpty(p)));
            var retired = 0;
            var alreadyRetired = 0;
            foreach (var t in templates.Values.Where(t => profiles.Contains(t.Profile ?? "") && !IsManaged(t.Id)).OrderBy(t => t.Id)) {
                if (IsRole(t.Id, TemplateRoles.Retired)) {
                    alreadyRetired++;
                    continue;
                }
                if (!dryRun) {
                    s.Exec("INSERT OR REPLACE INTO ts_template_role (template_id, template_guid, profile_id, filter, role, changed_at) VALUES (@id, @g, @p, @f, @r, @at)",
                        ("@id", t.Id), ("@g", Db(t.Guid)), ("@p", t.Profile), ("@f", t.Filter ?? ""), ("@r", TemplateRoles.Retired), ("@at", stamp));
                }
                retired++;
            }

            var changed = remapped > 0 || created.Count > 0 || retired > 0;
            if (!dryRun && changed) {
                s.Commit();
                o.Changed = true;
            }
            var verb = dryRun ? "Dry run, nothing was written: would remap" : "Remapped";
            o.Message = changed || dryRun
                ? $"{verb} {remapped} {(remapped == 1 ? "plan" : "plans")}, {(dryRun ? "create" : "created")} {created.Count} managed {(created.Count == 1 ? "template" : "templates")} and {(dryRun ? "retire" : "retired")} {retired} {(retired == 1 ? "template" : "templates")}. " +
                  $"{duplicates.Count} same-filter duplicate {(duplicates.Count == 1 ? "group stays" : "groups stay")} as they are; {skipped.Count} {(skipped.Count == 1 ? "plan was" : "plans were")} skipped."
                : $"Nothing to migrate: {alreadyManaged} {(alreadyManaged == 1 ? "plan is" : "plans are")} already on managed templates; {skipped.Count} {(skipped.Count == 1 ? "plan was" : "plans were")} skipped.";
            o.Detail = new Dictionary<string, object> {
                ["dry_run"] = dryRun,
                ["plans_remapped"] = remapped,
                ["plans_already_managed"] = alreadyManaged,
                ["per_old_template"] = perTemplate.OrderBy(kv => kv.Key).Select(kv => new Dictionary<string, object> {
                    ["template_id"] = kv.Key, ["name"] = templates[kv.Key].Name, ["filter"] = templates[kv.Key].Filter, ["plans"] = kv.Value
                }).ToList(),
                ["managed_created"] = created,
                ["templates_retired"] = retired,
                ["templates_already_retired"] = alreadyRetired,
                ["duplicates"] = duplicates,
                ["skipped"] = skipped,
            };
            return o;
        }

        public ChangeOutcome RevertTemplates(bool dryRun, DateTime now) {
            var o = new ChangeOutcome();
            using var s = Open();
            if (!s.TableExists(TemplateRoleStore.MigrationTable)) {
                o.Message = "Nothing to revert: no template migration was recorded.";
                o.Detail = new Dictionary<string, object> { ["dry_run"] = dryRun, ["plans_reverted"] = 0, ["retired_roles_removed"] = 0, ["skipped"] = new List<object>() };
                return o;
            }
            var stamp = Stamp(now);
            var rows = s.Rows("SELECT plan_id, target_id, profile_id, filter, old_template_id, old_template_name, new_template_id, migrated_at FROM ts_template_migration WHERE reverted_at IS NULL ORDER BY plan_id, migrated_at DESC");
            var targetProjects = new Dictionary<long, int?>();
            foreach (var r in s.Rows("SELECT Id, projectid FROM target")) {
                targetProjects[Long(r["Id"]).Value] = (int?)Long(r["projectid"]);
            }
            var reverted = 0;
            var skipped = new List<Dictionary<string, object>>();
            var profiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows) {
                var planId = Long(r["plan_id"]).Value;
                var oldId = Long(r["old_template_id"]).Value;
                var newId = Long(r["new_template_id"]).Value;
                profiles.Add(Str(r["profile_id"]));
                var plan = s.Row("SELECT exposureTemplateId, targetid FROM exposureplan WHERE Id = @id", ("@id", planId));
                string reason = null;
                if (plan == null) {
                    reason = "plan no longer exists";
                } else if (Long(plan["exposureTemplateId"]) != newId) {
                    reason = "plan no longer uses the managed template";
                } else if (s.Count("SELECT COUNT(*) FROM exposuretemplate WHERE Id = @id", ("@id", oldId)) == 0) {
                    reason = "old template no longer exists";
                }
                if (reason == null && !dryRun) {
                    if (s.Exec("UPDATE exposureplan SET exposureTemplateId = @old WHERE Id = @id AND exposureTemplateId = @new", ("@old", oldId), ("@id", planId), ("@new", newId)) == 0) {
                        reason = "plan changed during the revert";
                    } else {
                        s.Exec("UPDATE ts_template_migration SET reverted_at = @at WHERE plan_id = @id AND migrated_at = @m AND reverted_at IS NULL", ("@at", stamp), ("@id", planId), ("@m", Str(r["migrated_at"])));
                        var targetId = Long(plan["targetid"]);
                        if (targetId.HasValue) {
                            o.Touch(targetProjects.TryGetValue(targetId.Value, out var project) ? project : null, (int)targetId.Value);
                        }
                    }
                }
                if (reason != null) {
                    skipped.Add(new Dictionary<string, object> { ["plan_id"] = planId, ["old_template_id"] = oldId, ["old_template_name"] = Str(r["old_template_name"]), ["reason"] = reason });
                    continue;
                }
                reverted++;
            }
            long unretired = 0;
            if (s.TableExists(TemplateRoleStore.RoleTable)) {
                foreach (var profile in profiles) {
                    unretired += dryRun
                        ? s.Count("SELECT COUNT(*) FROM ts_template_role WHERE role = @r AND profile_id = @p", ("@r", TemplateRoles.Retired), ("@p", profile))
                        : s.Exec("DELETE FROM ts_template_role WHERE role = @r AND profile_id = @p", ("@r", TemplateRoles.Retired), ("@p", profile));
                }
            }
            var changed = reverted > 0 || unretired > 0;
            if (!dryRun && changed) {
                s.Commit();
                o.Changed = true;
            }
            o.Message = dryRun
                ? $"Dry run, nothing was written: would revert {reverted} {(reverted == 1 ? "plan" : "plans")} and un-retire {unretired} {(unretired == 1 ? "template" : "templates")}; {skipped.Count} {(skipped.Count == 1 ? "plan" : "plans")} would be skipped."
                : changed
                    ? $"Reverted {reverted} {(reverted == 1 ? "plan" : "plans")} and un-retired {unretired} {(unretired == 1 ? "template" : "templates")}; {skipped.Count} {(skipped.Count == 1 ? "plan was" : "plans were")} skipped. Managed templates stay."
                    : $"Nothing to revert; {skipped.Count} {(skipped.Count == 1 ? "plan was" : "plans were")} skipped.";
            o.Detail = new Dictionary<string, object> {
                ["dry_run"] = dryRun,
                ["plans_reverted"] = reverted,
                ["retired_roles_removed"] = unretired,
                ["skipped"] = skipped,
            };
            return o;
        }
    }
}
