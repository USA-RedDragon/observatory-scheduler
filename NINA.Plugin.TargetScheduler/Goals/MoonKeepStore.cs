using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using NINA.Plugin.TargetScheduler.Darks;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class MoonKeepStore {
        public const string Table = "ts_moon_keep";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_moon_keep (
    filter TEXT NOT NULL,
    narrowband INTEGER NOT NULL,
    moon_down INTEGER NOT NULL,
    illum_bin INTEGER NOT NULL,
    alt_bin INTEGER NOT NULL,
    sep_bin INTEGER NOT NULL,
    illum_from REAL NOT NULL,
    illum_to REAL NOT NULL,
    alt_from REAL NOT NULL,
    alt_to REAL NOT NULL,
    sep_from REAL NOT NULL,
    sep_to REAL NOT NULL,
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
    PRIMARY KEY (filter, narrowband, illum_bin, alt_bin, sep_bin)
)";

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public static MoonKeepTable Load(DbConnection connection) {
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!TableExists(connection, Table)) { return MoonKeepTable.Empty; }
                List<MoonKeepRow> rows = new List<MoonKeepRow>();
                using DbCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT filter, narrowband, moon_down, illum_from, illum_to, alt_from, alt_to, sep_from, sep_to, subs, kept, nights, rate, rate_high, worth, usable, skips FROM ts_moon_keep";
                using DbDataReader r = cmd.ExecuteReader();
                while (r.Read()) {
                    double?[] v = Enumerable.Range(1, 16).Select(i => Real(r, i)).ToArray();
                    if (v.Any(x => !x.HasValue)) { continue; }
                    rows.Add(new MoonKeepRow {
                        Filter = Text(r, 0),
                        Narrowband = v[0] != 0,
                        MoonDown = v[1] != 0,
                        IllumFrom = v[2].Value,
                        IllumTo = v[3].Value,
                        AltFrom = v[4].Value,
                        AltTo = v[5].Value,
                        SepFrom = v[6].Value,
                        SepTo = v[7].Value,
                        Subs = (int)v[8].Value,
                        Kept = (int)v[9].Value,
                        Nights = (int)v[10].Value,
                        Rate = v[11].Value,
                        RateHigh = v[12].Value,
                        Worth = v[13].Value,
                        Usable = v[14] != 0,
                        Skips = v[15] != 0
                    });
                }
                return rows.Count == 0 ? MoonKeepTable.Empty : new MoonKeepTable(rows);
            } finally {
                if (opened) { connection.Close(); }
            }
        }
    }
}
