using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class DarkStore {
        public const string NeedTable = "ts_dark_need";
        public const string TakenTable = "ts_dark_taken";

        public const string CreateNeedTable = @"CREATE TABLE IF NOT EXISTS ts_dark_need (
    combo_key TEXT NOT NULL PRIMARY KEY,
    priority INTEGER NOT NULL,
    exposure REAL NOT NULL,
    gain REAL NOT NULL,
    camera_offset REAL NOT NULL,
    binning REAL NOT NULL,
    readout_mode TEXT,
    set_temp REAL NOT NULL,
    frames_needed INTEGER NOT NULL,
    frames_have INTEGER NOT NULL,
    frames_rejected INTEGER NOT NULL,
    lights_blocked INTEGER NOT NULL,
    lights_scaled INTEGER NOT NULL,
    newest_dark_at TEXT,
    session_gap_hours REAL NOT NULL,
    basis TEXT,
    computed_at TEXT NOT NULL
)";

        public const string CreateTakenTable = @"CREATE TABLE IF NOT EXISTS ts_dark_taken (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    combo_key TEXT NOT NULL,
    taken_at TEXT NOT NULL,
    exposure REAL NOT NULL,
    set_temp REAL NOT NULL,
    sensor_temp REAL,
    max_deviation REAL,
    path TEXT
)";

        private readonly Func<DbConnection> connect;

        public DarkStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public static void Ensure(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                foreach (string sql in new[] { CreateNeedTable, CreateTakenTable }) {
                    using DbCommand cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }

        public void Ensure() {
            using DbConnection c = connect();
            Ensure(c);
        }

        public IList<DarkNeed> LoadNeeds() {
            List<DarkNeed> rows = new List<DarkNeed>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, NeedTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT combo_key, priority, exposure, gain, camera_offset, binning, readout_mode, set_temp, frames_needed, frames_have, frames_rejected, lights_blocked, lights_scaled, newest_dark_at, session_gap_hours, basis, computed_at FROM ts_dark_need ORDER BY priority";
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                rows.Add(new DarkNeed {
                    ComboKey = Text(r, 0),
                    Priority = (int)Math.Round(Real(r, 1) ?? int.MaxValue),
                    Exposure = Real(r, 2) ?? 0,
                    Gain = Real(r, 3) ?? 0,
                    Offset = Real(r, 4) ?? 0,
                    Binning = Real(r, 5) ?? 1,
                    ReadoutMode = Text(r, 6),
                    SetTemp = Real(r, 7) ?? double.NaN,
                    FramesNeeded = (int)Math.Round(Real(r, 8) ?? 0),
                    FramesHave = (int)Math.Round(Real(r, 9) ?? 0),
                    FramesRejected = (int)Math.Round(Real(r, 10) ?? 0),
                    LightsBlocked = (int)Math.Round(Real(r, 11) ?? 0),
                    LightsScaled = (int)Math.Round(Real(r, 12) ?? 0),
                    NewestDarkAt = Time(r, 13),
                    SessionGapHours = Real(r, 14) ?? 0,
                    Basis = Text(r, 15),
                    ComputedAt = Time(r, 16)
                });
            }
            rows.RemoveAll(n => string.IsNullOrEmpty(n.ComboKey) || double.IsNaN(n.SetTemp));
            return rows;
        }

        public IList<DarkTaken> LoadTaken(DateTime sinceUtc) {
            List<DarkTaken> rows = new List<DarkTaken>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, TakenTable)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT combo_key, taken_at FROM ts_dark_taken WHERE taken_at >= @since";
            Add(cmd, "@since", Format(sinceUtc));
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                DateTime? at = Time(r, 1);
                if (at.HasValue) {
                    rows.Add(new DarkTaken { ComboKey = Text(r, 0), TakenAt = at.Value });
                }
            }
            return rows;
        }

        public void RecordTaken(DarkNeed need, DateTime takenAtUtc, double? sensorTemp, double? maxDeviation, string path) {
            using DbConnection c = connect();
            Ensure(c);
            if (c.State != ConnectionState.Open) { c.Open(); }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO ts_dark_taken (combo_key, taken_at, exposure, set_temp, sensor_temp, max_deviation, path) VALUES (@k, @t, @e, @s, @st, @md, @p)";
            Add(cmd, "@k", need.ComboKey);
            Add(cmd, "@t", Format(takenAtUtc));
            Add(cmd, "@e", need.Exposure);
            Add(cmd, "@s", need.SetTemp);
            Add(cmd, "@st", sensorTemp);
            Add(cmd, "@md", maxDeviation);
            Add(cmd, "@p", path);
            cmd.ExecuteNonQuery();
        }

        public static string Format(DateTime utc) {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static bool TableExists(DbConnection connection, string table) {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
            Add(cmd, "@name", table);
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        private static void Add(DbCommand cmd, string name, object value) {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static string Text(DbDataReader r, int i) {
            if (r.IsDBNull(i)) { return null; }
            return Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
        }

        private static double? Real(DbDataReader r, int i) {
            if (r.IsDBNull(i)) { return null; }
            object v = r.GetValue(i);
            if (v is string s) {
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
            }

            try {
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            } catch (Exception) {
                return null;
            }
        }

        private static DateTime? Time(DbDataReader r, int i) {
            if (r.IsDBNull(i)) { return null; }
            object v = r.GetValue(i);
            if (v is DateTime dt) {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)) {
                return t;
            }

            return null;
        }
    }
}
