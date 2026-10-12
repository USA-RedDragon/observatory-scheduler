using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class TemplateRoleStore {
        public const string RoleTable = "ts_template_role";
        public const string MigrationTable = "ts_template_migration";

        public const string CreateRoleTable = @"CREATE TABLE IF NOT EXISTS ts_template_role (
    template_id INTEGER NOT NULL PRIMARY KEY,
    template_guid TEXT,
    profile_id TEXT NOT NULL,
    filter TEXT NOT NULL,
    role TEXT NOT NULL,
    changed_at TEXT NOT NULL
)";

        public const string CreateMigrationTable = @"CREATE TABLE IF NOT EXISTS ts_template_migration (
    plan_id INTEGER NOT NULL PRIMARY KEY,
    plan_guid TEXT,
    target_id INTEGER,
    profile_id TEXT NOT NULL,
    filter TEXT NOT NULL,
    old_template_id INTEGER NOT NULL,
    old_template_guid TEXT,
    old_template_name TEXT,
    new_template_id INTEGER NOT NULL,
    migrated_at TEXT NOT NULL,
    reverted_at TEXT
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateRoleTable, CreateMigrationTable);
        }

        public static ManagedTemplateTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, RoleTable)) { return ManagedTemplateTable.Empty; }
                List<TemplateRoleRow> rows = new List<TemplateRoleRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT template_id, template_guid, profile_id, filter, role, changed_at FROM ts_template_role";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    double? id = Real(r, 0);
                    if (!id.HasValue) { continue; }
                    rows.Add(new TemplateRoleRow {
                        TemplateId = (int)Math.Round(id.Value),
                        TemplateGuid = Text(r, 1),
                        ProfileId = Text(r, 2),
                        Filter = Text(r, 3),
                        Role = Text(r, 4),
                        ChangedAt = Time(r, 5)
                    });
                }
                return rows.Count == 0 ? ManagedTemplateTable.Empty : new ManagedTemplateTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
