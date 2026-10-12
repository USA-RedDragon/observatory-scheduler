using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class AltKeepStore {
        public const string Table = "ts_alt_keep";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_alt_keep (
    filter TEXT NOT NULL,
    narrowband INTEGER NOT NULL,
    alt_bin INTEGER NOT NULL,
    alt_from REAL NOT NULL,
    alt_to REAL NOT NULL,
    subs INTEGER NOT NULL,
    kept INTEGER NOT NULL,
    nights INTEGER NOT NULL,
    rate REAL NOT NULL,
    rate_low REAL NOT NULL,
    rate_high REAL NOT NULL,
    worth REAL NOT NULL,
    usable INTEGER NOT NULL,
    skips INTEGER NOT NULL,
    computed_at TEXT NOT NULL,
    PRIMARY KEY (filter, narrowband, alt_bin)
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static AltKeepTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return AltKeepTable.Empty; }
                List<AltKeepRow> rows = new List<AltKeepRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT filter, narrowband, alt_from, alt_to, subs, kept, nights, rate, rate_high, worth, usable, skips FROM ts_alt_keep";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    double?[] v = Enumerable.Range(1, 11).Select(i => Real(r, i)).ToArray();
                    if (v.Any(x => !x.HasValue)) { continue; }
                    rows.Add(new AltKeepRow {
                        Filter = Text(r, 0),
                        Narrowband = v[0] != 0,
                        AltFrom = v[1].Value,
                        AltTo = v[2].Value,
                        Subs = (int)v[3].Value,
                        Kept = (int)v[4].Value,
                        Nights = (int)v[5].Value,
                        Rate = v[6].Value,
                        RateHigh = v[7].Value,
                        Worth = v[8].Value,
                        Usable = v[9] != 0,
                        Skips = v[10] != 0
                    });
                }
                return rows.Count == 0 ? AltKeepTable.Empty : new AltKeepTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
