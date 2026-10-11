using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class SubLengthStoreTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        [Test]
        public void StartupCreatesTheTableOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_sub_length'").Should().Be(1);
            var columns = new List<string>();
            using (var conn = db.Connect())
            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "PRAGMA table_info(ts_sub_length)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { columns.Add(r.GetString(1)); }
            }
            columns.Should().Equal("target_guid", "filter", "gain", "bucket", "exposure", "ideal", "current", "sky_rate", "snr_change", "basis", "computed_at");
        }

        [Test]
        public void MissingTableFallsBack() {
            using var conn = db.Connect();
            SubLengthTable table = SubLengthStore.Load(conn);
            table.Should().BeSameAs(SubLengthTable.Empty);
            SubLength.Choose(table, "g13", "H-a", 100, 0.0, -1, 600).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "g13", "H-a", 100, 0.0, 300, 600).Source.Should().Be(SubLength.SourcePlan);
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            using (var conn = db.Connect()) { SubLengthStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_sub_length (target_guid, filter, gain, bucket, exposure, ideal, current, sky_rate, snr_change, basis, computed_at) VALUES
('g13', 'H-a', '100', 'dark', '300.0', '300.0', '600.0', '0.21', '-0.03', 'read noise share 4%', '2026-10-11 02:00:00.000'),
('g13', 'H-a', 100, 'bright', 120, 120, 600, 2.4, 0.01, 'sky limited', '2026-10-11 02:00:00.000'),
('g13', 'H-a', 0, 'dark', 0, NULL, NULL, NULL, NULL, NULL, '2026-10-11 02:00:00.000'),
('g29', 'L', 100, 'mid', 60, 60, 120, NULL, NULL, NULL, '2026-10-11 02:00:00.000')");

            SubLengthTable table;
            using (var conn = db.Connect()) { table = SubLengthStore.Load(conn); }
            table.Count.Should().Be(4);

            SubLengthRow dark = table.Get("g13", "H-a", 100, "dark");
            dark.Exposure.Should().Be(300);
            dark.Current.Should().Be(600);
            dark.SnrChange.Should().Be(-0.03);
            dark.Basis.Should().Be("read noise share 4%");
            dark.ComputedAt.Should().Be(new DateTime(2026, 10, 11, 2, 0, 0, DateTimeKind.Utc));

            SubLength.Choose(table, "g13", "H-a", 100, 0.01, -1, 600).Seconds.Should().Be(300);
            SubLength.Choose(table, "g13", "H-a", 100, 0.6, -1, 600).Seconds.Should().Be(120);
            SubLength.Choose(table, "g13", "H-a", 100, 0.3, -1, 600).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "g13", "H-a", 0, 0.01, -1, 600).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "g29", "L", 100, 0.3, 90, 120).Seconds.Should().Be(60);
            SubLength.Choose(table, "g29", "L", 100, 0.3, 90, 120).Source.Should().Be(SubLength.SourceSystem);
        }
    }
}
