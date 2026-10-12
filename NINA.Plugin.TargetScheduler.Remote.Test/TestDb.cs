using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    public sealed class TestDb : IDisposable {
        private readonly SqliteConnection keepAlive;
        public string ConnectionString { get; }

        private TestDb(string options) {
            ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared{options}";
            keepAlive = new SqliteConnection(ConnectionString);
            keepAlive.Open();
        }

        public static TestDb RealSchema() {
            var db = new TestDb(";Foreign Keys=False");
            var dir = Path.Combine(AppContext.BaseDirectory, "Schema");
            db.Exec(File.ReadAllText(Path.Combine(dir, "Initial", "initial_schema.sql")));
            foreach (var file in Directory.GetFiles(Path.Combine(dir, "Migrate"), "*.sql").OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f), CultureInfo.InvariantCulture))) {
                db.Exec(File.ReadAllText(file));
            }
            return db;
        }

        public TestDb() : this("") {
            Exec(@"CREATE TABLE project (Id INTEGER PRIMARY KEY, profileId TEXT NOT NULL, name TEXT NOT NULL, state INTEGER, priority INTEGER, minimumtime INTEGER, isMosaic INTEGER NOT NULL DEFAULT 0, flatsHandling INTEGER NOT NULL DEFAULT 0);
CREATE TABLE target (Id INTEGER PRIMARY KEY, name TEXT NOT NULL, active INTEGER NOT NULL, ra REAL, dec REAL, epochcode INTEGER NOT NULL DEFAULT 2, projectid INTEGER);
CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, profileId TEXT NOT NULL, exposure REAL NOT NULL, desired INTEGER, acquired INTEGER, accepted INTEGER, targetid INTEGER, exposureTemplateId INTEGER, enabled INTEGER);
INSERT INTO project (Id, profileId, name, state, priority, minimumtime) VALUES (5, 'p', 'Cygnis Loop', 1, 1, 90), (7, 'p', 'California', 1, 0, 60);
INSERT INTO target (Id, name, active, projectid) VALUES (12, 'Cygnis Loop Panel 1', 1, 5), (13, 'Cygnis Loop Panel 2', 1, 5);
INSERT INTO exposureplan (Id, profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled) VALUES (40, 'p', 300, 300, 350, 180, 13, 1, 1);");
        }

        public const string GoalTable = "CREATE TABLE IF NOT EXISTS ts_goal (target_guid TEXT NOT NULL, filter TEXT NOT NULL, kind INTEGER NOT NULL DEFAULT 0, snr_goal REAL NOT NULL DEFAULT 10, depth_goal REAL, plateau_stop INTEGER NOT NULL DEFAULT 1, region TEXT, updated_at TEXT, PRIMARY KEY (target_guid, filter))";

        public TestDb WithPlanning(bool goals = true) {
            Exec(@"ALTER TABLE project ADD COLUMN description TEXT;
ALTER TABLE project ADD COLUMN createdate INTEGER;
ALTER TABLE project ADD COLUMN activedate INTEGER;
ALTER TABLE project ADD COLUMN inactivedate INTEGER;
ALTER TABLE project ADD COLUMN minimumaltitude REAL;
ALTER TABLE project ADD COLUMN usecustomhorizon INTEGER;
ALTER TABLE project ADD COLUMN horizonoffset REAL;
ALTER TABLE project ADD COLUMN meridianwindow INTEGER;
ALTER TABLE project ADD COLUMN filterswitchfrequency INTEGER;
ALTER TABLE project ADD COLUMN ditherevery INTEGER;
ALTER TABLE project ADD COLUMN enablegrader INTEGER;
ALTER TABLE project ADD COLUMN maximumAltitude REAL DEFAULT 0;
ALTER TABLE project ADD COLUMN smartexposureorder INTEGER DEFAULT 0;
ALTER TABLE project ADD COLUMN guid TEXT;
ALTER TABLE target ADD COLUMN rotation REAL;
ALTER TABLE target ADD COLUMN roi REAL;
ALTER TABLE target ADD COLUMN unusedOEO TEXT;
ALTER TABLE target ADD COLUMN guid TEXT;
ALTER TABLE exposureplan ADD COLUMN guid TEXT;
CREATE TABLE exposuretemplate (Id INTEGER NOT NULL, profileId TEXT NOT NULL, name TEXT NOT NULL, filtername TEXT NOT NULL, gain INTEGER, ""offset"" INTEGER, bin INTEGER, readoutmode INTEGER, twilightlevel INTEGER,
  moonavoidanceenabled INTEGER, moonavoidanceseparation REAL, moonavoidancewidth INTEGER, maximumhumidity REAL, defaultexposure REAL DEFAULT 60, moonrelaxscale REAL DEFAULT 0, moonrelaxmaxaltitude REAL DEFAULT 5,
  moonrelaxminaltitude REAL DEFAULT -15, moondownenabled INTEGER DEFAULT 0, ditherevery INTEGER DEFAULT -1, minutesOffset INTEGER DEFAULT 0, guid TEXT, PRIMARY KEY(Id));
CREATE TABLE ruleweight (Id INTEGER NOT NULL, name TEXT NOT NULL, weight REAL NOT NULL, projectid INTEGER, PRIMARY KEY(Id));
CREATE TABLE acquiredimage (Id INTEGER NOT NULL, projectId INTEGER NOT NULL, targetId INTEGER NOT NULL, acquireddate INTEGER, filtername TEXT NOT NULL, gradingStatus INTEGER NOT NULL, metadata TEXT NOT NULL,
  rejectreason TEXT, profileId TEXT, exposureId INTEGER DEFAULT 0, guid TEXT, PRIMARY KEY(Id));
CREATE TABLE overrideexposureorderitem (Id INTEGER NOT NULL, targetid INTEGER NOT NULL, ""order"" INTEGER NOT NULL, action INTEGER NOT NULL, referenceIdx INTEGER, PRIMARY KEY(Id));
CREATE TABLE filtercadenceitem (Id INTEGER NOT NULL, targetid INTEGER NOT NULL, ""order"" INTEGER NOT NULL, next INTEGER, action INTEGER NOT NULL, referenceIdx INTEGER, PRIMARY KEY(Id));
CREATE TABLE flathistory (Id INTEGER NOT NULL, profileId TEXT NOT NULL, targetId INTEGER, lightSessionId INTEGER NOT NULL DEFAULT 0, lightSessionDate INTEGER, flatsTakenDate INTEGER, flatsType TEXT, filterName TEXT,
  gain INTEGER, ""offset"" INTEGER, bin INTEGER, readoutmode INTEGER, rotation REAL, roi REAL, PRIMARY KEY(Id));
UPDATE project SET guid = 'pg' || Id, minimumaltitude = 20, createdate = 1700000000, enablegrader = 1;
UPDATE target SET guid = 'g' || Id, rotation = 0, roi = 100;
UPDATE exposureplan SET guid = 'p' || Id;
INSERT INTO project (Id, profileId, name, state, priority, minimumtime, minimumaltitude, guid) VALUES (9, 'p', 'Triangulum', 1, 1, 60, 20, 'pg9');
INSERT INTO target (Id, name, active, ra, dec, epochcode, projectid, rotation, roi, guid) VALUES (29, 'Triangulum', 1, 1.564, 30.66, 2, 9, 0, 100, 'g29');
INSERT INTO exposureplan (Id, profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled, guid) VALUES
  (41, 'p', 300, 300, 0, 0, 13, 4, 1, 'p41'), (90, 'p', 120, 200, 20, 20, 29, 1, 1, 'p90');
INSERT INTO exposuretemplate (Id, profileId, name, filtername, gain, ""offset"", bin, readoutmode, twilightlevel, moonavoidanceenabled, moonavoidanceseparation, moonavoidancewidth, maximumhumidity, defaultexposure, guid) VALUES
  (1, 'p', 'Luminance', 'L', 100, 50, 1, -1, 0, 1, 45, 7, 0, 120, 't1'),
  (3, 'p', 'H-a', 'H-a', 100, 50, 1, -1, 0, 1, 45, 7, 0, 600, 't3'),
  (4, 'p', 'O-III', 'O-III', 100, 50, 1, -1, 0, 1, 45, 7, 0, 600, 't4');
INSERT INTO ruleweight (name, weight, projectid) VALUES
  ('Meridian Flip Penalty', 0, 5), ('Meridian Window Priority', 75, 5), ('Mosaic Completion', 0, 5), ('Percent Complete', 50, 5),
  ('Project Priority', 50, 5), ('Setting Soonest', 50, 5), ('Smart Exposure Order', 0, 5), ('Target Switch Penalty', 67, 5);
INSERT INTO filtercadenceitem (targetid, ""order"", next, action, referenceIdx) VALUES (29, 1, 1, 0, 0), (13, 1, 1, 0, 0);
INSERT INTO overrideexposureorderitem (targetid, ""order"", action, referenceIdx) VALUES (29, 1, 0, 0);");
            if (goals) {
                Exec(GoalTable);
                Exec("INSERT INTO ts_goal (target_guid, filter, kind, snr_goal, depth_goal, plateau_stop, region, updated_at) VALUES ('g13', 'H-a', 0, 10, NULL, 1, NULL, '2026-10-01T00:00:00Z')");
            }
            return this;
        }

        public string Dump(string table, params string[] skip) {
            using var cmd = keepAlive.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {table}";
            using var r = cmd.ExecuteReader();
            var rows = new List<string>();
            while (r.Read()) {
                var cells = new List<string>();
                for (int i = 0; i < r.FieldCount; i++) {
                    if (Array.IndexOf(skip, r.GetName(i)) < 0) {
                        cells.Add($"{r.GetName(i)}={(r.IsDBNull(i) ? "null" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture))}");
                    }
                }
                rows.Add(string.Join("|", cells));
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join("\n", rows);
        }

        public long Count(string sql) {
            return Convert.ToInt64(Scalar(sql));
        }

        public DbConnection Connect() {
            var c = new SqliteConnection(ConnectionString);
            c.Open();
            return c;
        }

        public void Exec(string sql) {
            using var cmd = keepAlive.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public object Scalar(string sql) {
            using var cmd = keepAlive.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        public void Dispose() {
            keepAlive.Dispose();
        }
    }
}
