using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public partial class SqlSchedulerDatabase {
        private const double WeightTolerance = 1e-3;
        private const double GoalTolerance = 1e-6;
        private const double RegionTolerance = 1e-4;

        private sealed class Session : IDisposable {
            private readonly DbConnection conn;
            private readonly DbTransaction tx;

            public Session(DbConnection conn) {
                this.conn = conn;
                tx = conn.BeginTransaction();
            }

            private DbCommand Command(string sql, (string, object)[] args) {
                var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var (name, value) in args) {
                    SqlCommandStore.Add(cmd, name, value);
                }
                return cmd;
            }

            public object Scalar(string sql, params (string, object)[] args) {
                using var cmd = Command(sql, args);
                var v = cmd.ExecuteScalar();
                return v is DBNull ? null : v;
            }

            public long Count(string sql, params (string, object)[] args) {
                var v = Scalar(sql, args);
                return v == null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
            }

            public int Exec(string sql, params (string, object)[] args) {
                using var cmd = Command(sql, args);
                return cmd.ExecuteNonQuery();
            }

            public long Insert(string sql, params (string, object)[] args) {
                Exec(sql, args);
                return Convert.ToInt64(Scalar("SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
            }

            public List<Dictionary<string, object>> Rows(string sql, params (string, object)[] args) {
                using var cmd = Command(sql, args);
                using var r = cmd.ExecuteReader();
                var rows = new List<Dictionary<string, object>>();
                while (r.Read()) {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < r.FieldCount; i++) {
                        row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                    }
                    rows.Add(row);
                }
                return rows;
            }

            public Dictionary<string, object> Row(string sql, params (string, object)[] args) {
                return Rows(sql, args).FirstOrDefault();
            }

            public bool TableExists(string name) {
                return Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n", ("@n", name)) > 0;
            }

            public void Commit() {
                tx.Commit();
            }

            public void Dispose() {
                tx.Dispose();
                conn.Dispose();
            }
        }

        private Session Open() {
            return new Session(connect());
        }

        private static long? Long(object v) {
            return v == null ? null : Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        private static double? Double(object v) {
            return v == null ? null : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        private static string Str(object v) {
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static object Db(object v) {
            return v ?? DBNull.Value;
        }

        public ChangeOutcome ApplyGoalEdit(GoalEditPayload p, DateTime now) {
            var o = new ChangeOutcome();
            using var s = Open();
            if (!s.TableExists("ts_goal")) {
                return o.Reject("Goals are not set up on the observatory yet.");
            }
            var targets = new Dictionary<string, (int project, int target)>();
            foreach (var g in p.Goals) {
                var label = $"{g.TargetName ?? g.TargetGuid} {g.Filter} goal";
                var t = s.Row("SELECT Id, projectid FROM target WHERE guid = @g", ("@g", g.TargetGuid));
                if (t == null) {
                    o.Conflict(label, "target present", "target removed");
                    continue;
                }
                targets[g.TargetGuid] = ((int)Long(t["projectid"]).GetValueOrDefault(), (int)Long(t["Id"]).Value);
                var current = ReadGoal(s, g.TargetGuid, g.Filter);
                if (!GoalEquals(current, g.Before)) {
                    o.Conflict(label, GoalText(g.Before), GoalText(current));
                }
            }
            if (o.Conflicts.Count > 0) {
                return o;
            }
            var stamp = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            foreach (var g in p.Goals) {
                if (g.After == null) {
                    s.Exec("DELETE FROM ts_goal WHERE target_guid = @g AND filter = @f", ("@g", g.TargetGuid), ("@f", g.Filter));
                } else {
                    WriteGoal(s, g.TargetGuid, g.Filter, g.After, stamp);
                }
                var (project, target) = targets[g.TargetGuid];
                o.Touch(project, target);
            }
            s.Commit();
            o.Changed = true;
            return o;
        }

        private static GoalSetting ReadGoal(Session s, string targetGuid, string filter) {
            var r = s.Row("SELECT kind, snr_goal, depth_goal, plateau_stop, region FROM ts_goal WHERE target_guid = @g AND filter = @f", ("@g", targetGuid), ("@f", filter));
            if (r == null) {
                return null;
            }
            return new GoalSetting {
                Kind = (int)Long(r["kind"]).GetValueOrDefault(),
                SnrGoal = Double(r["snr_goal"]).GetValueOrDefault(),
                DepthGoal = Double(r["depth_goal"]),
                PlateauStop = Long(r["plateau_stop"]).GetValueOrDefault() != 0,
                Region = Str(r["region"]),
            };
        }

        private static void WriteGoal(Session s, string targetGuid, string filter, GoalSetting g, string stamp) {
            var args = new (string, object)[] {
                ("@g", targetGuid), ("@f", filter), ("@k", g.Kind), ("@snr", g.SnrGoal), ("@depth", Db(g.DepthGoal)),
                ("@plateau", g.PlateauStop ? 1 : 0), ("@region", Db(string.IsNullOrEmpty(g.Region) ? null : g.Region)), ("@at", stamp)
            };
            var n = s.Exec("UPDATE ts_goal SET kind = @k, snr_goal = @snr, depth_goal = @depth, plateau_stop = @plateau, region = @region, updated_at = @at WHERE target_guid = @g AND filter = @f", args);
            if (n == 0) {
                s.Exec("INSERT INTO ts_goal (target_guid, filter, kind, snr_goal, depth_goal, plateau_stop, region, updated_at) VALUES (@g, @f, @k, @snr, @depth, @plateau, @region, @at)", args);
            }
        }

        public static bool GoalEquals(GoalSetting a, GoalSetting b) {
            if (a == null || b == null) {
                return a == null && b == null;
            }
            bool depthSame = a.DepthGoal.HasValue == b.DepthGoal.HasValue && (!a.DepthGoal.HasValue || Math.Abs(a.DepthGoal.Value - b.DepthGoal.Value) <= GoalTolerance);
            return a.Kind == b.Kind
                && Math.Abs(a.SnrGoal - b.SnrGoal) <= GoalTolerance
                && depthSame
                && a.PlateauStop == b.PlateauStop
                && RegionEquals(a.Region, b.Region);
        }

        public static bool RegionEquals(string a, string b) {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) {
                return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
            }
            var pa = RegionPoints(a);
            var pb = RegionPoints(b);
            if (pa == null || pb == null) {
                return pa == null && pb == null && a.Trim() == b.Trim();
            }
            return pa.Count == pb.Count && pa.Zip(pb, (x, y) => Math.Abs(x.x - y.x) <= RegionTolerance && Math.Abs(x.y - y.y) <= RegionTolerance).All(ok => ok);
        }

        private static List<(double x, double y)> RegionPoints(string json) {
            try {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                    return null;
                }
                var points = new List<(double, double)>();
                foreach (var e in doc.RootElement.EnumerateArray()) {
                    if (e.ValueKind != JsonValueKind.Object) {
                        return null;
                    }
                    double? x = null, y = null;
                    foreach (var prop in e.EnumerateObject()) {
                        if (prop.Value.ValueKind != JsonValueKind.Number) {
                            continue;
                        }
                        if (string.Equals(prop.Name, "x", StringComparison.OrdinalIgnoreCase)) {
                            x = prop.Value.GetDouble();
                        } else if (string.Equals(prop.Name, "y", StringComparison.OrdinalIgnoreCase)) {
                            y = prop.Value.GetDouble();
                        }
                    }
                    if (!x.HasValue || !y.HasValue) {
                        return null;
                    }
                    points.Add((x.Value, y.Value));
                }
                return points;
            } catch (JsonException) {
                return null;
            }
        }

        public static string GoalText(GoalSetting g) {
            if (g == null) {
                return "not set";
            }
            var text = g.Kind == 1 && g.DepthGoal.HasValue
                ? $"{g.DepthGoal.Value.ToString(CultureInfo.InvariantCulture)} mag/arcsec²"
                : $"SNR {g.SnrGoal.ToString(CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrEmpty(g.Region)) {
                text += " with a region";
            }
            if (!g.PlateauStop) {
                text += ", no plateau stop";
            }
            return text;
        }

        public ChangeOutcome ApplyRuleWeightEdit(RuleWeightEditPayload p) {
            var o = new ChangeOutcome();
            using var s = Open();
            var project = s.Row("SELECT Id, guid FROM project WHERE Id = @id", ("@id", p.ProjectId));
            if (project == null) {
                return o.Reject($"{p.ProjectName ?? "Project " + p.ProjectId} no longer exists.");
            }
            var guid = Str(project["guid"]);
            if (!string.IsNullOrEmpty(p.ProjectGuid) && !string.IsNullOrEmpty(guid) && guid != p.ProjectGuid) {
                return o.Reject($"Project {p.ProjectId} is no longer {p.ProjectName}.");
            }
            foreach (var c in p.Changes) {
                var current = Double(s.Scalar("SELECT weight FROM ruleweight WHERE projectid = @p AND name = @n ORDER BY Id LIMIT 1", ("@p", p.ProjectId), ("@n", c.Rule)));
                bool same = current.HasValue == c.Before.HasValue && (!current.HasValue || Math.Abs(current.Value - c.Before.Value) <= WeightTolerance);
                if (!same) {
                    o.Conflict($"{c.Rule} weight", c.Before, current);
                }
            }
            if (o.Conflicts.Count > 0) {
                return o;
            }
            foreach (var c in p.Changes) {
                if (!c.After.HasValue) {
                    s.Exec("DELETE FROM ruleweight WHERE projectid = @p AND name = @n", ("@p", p.ProjectId), ("@n", c.Rule));
                } else if (s.Exec("UPDATE ruleweight SET weight = @w WHERE projectid = @p AND name = @n", ("@w", c.After.Value), ("@p", p.ProjectId), ("@n", c.Rule)) == 0) {
                    s.Exec("INSERT INTO ruleweight (name, weight, projectid) VALUES (@n, @w, @p)", ("@n", c.Rule), ("@w", c.After.Value), ("@p", p.ProjectId));
                }
            }
            s.Commit();
            o.Touch(p.ProjectId, null);
            o.Changed = true;
            return o;
        }

        public ChangeOutcome CloneTemplate(TemplateClonePayload p) {
            var o = new ChangeOutcome();
            using var s = Open();
            if (s.Count("SELECT COUNT(*) FROM exposuretemplate WHERE guid = @g", ("@g", p.Guid)) > 0) {
                o.Message = $"{p.Name} already exists.";
                return o;
            }
            var src = s.Row("SELECT * FROM exposuretemplate WHERE Id = @id", ("@id", p.SourceId));
            if (src == null) {
                return o.Reject($"{p.SourceName ?? "Template " + p.SourceId} no longer exists.");
            }
            if (s.Count("SELECT COUNT(*) FROM exposuretemplate WHERE profileId = @p AND name = @n", ("@p", src["profileId"]), ("@n", p.Name)) > 0) {
                return o.Reject($"A template called {p.Name} already exists.");
            }
            src.Remove("Id");
            src["guid"] = p.Guid;
            src["name"] = p.Name;
            if (p.DefaultExposure.HasValue) {
                src["defaultexposure"] = p.DefaultExposure.Value;
            }
            if (p.Gain.HasValue) {
                src["gain"] = p.Gain.Value;
            }
            var cols = src.Keys.ToList();
            var sql = $"INSERT INTO exposuretemplate ({string.Join(", ", cols.Select(Quote))}) VALUES ({string.Join(", ", cols.Select((c, i) => "@c" + i))})";
            s.Exec(sql, cols.Select((c, i) => ("@c" + i, Db(src[c]))).ToArray());
            s.Commit();
            o.Changed = true;
            return o;
        }

        public ChangeOutcome DeleteTemplate(TemplateDeletePayload p) {
            var o = new ChangeOutcome();
            using var s = Open();
            var id = Long(s.Scalar("SELECT Id FROM exposuretemplate WHERE guid = @g", ("@g", p.Guid)));
            if (!id.HasValue) {
                o.Message = $"{p.Name ?? "The template"} was already gone.";
                return o;
            }
            var used = s.Count("SELECT COUNT(*) FROM exposureplan WHERE exposureTemplateId = @id", ("@id", id.Value));
            if (used > 0) {
                return o.Reject($"{p.Name ?? "The template"} is used by {used} exposure {(used == 1 ? "plan" : "plans")}, so it was not removed.");
            }
            s.Exec("DELETE FROM exposuretemplate WHERE Id = @id", ("@id", id.Value));
            s.Commit();
            o.Changed = true;
            return o;
        }

        public ChangeOutcome ApplyExposureSet(ApplySetPayload p, bool undo) {
            var o = new ChangeOutcome();
            using var s = Open();
            var found = new Dictionary<ApplySetTarget, int?>();
            foreach (var t in p.Targets) {
                var label = t.TargetName ?? $"Target {t.TargetId}";
                var row = s.Row("SELECT Id, projectid, guid FROM target WHERE Id = @id", ("@id", t.TargetId));
                if (row == null) {
                    o.Conflict(label, "present", "removed");
                    continue;
                }
                var guid = Str(row["guid"]);
                if (!string.IsNullOrEmpty(t.TargetGuid) && !string.IsNullOrEmpty(guid) && guid != t.TargetGuid) {
                    o.Conflict($"{label} guid", t.TargetGuid, guid);
                    continue;
                }
                found[t] = (int?)Long(row["projectid"]);
                foreach (var r in t.Disable.Concat(t.Enable)) {
                    CheckPlanRef(s, o, t, r);
                }
                foreach (var c in t.Create) {
                    var existing = s.Row("SELECT targetid FROM exposureplan WHERE guid = @g", ("@g", c.Guid));
                    if (existing != null && Long(existing["targetid"]) != t.TargetId) {
                        o.Conflict($"{label} {c.TemplateName} plan", $"on {label}", "on another target");
                    } else if (!undo && existing == null && s.Count("SELECT COUNT(*) FROM exposuretemplate WHERE Id = @id", ("@id", c.TemplateId)) == 0) {
                        o.Conflict($"{c.TemplateName ?? "Template " + c.TemplateId} template", "present", "removed");
                    }
                }
            }
            if (o.Conflicts.Count > 0) {
                return o;
            }
            foreach (var t in p.Targets) {
                bool added = false, toggled = false;
                if (!undo) {
                    foreach (var c in t.Create) {
                        if (s.Count("SELECT COUNT(*) FROM exposureplan WHERE guid = @g", ("@g", c.Guid)) > 0) {
                            continue;
                        }
                        var profile = s.Scalar("SELECT profileId FROM exposureplan WHERE targetid = @t ORDER BY Id LIMIT 1", ("@t", t.TargetId))
                            ?? s.Scalar("SELECT profileId FROM project WHERE Id = @p", ("@p", Db(found[t])))
                            ?? s.Scalar("SELECT profileId FROM exposuretemplate WHERE Id = @id", ("@id", c.TemplateId));
                        s.Exec("INSERT INTO exposureplan (profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled, guid) VALUES (@pr, @e, @d, 0, 0, @t, @tmpl, 1, @g)",
                            ("@pr", profile), ("@e", c.Exposure), ("@d", c.Desired), ("@t", t.TargetId), ("@tmpl", c.TemplateId), ("@g", c.Guid));
                        added = true;
                    }
                } else {
                    foreach (var c in t.Create) {
                        var row = s.Row("SELECT Id, acquired, enabled FROM exposureplan WHERE guid = @g", ("@g", c.Guid));
                        if (row == null) {
                            continue;
                        }
                        var id = Long(row["Id"]).Value;
                        var images = s.Count("SELECT COUNT(*) FROM acquiredimage WHERE exposureId = @id", ("@id", id));
                        if (Long(row["acquired"]).GetValueOrDefault() == 0 && images == 0) {
                            s.Exec("DELETE FROM exposureplan WHERE Id = @id", ("@id", id));
                            toggled = true;
                        } else if (SetEnabled(s, id, false)) {
                            toggled = true;
                        }
                    }
                }
                foreach (var r in t.Disable) {
                    toggled |= SetEnabled(s, r.Id, undo);
                }
                foreach (var r in t.Enable) {
                    toggled |= SetEnabled(s, r.Id, !undo);
                }
                if (added || toggled) {
                    s.Exec("DELETE FROM filtercadenceitem WHERE targetid = @t", ("@t", t.TargetId));
                    if (toggled) {
                        s.Exec("DELETE FROM overrideexposureorderitem WHERE targetid = @t", ("@t", t.TargetId));
                    }
                    o.Touch(found[t], t.TargetId);
                    o.Changed = true;
                }
            }
            s.Commit();
            return o;
        }

        private static void CheckPlanRef(Session s, ChangeOutcome o, ApplySetTarget t, PlanRef r) {
            var label = $"{t.TargetName ?? "Target " + t.TargetId} {r.TemplateName ?? "plan " + r.Id} plan";
            var row = s.Row("SELECT targetid, guid FROM exposureplan WHERE Id = @id", ("@id", r.Id));
            if (row == null) {
                o.Conflict(label, "present", "removed");
                return;
            }
            var guid = Str(row["guid"]);
            if (Long(row["targetid"]) != t.TargetId || (!string.IsNullOrEmpty(r.Guid) && !string.IsNullOrEmpty(guid) && guid != r.Guid)) {
                o.Conflict(label, $"plan {r.Id}", "a different plan");
            }
        }

        private static bool SetEnabled(Session s, long planId, bool enabled) {
            return s.Exec("UPDATE exposureplan SET enabled = @v WHERE Id = @id AND COALESCE(enabled, 1) <> @v", ("@v", enabled ? 1 : 0), ("@id", planId)) > 0;
        }

        public ChangeOutcome CreateProject(ProjectCreatePayload p, string profileId, IReadOnlyDictionary<string, double> defaultRuleWeights, DateTime now) {
            var o = new ChangeOutcome();
            using var s = Open();
            if (s.Count("SELECT COUNT(*) FROM project WHERE guid = @g", ("@g", p.Project.Guid)) > 0) {
                o.Message = $"{p.Project.Name} already exists.";
                return o;
            }
            var plans = p.Targets.SelectMany(t => t.Plans).ToList();
            var templateProfiles = new Dictionary<int, string>();
            foreach (var pl in plans) {
                if (templateProfiles.ContainsKey(pl.TemplateId)) {
                    continue;
                }
                var row = s.Row("SELECT profileId FROM exposuretemplate WHERE Id = @id", ("@id", pl.TemplateId));
                if (row == null) {
                    return o.Reject($"The {pl.TemplateName ?? "template " + pl.TemplateId} template no longer exists.");
                }
                templateProfiles[pl.TemplateId] = Str(row["profileId"]);
            }
            var profile = !string.IsNullOrEmpty(profileId) ? profileId
                : templateProfiles.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v))
                ?? Str(s.Scalar("SELECT profileId FROM project ORDER BY Id LIMIT 1"));
            if (string.IsNullOrEmpty(profile)) {
                return o.Reject("The observatory has no profile to add the project to.");
            }
            var foreign = plans.FirstOrDefault(pl => templateProfiles[pl.TemplateId] != profile);
            if (foreign != null) {
                return o.Reject($"The {foreign.TemplateName ?? "template " + foreign.TemplateId} template belongs to another profile.");
            }
            if (p.Match != "separate" && s.Count("SELECT COUNT(*) FROM project WHERE profileId = @p AND name = @n", ("@p", profile), ("@n", p.Project.Name)) > 0) {
                return o.Reject($"A project called {p.Project.Name} already exists.");
            }
            foreach (var t in p.Targets) {
                if (s.Count("SELECT COUNT(*) FROM target WHERE guid = @g", ("@g", t.Guid)) > 0) {
                    return o.Reject($"Target {t.Name} already exists in another project.");
                }
            }
            foreach (var pl in plans) {
                if (s.Count("SELECT COUNT(*) FROM exposureplan WHERE guid = @g", ("@g", pl.Guid)) > 0) {
                    return o.Reject($"Exposure plan {pl.Guid} already exists.");
                }
            }
            if (p.Goals.Count > 0 && !s.TableExists("ts_goal")) {
                return o.Reject("Goals are not set up on the observatory yet.");
            }
            var created = new DateTimeOffset(now.ToUniversalTime()).ToUnixTimeSeconds();
            var projectId = s.Insert(@"INSERT INTO project (profileId, name, description, state, priority, createdate, activedate, inactivedate, minimumtime, minimumaltitude, maximumAltitude,
usecustomhorizon, horizonoffset, meridianwindow, filterswitchfrequency, ditherevery, smartexposureorder, enablegrader, isMosaic, flatsHandling, guid)
VALUES (@pr, @name, @desc, @state, @prio, @created, NULL, NULL, @mintime, @minalt, 0, 0, 0, 0, 0, 0, 0, 1, @mosaic, 0, @g)",
                ("@pr", profile), ("@name", p.Project.Name), ("@desc", Db(string.IsNullOrEmpty(p.Project.Description) ? null : p.Project.Description)),
                ("@state", p.Project.State), ("@prio", p.Project.Priority), ("@created", created), ("@mintime", p.Project.MinimumTime),
                ("@minalt", p.Project.MinimumAltitude), ("@mosaic", p.Project.IsMosaic ? 1 : 0), ("@g", p.Project.Guid));
            foreach (var t in p.Targets) {
                var targetId = s.Insert("INSERT INTO target (name, active, ra, dec, epochcode, rotation, roi, projectid, guid) VALUES (@n, 1, @ra, @dec, 2, @rot, 100, @p, @g)",
                    ("@n", t.Name), ("@ra", t.RaHours), ("@dec", t.Dec), ("@rot", t.Rotation), ("@p", projectId), ("@g", t.Guid));
                foreach (var pl in t.Plans) {
                    s.Exec("INSERT INTO exposureplan (profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled, guid) VALUES (@pr, @e, @d, 0, 0, @t, @tmpl, 1, @g)",
                        ("@pr", profile), ("@e", pl.Exposure), ("@d", pl.Desired), ("@t", targetId), ("@tmpl", pl.TemplateId), ("@g", pl.Guid));
                }
            }
            var weights = new Dictionary<string, double>(p.RuleWeights ?? new Dictionary<string, double>());
            foreach (var kv in defaultRuleWeights ?? new Dictionary<string, double>()) {
                if (!weights.ContainsKey(kv.Key)) {
                    weights[kv.Key] = kv.Value;
                }
            }
            foreach (var kv in weights.OrderBy(kv => kv.Key, StringComparer.Ordinal)) {
                s.Exec("INSERT INTO ruleweight (name, weight, projectid) VALUES (@n, @w, @p)", ("@n", kv.Key), ("@w", kv.Value), ("@p", projectId));
            }
            var stamp = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            foreach (var g in p.Goals) {
                WriteGoal(s, g.TargetGuid, g.Filter, g.Setting, stamp);
            }
            s.Commit();
            o.Touch((int)projectId, null);
            o.Changed = true;
            return o;
        }

        public ChangeOutcome DeleteProject(ProjectCreatePayload p) {
            var o = new ChangeOutcome();
            using var s = Open();
            var id = Long(s.Scalar("SELECT Id FROM project WHERE guid = @g", ("@g", p.Project.Guid)));
            if (!id.HasValue) {
                o.Message = $"{p.Project.Name} was already gone.";
                return o;
            }
            var pid = ("@p", (object)id.Value);
            const string targets = "SELECT Id FROM target WHERE projectid = @p";
            var images = s.Count($"SELECT COUNT(*) FROM acquiredimage WHERE projectId = @p OR targetId IN ({targets})", pid);
            var acquired = s.Count($"SELECT COALESCE(SUM(acquired), 0) FROM exposureplan WHERE targetid IN ({targets})", pid);
            if (images > 0 || acquired > 0) {
                var subs = Math.Max(images, acquired);
                return o.Reject($"{p.Project.Name} already has {subs} {(subs == 1 ? "sub" : "subs")}, so it was not removed.");
            }
            if (s.TableExists("ts_goal")) {
                s.Exec("DELETE FROM ts_goal WHERE target_guid IN (SELECT guid FROM target WHERE projectid = @p AND guid IS NOT NULL)", pid);
            }
            s.Exec($"DELETE FROM filtercadenceitem WHERE targetid IN ({targets})", pid);
            s.Exec($"DELETE FROM overrideexposureorderitem WHERE targetid IN ({targets})", pid);
            s.Exec($"DELETE FROM flathistory WHERE targetId IN ({targets})", pid);
            s.Exec($"DELETE FROM exposureplan WHERE targetid IN ({targets})", pid);
            s.Exec("DELETE FROM target WHERE projectid = @p", pid);
            s.Exec("DELETE FROM ruleweight WHERE projectid = @p", pid);
            s.Exec("DELETE FROM project WHERE Id = @p", pid);
            s.Commit();
            o.Touch((int)id.Value, null);
            o.Changed = true;
            return o;
        }
    }
}
