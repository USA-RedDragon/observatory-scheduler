using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public static class TargetWindowStore {
        public const string Table = "ts_target_window";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_target_window (
    target_guid TEXT NOT NULL PRIMARY KEY,
    start_utc TEXT NOT NULL,
    end_utc TEXT NOT NULL,
    priority TEXT NOT NULL DEFAULT 'normal',
    event_id TEXT,
    ignore_moon INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static TargetWindowTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return TargetWindowTable.Empty; }
                List<TargetWindowRow> rows = new List<TargetWindowRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT target_guid, start_utc, end_utc, priority, event_id, ignore_moon, updated_at FROM ts_target_window";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    DateTime? start = Time(r, 1);
                    DateTime? end = Time(r, 2);
                    if (!start.HasValue || !end.HasValue) { continue; }
                    string priority = Text(r, 3)?.Trim().ToLowerInvariant();
                    rows.Add(new TargetWindowRow {
                        TargetGuid = Text(r, 0),
                        StartUtc = start.Value,
                        EndUtc = end.Value,
                        Priority = priority == TargetWindowRow.High ? TargetWindowRow.High : TargetWindowRow.Normal,
                        EventId = Text(r, 4),
                        IgnoreMoon = (Real(r, 5) ?? 0) != 0,
                        UpdatedAt = Time(r, 6)
                    });
                }
                return rows.Count == 0 ? TargetWindowTable.Empty : new TargetWindowTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
