using System;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Darks {

    public static class SqliteRows {

        public static void Ensure(DbConnection connection, params string[] statements) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                foreach (string sql in statements) {
                    using DbCommand cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }

        public static string Format(DateTime utc) {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        public static bool TableExists(DbConnection connection, string table) {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
            Add(cmd, "@name", table);
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }

        public static void Add(DbCommand cmd, string name, object value) {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public static string Text(DbDataReader r, int i) {
            if (r.IsDBNull(i)) { return null; }
            return Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
        }

        public static double? Real(DbDataReader r, int i) {
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

        public static DateTime? Time(DbDataReader r, int i) {
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
