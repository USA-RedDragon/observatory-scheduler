using Microsoft.Data.Sqlite;
using System;
using System.Data.Common;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    public sealed class TestDb : IDisposable {
        private readonly SqliteConnection keepAlive;
        public string ConnectionString { get; }

        public TestDb() {
            ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            keepAlive = new SqliteConnection(ConnectionString);
            keepAlive.Open();
            Exec(@"CREATE TABLE project (Id INTEGER PRIMARY KEY, profileId TEXT NOT NULL, name TEXT NOT NULL, state INTEGER, priority INTEGER, minimumtime INTEGER, isMosaic INTEGER NOT NULL DEFAULT 0, flatsHandling INTEGER NOT NULL DEFAULT 0);
CREATE TABLE target (Id INTEGER PRIMARY KEY, name TEXT NOT NULL, active INTEGER NOT NULL, ra REAL, dec REAL, epochcode INTEGER NOT NULL DEFAULT 2, projectid INTEGER);
CREATE TABLE exposureplan (Id INTEGER PRIMARY KEY, profileId TEXT NOT NULL, exposure REAL NOT NULL, desired INTEGER, acquired INTEGER, accepted INTEGER, targetid INTEGER, exposureTemplateId INTEGER, enabled INTEGER);
INSERT INTO project (Id, profileId, name, state, priority, minimumtime) VALUES (5, 'p', 'Cygnis Loop', 1, 1, 90), (7, 'p', 'California', 1, 0, 60);
INSERT INTO target (Id, name, active, projectid) VALUES (12, 'Cygnis Loop Panel 1', 1, 5), (13, 'Cygnis Loop Panel 2', 1, 5);
INSERT INTO exposureplan (Id, profileId, exposure, desired, acquired, accepted, targetid, exposureTemplateId, enabled) VALUES (40, 'p', 300, 300, 350, 180, 13, 1, 1);");
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
