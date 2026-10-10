using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public static class GoalSchema {
        public const string GoalTable = "ts_goal";
        public const string GoalProgressTable = "ts_goal_progress";
        public const string TargetSeasonTable = "ts_target_season";

        public const string CreateGoalTable = @"CREATE TABLE IF NOT EXISTS ts_goal (
    target_guid TEXT NOT NULL,
    filter TEXT NOT NULL,
    kind INTEGER NOT NULL DEFAULT 0,
    snr_goal REAL NOT NULL DEFAULT 10,
    depth_goal REAL,
    plateau_stop INTEGER NOT NULL DEFAULT 1,
    region TEXT,
    updated_at TEXT,
    PRIMARY KEY (target_guid, filter)
)";

        public const string CreateGoalProgressTable = @"CREATE TABLE IF NOT EXISTS ts_goal_progress (
    target_guid TEXT NOT NULL,
    filter TEXT NOT NULL,
    kind INTEGER,
    goal_value REAL,
    achieved_value REAL,
    progress REAL,
    snr REAL,
    depth REAL,
    effective_hours REAL,
    hours_needed REAL,
    gain_per_hour_pct REAL,
    plateau INTEGER,
    low_confidence INTEGER,
    done INTEGER,
    measured_at TEXT,
    PRIMARY KEY (target_guid, filter)
)";

        public const string CreateTargetSeasonTable = @"CREATE TABLE IF NOT EXISTS ts_target_season (
    target_guid TEXT NOT NULL PRIMARY KEY,
    nights_left INTEGER,
    out_of_season INTEGER,
    season_end TEXT,
    computed_for TEXT,
    computed_at TEXT
)";

        public static readonly IReadOnlyList<string> All = new[] { CreateGoalTable, CreateGoalProgressTable, CreateTargetSeasonTable };

        public static void Ensure(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                foreach (string sql in All) {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
