using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TargetWindowStoreTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
            TargetWindowTable.Current = TargetWindowTable.Empty;
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
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_target_window'").Should().Be(1);
            Columns("ts_target_window").Should().Equal("target_guid", "start_utc", "end_utc", "priority", "event_id", "ignore_moon", "updated_at");
            db.Count("SELECT COUNT(*) FROM ts_target_window").Should().Be(0);
        }

        [Test]
        public void MissingOrEmptyTableLoadsEmpty() {
            using (var conn = db.Connect()) {
                TargetWindowStore.Load(conn).Should().BeSameAs(TargetWindowTable.Empty);
                TargetWindowStore.Ensure(conn);
            }
            using (var conn = db.Connect()) {
                TargetWindowStore.Load(conn).Should().BeSameAs(TargetWindowTable.Empty);
            }
        }

        [Test]
        public void ReadsWindowsAndSkipsBadRows() {
            using (var conn = db.Connect()) { TargetWindowStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_target_window (target_guid, start_utc, end_utc, priority, event_id, ignore_moon, updated_at) VALUES
('g13', '2026-10-13T00:30:00Z', '2026-10-14T12:00:00Z', 'high', 'mars-m-44-2026-10-13', 1, '2026-10-12T07:00:00Z'),
('G29', '2026-10-13 00:30:00.000', '2026-10-13 06:00:00.000', 'normal', NULL, 0, NULL),
('g30', '2026-10-13T00:30:00Z', '2026-10-13T06:00:00Z', 'urgent', NULL, 0, NULL),
('g40', 'soon', '2026-10-13T06:00:00Z', 'high', NULL, 0, NULL),
('g41', '2026-10-13T06:00:00Z', '2026-10-13T06:00:00Z', 'high', NULL, 0, NULL),
('g42', '2026-10-13T07:00:00Z', '2026-10-13T06:00:00Z', 'high', NULL, 0, NULL)");

            TargetWindowTable table;
            using (var conn = db.Connect()) { table = TargetWindowStore.Load(conn); }
            table.Count.Should().Be(3);

            var high = table.Get(" G13 ");
            high.StartUtc.Should().Be(new DateTime(2026, 10, 13, 0, 30, 0, DateTimeKind.Utc));
            high.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
            high.EndUtc.Should().Be(new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc));
            high.IsHigh.Should().BeTrue();
            high.EventId.Should().Be("mars-m-44-2026-10-13");
            high.IgnoreMoon.Should().BeTrue();
            high.UpdatedAt.Should().Be(new DateTime(2026, 10, 12, 7, 0, 0, DateTimeKind.Utc));

            var normal = table.Get("g29");
            normal.IsHigh.Should().BeFalse();
            normal.Priority.Should().Be("normal");
            normal.IgnoreMoon.Should().BeFalse();
            normal.EndUtc.Should().Be(new DateTime(2026, 10, 13, 6, 0, 0, DateTimeKind.Utc));

            table.Get("g30").Priority.Should().Be("normal");
            table.Get("g40").Should().BeNull();
            table.Get("g41").Should().BeNull();
            table.Get("g42").Should().BeNull();
            table.Get(null).Should().BeNull();
            table.Get("g12").Should().BeNull();
        }

        [Test]
        public void ABrokenTableThrowsSoTheCallerFallsBackToEmpty() {
            db.Exec("CREATE TABLE ts_target_window (target_guid TEXT)");
            using var conn = db.Connect();
            Action load = () => TargetWindowStore.Load(conn);
            load.Should().Throw<Exception>();
        }

        [Test]
        public void CurrentFallsBackToEmpty() {
            TargetWindowTable.Current = null;
            TargetWindowTable.Current.Should().BeSameAs(TargetWindowTable.Empty);
            TargetWindowTable.Current.Get("g13").Should().BeNull();
        }
    }
}
