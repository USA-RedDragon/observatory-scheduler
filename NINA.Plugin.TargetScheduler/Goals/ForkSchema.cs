using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class ForkSchema {
        public const string GoalTable = "ts_goal";
        public const string GoalProgressTable = "ts_goal_progress";
        public const string TargetSeasonTable = "ts_target_season";

        public static readonly IReadOnlyList<string> Statements = new List<string> {
            "CREATE TABLE IF NOT EXISTS ts_goal (" +
                "target_guid TEXT NOT NULL, " +
                "filter TEXT NOT NULL, " +
                "kind INTEGER NOT NULL DEFAULT 0, " +
                "snr_goal REAL NOT NULL DEFAULT 10, " +
                "depth_goal REAL, " +
                "plateau_stop INTEGER NOT NULL DEFAULT 1, " +
                "region TEXT, " +
                "updated_at TEXT, " +
                "PRIMARY KEY (target_guid, filter))",
            "CREATE TABLE IF NOT EXISTS ts_goal_progress (" +
                "target_guid TEXT NOT NULL, " +
                "filter TEXT NOT NULL, " +
                "kind INTEGER, " +
                "goal_value REAL, " +
                "achieved_value REAL, " +
                "progress REAL, " +
                "snr REAL, " +
                "depth REAL, " +
                "effective_hours REAL, " +
                "hours_needed REAL, " +
                "gain_per_hour_pct REAL, " +
                "plateau INTEGER, " +
                "low_confidence INTEGER, " +
                "done INTEGER, " +
                "measured_at TEXT, " +
                "PRIMARY KEY (target_guid, filter))",
            "CREATE TABLE IF NOT EXISTS ts_target_season (" +
                "target_guid TEXT PRIMARY KEY, " +
                "nights_left INTEGER, " +
                "out_of_season INTEGER, " +
                "season_end TEXT, " +
                "computed_for TEXT, " +
                "computed_at TEXT)"
        };

        private static readonly object ensureLock = new object();
        private static bool ensured;

        public static bool Ensured => ensured;

        public static void Ensure(Action<string> execute) {
            foreach (string sql in Statements) {
                execute(sql);
            }
        }

        public static void Ensure(DbConnection connection) {
            WithOpen(connection, () => Ensure(sql => {
                using (DbCommand cmd = connection.CreateCommand()) {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }));
        }

        public static bool EnsureOnce(Action<string> execute) {
            lock (ensureLock) {
                if (ensured) { return false; }
                Ensure(execute);
                ensured = true;
                return true;
            }
        }

        public static bool TableExists(DbConnection connection, string table) {
            bool exists = false;
            WithOpen(connection, () => {
                using (DbCommand cmd = connection.CreateCommand()) {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
                    DbParameter p = cmd.CreateParameter();
                    p.ParameterName = "@name";
                    p.Value = table;
                    cmd.Parameters.Add(p);
                    exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                }
            });
            return exists;
        }

        private static void WithOpen(DbConnection connection, Action action) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                action();
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
