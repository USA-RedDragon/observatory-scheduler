using NINA.Plugin.TargetScheduler.Goals;
using System;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlTargetSeasonWriter : ITargetSeasonWriter {
        public const string UpsertSql =
            "INSERT INTO ts_target_season (target_guid, nights_left, out_of_season, season_end, computed_for, computed_at) " +
            "VALUES (@guid, @nights, @out, @end, @for, @at) " +
            "ON CONFLICT(target_guid) DO UPDATE SET nights_left = excluded.nights_left, out_of_season = excluded.out_of_season, " +
            "season_end = excluded.season_end, computed_for = excluded.computed_for, computed_at = excluded.computed_at";

        private readonly Func<DbConnection> connect;

        public SqlTargetSeasonWriter(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public void Upsert(TargetSeasonRow row) {
            using var conn = connect();
            if (conn.State != ConnectionState.Open) { conn.Open(); }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = UpsertSql;
            SqlCommandStore.Add(cmd, "@guid", row.TargetGuid);
            SqlCommandStore.Add(cmd, "@nights", row.NightsLeft);
            SqlCommandStore.Add(cmd, "@out", row.OutOfSeason ? 1 : 0);
            SqlCommandStore.Add(cmd, "@end", row.SeasonEnd.HasValue ? row.SeasonEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null);
            SqlCommandStore.Add(cmd, "@for", row.ComputedFor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            SqlCommandStore.Add(cmd, "@at", row.ComputedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }
}
