using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class AltKeepStoreTest {
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
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_alt_keep'").Should().Be(1);
        }

        [Test]
        public void MissingOrEmptyTableWeighsNothing() {
            using (var conn = db.Connect()) {
                AltKeepStore.Load(conn).Should().BeSameAs(AltKeepTable.Empty);
                AltKeepStore.Ensure(conn);
                AltKeepStore.Load(conn).Should().BeSameAs(AltKeepTable.Empty);
            }
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            using (var conn = db.Connect()) { AltKeepStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_alt_keep VALUES
('', '0', '3', '15.0', '20.0', '259', '68', '61', '0.275', '0.21', '0.346', '0.33', '1', '0', '2026-10-12 19:00:00'),
('', 0, 8, 40, 90, 5060, 4240, 209, 0.837, 0.8, 0.876, 1, 1, 0, '2026-10-12 19:00:00'),
('Blue', 0, 3, 15, 20, 76, 0, 48, 0.0, 0.0, 0.03, 0, 1, 1, '2026-10-12 19:00:00'),
('Bad', 0, 3, 'x', 20, 1, 0, 1, 0.5, 0.1, 0.9, 0.6, 0, 0, '2026-10-12 19:00:00')");
            AltKeepTable table;
            using (var conn = db.Connect()) { table = AltKeepStore.Load(conn); }
            table.Count.Should().Be(3);
            AltKeepRow blue = table.Lookup("blue", 17);
            blue.Filter.Should().Be("Blue");
            blue.Kept.Should().Be(0);
            AltKeep.Skips(blue).Should().BeTrue();
            AltKeepRow lum = table.Lookup("Luminance", 17);
            lum.Filter.Should().Be("");
            lum.Worth.Should().Be(0.33);
            AltKeep.Skips(lum).Should().BeFalse();
            table.Lookup("Luminance", 90).Worth.Should().Be(1);
            table.Lookup("Luminance", 30).Should().BeNull();
        }
    }
}
