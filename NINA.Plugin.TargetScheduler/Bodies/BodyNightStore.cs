using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public static class BodyNightStore {
        public const string Table = "ts_body_night";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_body_night (
    target_guid TEXT NOT NULL,
    night TEXT NOT NULL,
    at TEXT NOT NULL,
    designation TEXT NOT NULL,
    ra REAL NOT NULL,
    dec REAL NOT NULL,
    ra_rate REAL NOT NULL,
    dec_rate REAL NOT NULL,
    mag REAL,
    mag_limit REAL,
    computed_at TEXT NOT NULL,
    PRIMARY KEY (target_guid, night)
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static BodyNightTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return BodyNightTable.Empty; }
                List<BodyNightRow> rows = new List<BodyNightRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT target_guid, night, at, designation, ra, dec, ra_rate, dec_rate, mag, mag_limit, computed_at FROM ts_body_night";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    var at = Time(r, 2);
                    double? ra = Real(r, 4);
                    double? dec = Real(r, 5);
                    double? raRate = Real(r, 6);
                    double? decRate = Real(r, 7);
                    if (!at.HasValue || !ra.HasValue || !dec.HasValue || !raRate.HasValue || !decRate.HasValue) { continue; }
                    rows.Add(new BodyNightRow {
                        TargetGuid = Text(r, 0),
                        Night = Text(r, 1),
                        At = at.Value,
                        Designation = Text(r, 3),
                        Ra = ra.Value,
                        Dec = dec.Value,
                        RaRate = raRate.Value,
                        DecRate = decRate.Value,
                        Mag = Real(r, 8),
                        MagLimit = Real(r, 9),
                        ComputedAt = Time(r, 10)
                    });
                }
                return new BodyNightTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
