using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class BiasStore {
        public const string NeedTable = "ts_bias_need";
        public const string TakenTable = "ts_bias_taken";

        public const string CreateNeedTable = @"CREATE TABLE IF NOT EXISTS ts_bias_need (
    combo_key TEXT NOT NULL PRIMARY KEY,
    priority INTEGER NOT NULL,
    gain REAL NOT NULL,
    camera_offset REAL NOT NULL,
    binning REAL NOT NULL,
    readout_mode TEXT,
    frames_needed INTEGER NOT NULL,
    frames_have INTEGER NOT NULL,
    newest_bias_at TEXT,
    session_gap_hours REAL NOT NULL,
    basis TEXT,
    computed_at TEXT NOT NULL
)";

        public const string CreateTakenTable = @"CREATE TABLE IF NOT EXISTS ts_bias_taken (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    combo_key TEXT NOT NULL,
    taken_at TEXT NOT NULL,
    exposure REAL NOT NULL,
    sensor_temp REAL,
    path TEXT
)";

        private readonly Func<DbConnection> connect;

        public BiasStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateNeedTable, CreateTakenTable);
        }

        public void Ensure() {
            using DbConnection c = connect();
            Ensure(c);
        }

        public IList<BiasNeed> LoadNeeds() {
            List<BiasNeed> rows = new List<BiasNeed>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, NeedTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT combo_key, priority, gain, camera_offset, binning, readout_mode, frames_needed, frames_have, newest_bias_at, session_gap_hours, basis, computed_at FROM ts_bias_need ORDER BY priority";
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                rows.Add(new BiasNeed {
                    ComboKey = Text(r, 0),
                    Priority = (int)Math.Round(Real(r, 1) ?? int.MaxValue),
                    Gain = Real(r, 2) ?? 0,
                    Offset = Real(r, 3) ?? 0,
                    Binning = Real(r, 4) ?? 1,
                    ReadoutMode = Text(r, 5),
                    FramesNeeded = (int)Math.Round(Real(r, 6) ?? 0),
                    FramesHave = (int)Math.Round(Real(r, 7) ?? 0),
                    NewestBiasAt = Time(r, 8),
                    SessionGapHours = Real(r, 9) ?? 0,
                    Basis = Text(r, 10),
                    ComputedAt = Time(r, 11)
                });
            }
            rows.RemoveAll(n => string.IsNullOrEmpty(n.ComboKey));
            return rows;
        }

        public IList<BiasTaken> LoadTaken(DateTime sinceUtc) {
            List<BiasTaken> rows = new List<BiasTaken>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, TakenTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT combo_key, taken_at FROM ts_bias_taken WHERE taken_at >= @since";
            Add(cmd, "@since", Format(sinceUtc));
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                DateTime? at = Time(r, 1);
                if (at.HasValue) {
                    rows.Add(new BiasTaken { ComboKey = Text(r, 0), TakenAt = at.Value });
                }
            }
            return rows;
        }

        public void RecordTaken(BiasNeed need, DateTime takenAtUtc, double exposure, double? sensorTemp, string path) {
            using DbConnection c = connect();
            Ensure(c);
            if (c.State != ConnectionState.Open) { c.Open(); }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO ts_bias_taken (combo_key, taken_at, exposure, sensor_temp, path) VALUES (@k, @t, @e, @st, @p)";
            Add(cmd, "@k", need.ComboKey);
            Add(cmd, "@t", Format(takenAtUtc));
            Add(cmd, "@e", exposure);
            Add(cmd, "@st", sensorTemp.HasValue && double.IsNaN(sensorTemp.Value) ? null : sensorTemp);
            Add(cmd, "@p", path);
            cmd.ExecuteNonQuery();
        }
    }
}
