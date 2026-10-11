using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Flats {

    public class FlatRequestStore {
        public const string RequestTable = "ts_flat_request";
        public const string TakenTable = "ts_flat_request_taken";

        public const string CreateRequestTable = @"CREATE TABLE IF NOT EXISTS ts_flat_request (
    request_id TEXT NOT NULL PRIMARY KEY,
    target_id INTEGER NOT NULL,
    target_name TEXT NOT NULL,
    night TEXT NOT NULL,
    filters TEXT,
    requested_at TEXT NOT NULL,
    computed_at TEXT NOT NULL
)";

        public const string CreateTakenTable = @"CREATE TABLE IF NOT EXISTS ts_flat_request_taken (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id TEXT NOT NULL,
    taken_at TEXT NOT NULL,
    sets INTEGER NOT NULL,
    note TEXT
)";

        private readonly Func<DbConnection> connect;

        public FlatRequestStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public static void Ensure(DbConnection connection) {
            Darks.SqliteRows.Ensure(connection, CreateRequestTable, CreateTakenTable);
        }

        public void Ensure() {
            using DbConnection c = connect();
            Ensure(c);
        }

        public IList<FlatRequest> LoadRequests() {
            List<FlatRequest> rows = new List<FlatRequest>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, RequestTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT request_id, target_id, target_name, night, filters, requested_at, computed_at FROM ts_flat_request ORDER BY requested_at, request_id";
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                double? targetId = Real(r, 1);
                rows.Add(new FlatRequest {
                    RequestId = Text(r, 0),
                    TargetId = targetId.HasValue ? (int)Math.Round(targetId.Value) : 0,
                    TargetName = Text(r, 2),
                    Night = Text(r, 3)?.Trim(),
                    Filters = Text(r, 4),
                    RequestedAt = Time(r, 5),
                    ComputedAt = Time(r, 6)
                });
            }
            rows.RemoveAll(q => string.IsNullOrEmpty(q.RequestId) || q.TargetId <= 0 || string.IsNullOrEmpty(q.Night));
            return rows;
        }

        public IList<FlatRequestTaken> LoadTaken(DateTime sinceUtc) {
            List<FlatRequestTaken> rows = new List<FlatRequestTaken>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, TakenTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT request_id, taken_at, sets, note FROM ts_flat_request_taken WHERE taken_at >= @since";
            Add(cmd, "@since", Format(sinceUtc));
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                DateTime? at = Time(r, 1);
                if (at.HasValue) {
                    rows.Add(new FlatRequestTaken { RequestId = Text(r, 0), TakenAt = at.Value, Sets = (int)Math.Round(Real(r, 2) ?? 0), Note = Text(r, 3) });
                }
            }
            return rows;
        }

        public void RecordTaken(FlatRequest request, DateTime takenAtUtc, int sets, string note) {
            using DbConnection c = connect();
            Ensure(c);
            if (c.State != ConnectionState.Open) { c.Open(); }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO ts_flat_request_taken (request_id, taken_at, sets, note) VALUES (@r, @t, @s, @n)";
            Add(cmd, "@r", request.RequestId);
            Add(cmd, "@t", Format(takenAtUtc));
            Add(cmd, "@s", sets);
            Add(cmd, "@n", note);
            cmd.ExecuteNonQuery();
        }
    }
}
