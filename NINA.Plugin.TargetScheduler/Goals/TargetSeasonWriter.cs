using System;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class TargetSeasonRow {
        public string TargetGuid { get; set; }
        public int NightsLeft { get; set; }
        public bool OutOfSeason { get; set; }
        public DateTime? SeasonEnd { get; set; }
        public DateTime ComputedFor { get; set; }
        public DateTime ComputedAt { get; set; }

        public static TargetSeasonRow From(string targetGuid, SeasonResult season, DateTime nightDate, DateTime computedAtUtc) {
            return new TargetSeasonRow {
                TargetGuid = targetGuid,
                NightsLeft = season.NightsLeft,
                OutOfSeason = season.OutOfSeason,
                SeasonEnd = season.OutOfSeason || season.Capped ? (DateTime?)null : nightDate.Date.AddDays(season.LastUsableNight),
                ComputedFor = nightDate.Date,
                ComputedAt = computedAtUtc
            };
        }
    }

    public interface ITargetSeasonWriter {

        void Upsert(TargetSeasonRow row);
    }

    public class DbTargetSeasonWriter : ITargetSeasonWriter {
        public const string UpsertSql =
            "INSERT INTO ts_target_season (target_guid, nights_left, out_of_season, season_end, computed_for, computed_at) " +
            "VALUES (@guid, @nights, @out, @end, @for, @at) " +
            "ON CONFLICT(target_guid) DO UPDATE SET nights_left = excluded.nights_left, out_of_season = excluded.out_of_season, " +
            "season_end = excluded.season_end, computed_for = excluded.computed_for, computed_at = excluded.computed_at";

        private readonly Func<DbConnection> connectionFactory;

        public DbTargetSeasonWriter(Func<DbConnection> connectionFactory) {
            this.connectionFactory = connectionFactory;
        }

        public void Upsert(TargetSeasonRow row) {
            using (DbConnection connection = connectionFactory()) {
                Upsert(connection, row);
            }
        }

        public static void Upsert(DbConnection connection, TargetSeasonRow row) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                using (DbCommand cmd = connection.CreateCommand()) {
                    cmd.CommandText = UpsertSql;
                    Add(cmd, "@guid", row.TargetGuid);
                    Add(cmd, "@nights", row.NightsLeft);
                    Add(cmd, "@out", row.OutOfSeason ? 1 : 0);
                    Add(cmd, "@end", row.SeasonEnd.HasValue ? row.SeasonEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
                    Add(cmd, "@for", row.ComputedFor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    Add(cmd, "@at", row.ComputedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                    cmd.ExecuteNonQuery();
                }
            } finally {
                if (opened) { connection.Close(); }
            }
        }

        private static void Add(DbCommand cmd, string name, object value) {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}
