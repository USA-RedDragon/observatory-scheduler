using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlTargetSeasonReader : ITargetSeasonReader {
        private readonly Func<DbConnection> connect;

        public SqlTargetSeasonReader(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public IList<TargetSeasonRow> LoadAll() {
            List<TargetSeasonRow> rows = new List<TargetSeasonRow>();
            using var conn = connect();
            if (conn.State != ConnectionState.Open) { conn.Open(); }
            if (!SqlGoalRowProvider.TableExists(conn, GoalSchema.TargetSeasonTable)) { return rows; }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT target_guid, nights_left, out_of_season, computed_for FROM ts_target_season";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                if (r.IsDBNull(0) || r.IsDBNull(3)) { continue; }
                if (!DateTime.TryParseExact(Convert.ToString(r.GetValue(3), CultureInfo.InvariantCulture), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime computedFor)) { continue; }
                rows.Add(new TargetSeasonRow {
                    TargetGuid = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture),
                    NightsLeft = r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                    OutOfSeason = !r.IsDBNull(2) && Convert.ToInt64(r.GetValue(2), CultureInfo.InvariantCulture) != 0,
                    ComputedFor = computedFor
                });
            }
            return rows;
        }
    }
}
