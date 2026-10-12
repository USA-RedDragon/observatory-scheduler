using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class MoonKeepStoreTest {
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
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_moon_keep'").Should().Be(1);
            var columns = new List<string>();
            using (var conn = db.Connect())
            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "PRAGMA table_info(ts_moon_keep)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { columns.Add(r.GetString(1)); }
            }
            columns.Should().Equal("filter", "narrowband", "moon_down", "illum_bin", "alt_bin", "sep_bin", "illum_from", "illum_to", "alt_from", "alt_to", "sep_from", "sep_to",
                "subs", "kept", "nights", "rate", "rate_low", "rate_high", "worth", "usable", "skips", "computed_at");
        }

        [Test]
        public void MissingOrEmptyTableKeepsTheGlareRule() {
            using (var conn = db.Connect()) {
                MoonKeepStore.Load(conn).Should().BeSameAs(MoonKeepTable.Empty);
                MoonKeepStore.Ensure(conn);
                MoonKeepStore.Load(conn).Should().BeSameAs(MoonKeepTable.Empty);
            }
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            using (var conn = db.Connect()) { MoonKeepStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_moon_keep VALUES
('', '0', '0', '4', '2', '1', '0.9', '1.0', '35.0', '90.0', '45.0', '75.0', '676', '14', '34', '0.0198', '0.0071', '0.0275', '0.0253', '1', '1', '2026-10-12 19:00:00'),
('', 1, 1, -1, -1, -1, 0, 1, -90, 0, 0, 180, 2383, 1959, 240, 0.82, 0.8, 0.84, 1, 1, 0, '2026-10-12 19:00:00'),
('Red', 0, 0, 4, 2, 1, 0.9, 1, 35, 90, 45, 75, 190, 2, 33, 0.012, 0.004, 0.027, 0.015, 1, 1, '2026-10-12 19:00:00'),
('Bad', 0, 0, 4, 2, 1, 'x', 1, 35, 90, 45, 75, 1, 0, 1, 0.5, 0.1, 0.9, 0.6, 0, 0, '2026-10-12 19:00:00')");
            MoonKeepTable table;
            using (var conn = db.Connect()) { table = MoonKeepStore.Load(conn); }
            table.Count.Should().Be(3);
            var bright = new MoonState { Illumination = 0.95, Altitude = 50, Separation = 60 };
            MoonKeepRow red = table.Lookup("Red", bright);
            red.Filter.Should().Be("Red");
            red.Subs.Should().Be(190);
            red.Kept.Should().Be(2);
            red.Nights.Should().Be(33);
            red.RateHigh.Should().Be(0.027);
            MoonKeep.Skips(red, null).Should().BeTrue();
            MoonKeepRow lum = table.Lookup("Luminance", bright);
            lum.Filter.Should().Be("");
            lum.Worth.Should().Be(0.0253);
            table.Lookup("H-a", new MoonState { Illumination = 1, Altitude = -5, Separation = 10 }).MoonDown.Should().BeTrue();
            table.Lookup("H-a", bright).Should().BeNull();
        }
    }
}
