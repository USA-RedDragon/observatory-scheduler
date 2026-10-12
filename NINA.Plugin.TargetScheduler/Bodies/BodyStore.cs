using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public static class BodySchema {

        public static bool TryEnsure(DbConnection connection, Action<string> warn) {
            try {
                TargetBodyStore.Ensure(connection);
                BodyPositionStore.Ensure(connection);
                return true;
            } catch (Exception ex) {
                warn?.Invoke($"failed to create {TargetBodyStore.Table} or {BodyPositionStore.Table}, moving-body targets are not imaged: {ex.Message}");
                return false;
            }
        }

        public static BodySnapshot Load(DbConnection connection, BodySnapshot previous, Action<string> warn) {
            previous ??= BodySnapshot.Empty;
            TargetBodyTable targets;
            try {
                targets = TargetBodyStore.Load(connection);
            } catch (Exception ex) {
                warn?.Invoke($"failed to load {TargetBodyStore.Table}, keeping the previous moving bodies ({previous.Targets.Count} targets, {previous.Positions.Count} positions): {ex.Message}");
                return previous;
            }

            try {
                return new BodySnapshot(targets, BodyPositionStore.Load(connection));
            } catch (Exception ex) {
                warn?.Invoke($"failed to load {BodyPositionStore.Table}, moving-body targets have no positions and are not imaged: {ex.Message}");
                return new BodySnapshot(targets, BodyPositionTable.Empty);
            }
        }
    }

    public static class TargetBodyStore {
        public const string Table = "ts_target_body";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_target_body (
    target_guid TEXT NOT NULL PRIMARY KEY,
    designation TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'comet',
    updated_at TEXT,
    rotation_deg REAL,
    offset_frac REAL
)";

        public static readonly string[] FramingColumns = { "rotation_deg", "offset_frac" };

        public const string ColumnCountSql = "SELECT COUNT(*) FROM pragma_table_info('ts_target_body') WHERE name = @c";

        public static string AddColumnSql(string column) {
            return $"ALTER TABLE ts_target_body ADD COLUMN {column} REAL";
        }

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                foreach (string column in FramingColumns) {
                    if (HasColumn(connection, column)) { continue; }
                    using DbCommand cmd = connection.CreateCommand();
                    cmd.CommandText = AddColumnSql(column);
                    cmd.ExecuteNonQuery();
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }

        public static bool HasColumn(DbConnection connection, string column) {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = ColumnCountSql;
            Add(cmd, "@c", column);
            return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
        }

        public static TargetBodyTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return TargetBodyTable.Empty; }
                bool framing = HasColumn(connection, "rotation_deg") && HasColumn(connection, "offset_frac");
                List<TargetBodyRow> rows = new List<TargetBodyRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT target_guid, designation, kind, updated_at" + (framing ? ", rotation_deg, offset_frac" : "") + " FROM ts_target_body";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    rows.Add(new TargetBodyRow {
                        TargetGuid = Text(r, 0),
                        Designation = Text(r, 1),
                        Kind = Text(r, 2),
                        UpdatedAt = Time(r, 3),
                        RotationDeg = framing ? Real(r, 4) : null,
                        OffsetFrac = framing ? Real(r, 5) : null
                    });
                }
                return new TargetBodyTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }

    public static class BodyPositionStore {
        public const string Table = "ts_body_position";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_body_position (
    target_guid TEXT NOT NULL,
    at TEXT NOT NULL,
    designation TEXT NOT NULL,
    night TEXT NOT NULL,
    ra REAL NOT NULL,
    dec REAL NOT NULL,
    ra_app REAL,
    dec_app REAL,
    ra_rate REAL NOT NULL,
    dec_rate REAL NOT NULL,
    mag REAL,
    delta REAL,
    unc_ra REAL,
    unc_dec REAL,
    computed_at TEXT NOT NULL,
    PRIMARY KEY (target_guid, at)
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static BodyPositionTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return BodyPositionTable.Empty; }
                List<BodyPositionRow> rows = new List<BodyPositionRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT target_guid, at, designation, night, ra, dec, ra_app, dec_app, ra_rate, dec_rate, mag, delta, unc_ra, unc_dec, computed_at FROM ts_body_position";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    DateTime? at = Time(r, 1);
                    double? ra = Real(r, 4);
                    double? dec = Real(r, 5);
                    double? raRate = Real(r, 8);
                    double? decRate = Real(r, 9);
                    if (!at.HasValue || !ra.HasValue || !dec.HasValue || !raRate.HasValue || !decRate.HasValue) { continue; }
                    rows.Add(new BodyPositionRow {
                        TargetGuid = Text(r, 0),
                        At = at.Value,
                        Designation = Text(r, 2),
                        Night = Text(r, 3),
                        Ra = ra.Value,
                        Dec = dec.Value,
                        RaApp = Real(r, 6),
                        DecApp = Real(r, 7),
                        RaRate = raRate.Value,
                        DecRate = decRate.Value,
                        Mag = Real(r, 10),
                        Delta = Real(r, 11),
                        UncRa = Real(r, 12),
                        UncDec = Real(r, 13),
                        ComputedAt = Time(r, 14)
                    });
                }
                return new BodyPositionTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
