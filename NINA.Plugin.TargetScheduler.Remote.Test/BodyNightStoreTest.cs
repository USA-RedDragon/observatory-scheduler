using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class BodyNightStoreTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private List<string> Columns(string table) {
            var columns = new List<string>();
            using var conn = db.Connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { columns.Add(r.GetString(1)); }
            return columns;
        }

        [Test]
        public void StartupCreatesTheTableOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_body_night'").Should().Be(1);
            Columns("ts_body_night").Should().Equal("target_guid", "night", "at", "designation", "ra", "dec", "ra_rate", "dec_rate", "mag", "mag_limit", "computed_at");
            db.Count("SELECT COUNT(*) FROM ts_body_night").Should().Be(0);
        }

        [Test]
        public void MissingTableLoadsEmpty() {
            using var conn = db.Connect();
            BodyNightStore.Load(conn).Should().BeSameAs(BodyNightTable.Empty);
            BodyNightStore.Version(conn).Should().Be("absent");
        }

        [Test]
        public void VersionChangesWhenRowsArriveChangeOrGo() {
            using (var conn = db.Connect()) { BodyNightStore.Ensure(conn); }
            string Version() { using var conn = db.Connect(); return BodyNightStore.Version(conn); }
            string empty = Version();
            empty.Should().Be("0|||");
            db.Exec("INSERT INTO ts_body_night VALUES ('G161', '2026-10-11', '2026-10-12 07:20:00.000', '161P', 1, 1, 1, 1, 9.5, 15, '2026-10-12 20:00:00.000')");
            string one = Version();
            one.Should().Be("1|2026-10-12 20:00:00.000|2026-10-11|2026-10-11");
            db.Exec("UPDATE ts_body_night SET mag = 16, computed_at = '2026-10-13 20:00:00.000'");
            Version().Should().NotBe(one);
            db.Exec("DELETE FROM ts_body_night");
            Version().Should().Be(empty);
        }

        [Test]
        public void LoadsRowsAsSymmetricDsWritesThem() {
            using (var conn = db.Connect()) { BodyNightStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_body_night VALUES ('G161', '2026-10-11', '2026-10-12 07:20:00.000', '161P', 322.96, 4.18, -307.23, 117.56, 9.56, 15, '2026-10-12 20:00:00.000');
INSERT INTO ts_body_night VALUES ('G161', '2026-10-12', '2026-10-13 07:20:00.000', '161P', 320.98, 4.95, -291.39, 111.66, 15.2, 15, '2026-10-12 20:00:00.000');
INSERT INTO ts_body_night VALUES ('G161', '2026-10-13', '2026-10-14 07:20:00.000', '161P', 319.10, 5.70, -280.00, 105.00, NULL, NULL, '2026-10-12 20:00:00.000');
INSERT INTO ts_body_night VALUES ('G161', '2026-10-14', 'not a time', '161P', 1, 1, 1, 1, NULL, NULL, '2026-10-12 20:00:00.000')");

            BodyNightTable table;
            using (var conn = db.Connect()) { table = BodyNightStore.Load(conn); }
            table.Count.Should().Be(3);
            BodyNightTrack track = table.Track("g161", "161P");
            track.Count.Should().Be(3);
            track.Bright(new DateTime(2026, 10, 11)).Should().BeTrue();
            track.Bright(new DateTime(2026, 10, 12)).Should().BeFalse();
            track.Bright(new DateTime(2026, 10, 13)).Should().BeTrue();
            track.HasNight(new DateTime(2026, 10, 14)).Should().BeFalse();
            BodyPosition? at = track.At(new DateTime(2026, 10, 13, 7, 20, 0, DateTimeKind.Utc));
            at.Should().NotBeNull();
            at.Value.Ra.Should().BeApproximately(320.98, 1e-9);
        }
    }
}
