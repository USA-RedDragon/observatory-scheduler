using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlGoalRowProvider : IGoalRowProvider {
        private readonly DbConnection connection;

        public SqlGoalRowProvider(DbConnection connection) {
            this.connection = connection;
        }

        public IList<GoalRow> LoadGoals() {
            List<GoalRow> rows = new List<GoalRow>();
            Read(GoalSchema.GoalTable, "SELECT target_guid, filter, kind, snr_goal, depth_goal, plateau_stop, region, updated_at FROM ts_goal", r => {
                rows.Add(new GoalRow {
                    TargetGuid = Text(r, 0),
                    Filter = Text(r, 1),
                    Kind = (Int(r, 2) ?? 0) == 1 ? GoalKind.Depth : GoalKind.FaintSnr,
                    SnrGoal = Real(r, 3) ?? 10,
                    DepthGoal = Real(r, 4),
                    PlateauStop = (Int(r, 5) ?? 1) != 0,
                    Region = Text(r, 6),
                    UpdatedAt = Text(r, 7)
                });
            });
            return rows;
        }

        public IList<GoalProgressRow> LoadProgress() {
            List<GoalProgressRow> rows = new List<GoalProgressRow>();
            bool signals = false;
            bool readiness = false;
            WithConnection(() => {
                if (TableExists(connection, GoalSchema.GoalProgressTable)) {
                    ISet<string> columns = GoalSchema.Columns(connection, GoalSchema.GoalProgressTable);
                    signals = columns.Contains("night_quality") && columns.Contains("night_quality_at") && columns.Contains("season_boost");
                    readiness = columns.Contains("state") && columns.Contains("reason") && columns.Contains("stack_subs") && columns.Contains("min_subs") && columns.Contains("sub_limit");
                }
            });
            string extra = (signals ? ", night_quality, night_quality_at, season_boost" : ", NULL, NULL, NULL")
                + (readiness ? ", state, reason, stack_subs, min_subs, sub_limit" : string.Empty);
            Read(GoalSchema.GoalProgressTable, "SELECT target_guid, filter, kind, goal_value, achieved_value, progress, snr, depth, effective_hours, hours_needed, gain_per_hour_pct, plateau, low_confidence, done, measured_at" + extra + " FROM ts_goal_progress", r => {
                GoalProgressRow row = new GoalProgressRow {
                    TargetGuid = Text(r, 0),
                    Filter = Text(r, 1),
                    Kind = Int(r, 2),
                    GoalValue = Real(r, 3),
                    AchievedValue = Real(r, 4),
                    Progress = Real(r, 5),
                    Snr = Real(r, 6),
                    Depth = Real(r, 7),
                    EffectiveHours = Real(r, 8),
                    HoursNeeded = Real(r, 9),
                    GainPerHourPct = Real(r, 10),
                    Plateau = (Int(r, 11) ?? 0) != 0,
                    LowConfidence = (Int(r, 12) ?? 0) != 0,
                    Done = (Int(r, 13) ?? 0) != 0,
                    MeasuredAt = Time(r, 14)
                };
                if (signals) {
                    row.NightQuality = Real(r, 15);
                    DateTime at = Time(r, 16);
                    row.NightQualityAt = at == DateTime.MinValue ? (DateTime?)null : at;
                    row.SeasonBoost = Real(r, 17);
                }
                if (readiness) {
                    row.State = Text(r, 18);
                    row.Reason = Text(r, 19);
                    row.StackSubs = Int(r, 20);
                    row.MinSubs = Int(r, 21);
                    row.SubLimit = Int(r, 22);
                }
                rows.Add(row);
            });
            return rows;
        }

        private void WithConnection(Action action) {
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

        private void Read(string table, string sql, Action<DbDataReader> onRow) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, table)) { return; }

                using (DbCommand cmd = connection.CreateCommand()) {
                    cmd.CommandText = sql;
                    using (DbDataReader reader = cmd.ExecuteReader()) {
                        while (reader.Read()) {
                            onRow(reader);
                        }
                    }
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }

        public static bool TableExists(DbConnection connection, string table) {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
            SqlCommandStore.Add(cmd, "@name", table);
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
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

        private static int? Int(DbDataReader r, int i) {
            double? d = Real(r, i);
            return d.HasValue ? (int)Math.Round(d.Value) : (int?)null;
        }

        private static DateTime Time(DbDataReader r, int i) {
            string s = Text(r, i);
            if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t)) {
                return t;
            }

            return DateTime.MinValue;
        }
    }
}
