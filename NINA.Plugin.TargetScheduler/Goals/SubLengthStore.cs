using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class SubLengthStore {
        public const string Table = "ts_sub_length";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_sub_length (
    target_guid TEXT NOT NULL,
    filter TEXT NOT NULL,
    gain INTEGER NOT NULL,
    bucket TEXT NOT NULL,
    exposure REAL NOT NULL,
    ideal REAL,
    current REAL,
    sky_rate REAL,
    snr_change REAL,
    basis TEXT,
    computed_at TEXT NOT NULL,
    PRIMARY KEY (target_guid, filter, gain, bucket)
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static SubLengthTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return SubLengthTable.Empty; }
                List<SubLengthRow> rows = new List<SubLengthRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT target_guid, filter, gain, bucket, exposure, ideal, current, sky_rate, snr_change, basis, computed_at FROM ts_sub_length";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    double? gain = Real(r, 2);
                    if (!gain.HasValue) { continue; }
                    rows.Add(new SubLengthRow {
                        TargetGuid = Text(r, 0),
                        Filter = Text(r, 1),
                        Gain = (int)Math.Round(gain.Value),
                        Bucket = Text(r, 3),
                        Exposure = Real(r, 4) ?? 0,
                        Ideal = Real(r, 5),
                        Current = Real(r, 6),
                        SkyRate = Real(r, 7),
                        SnrChange = Real(r, 8),
                        Basis = Text(r, 9),
                        ComputedAt = Time(r, 10)
                    });
                }
                return new SubLengthTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
