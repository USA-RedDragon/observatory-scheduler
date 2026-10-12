using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class BodyStoreTest {
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
        public void StartupCreatesBothTablesOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('ts_target_body', 'ts_body_position')").Should().Be(2);
            Columns("ts_target_body").Should().Equal("target_guid", "designation", "kind", "updated_at", "rotation_deg", "offset_frac");
            Columns("ts_body_position").Should().Equal("target_guid", "at", "designation", "night", "ra", "dec", "ra_app", "dec_app", "ra_rate", "dec_rate", "mag", "delta", "unc_ra", "unc_dec", "computed_at");
        }

        [Test]
        public void StartupAddsTheFramingColumnsToALegacyTableAndKeepsItsRows() {
            db.Exec(@"CREATE TABLE ts_target_body (target_guid TEXT NOT NULL PRIMARY KEY, designation TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'comet', updated_at TEXT);
INSERT INTO ts_target_body (target_guid, designation) VALUES ('g13', 'C/2025 R2')");
            using (var conn = db.Connect()) {
                TargetBodyStore.Load(conn).Get("g13").Frame.Should().BeNull();
            }
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            Columns("ts_target_body").Should().Equal("target_guid", "designation", "kind", "updated_at", "rotation_deg", "offset_frac");
            db.Exec("UPDATE ts_target_body SET rotation_deg = 84.06, offset_frac = -0.25");
            TargetBodyRow row;
            using (var conn = db.Connect()) { row = TargetBodyStore.Load(conn).Get("g13"); }
            row.Designation.Should().Be("C/2025 R2");
            row.Frame.RotationDeg.Should().Be(84.06);
            row.Frame.OffsetFrac.Should().Be(-0.25);
        }

        [Test]
        public void OutOfRangeFramingLoadsAsNoFraming() {
            using (var conn = db.Connect()) { TargetBodyStore.Ensure(conn); }
            db.Exec("INSERT INTO ts_target_body (target_guid, designation, rotation_deg, offset_frac) VALUES ('g1', '2P', 400, 0), ('g2', '2P', 10, NULL), ('g3', '2P', 10, 0.2)");
            TargetBodyTable table;
            using (var conn = db.Connect()) { table = TargetBodyStore.Load(conn); }
            table.Get("g1").Frame.Should().BeNull();
            table.Get("g2").Frame.Should().BeNull();
            table.Frame("g3", "2p").OffsetFrac.Should().Be(0.2);
            table.Frame("g3", "3P").Should().BeNull();
            table.Frame("g9", "2P").Should().BeNull();
        }

        [Test]
        public void MissingTablesLoadEmpty() {
            using var conn = db.Connect();
            TargetBodyStore.Load(conn).Should().BeSameAs(TargetBodyTable.Empty);
            BodyPositionStore.Load(conn).Should().BeSameAs(BodyPositionTable.Empty);
        }

        [Test]
        public void ReadsTargetBodies() {
            using (var conn = db.Connect()) { TargetBodyStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_target_body (target_guid, designation, kind, updated_at) VALUES
('g13', 'C/2025 R2', 'comet', '2026-10-12 01:00:00.000'),
('g29', '   ', 'comet', NULL),
('G40', ' DES=2P;CAP;NOFRAG ', 'comet', NULL)");

            TargetBodyTable table;
            using (var conn = db.Connect()) { table = TargetBodyStore.Load(conn); }
            table.Count.Should().Be(2);
            table.Designation("g13").Should().Be("C/2025 R2");
            table.Get("g13").Kind.Should().Be("comet");
            table.Get("g13").UpdatedAt.Should().Be(new DateTime(2026, 10, 12, 1, 0, 0, DateTimeKind.Utc));
            table.Designation("g29").Should().BeNull();
            table.Designation("g40").Should().Be("DES=2P;CAP;NOFRAG");
            table.Designation("g12").Should().BeNull();
            table.Designation(null).Should().BeNull();
        }

        [Test]
        public void ReadsPositionsAsSymmetricDsWritesThemAndIgnoresOtherDesignations() {
            using (var conn = db.Connect()) { BodyPositionStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_body_position (target_guid, at, designation, night, ra, dec, ra_app, dec_app, ra_rate, dec_rate, mag, delta, unc_ra, unc_dec, computed_at) VALUES
('g13', '2026-10-13 02:10:00.000', 'C/2025 R2', '2026-10-12', '210.5', '-5.25', '210.8', '-5.3', '12.5', '-3.25', '9.1', '0.65', NULL, NULL, '2026-10-12 18:00:00.000'),
('g13', '2026-10-13 02:00:00.000', 'c/2025 r2 ', '2026-10-12', 210.4, -5.2, NULL, NULL, 12.4, -3.2, NULL, NULL, 0.2, 0.3, '2026-10-12 18:00:00.000'),
('g13', '2026-10-13 02:20:00.000', 'C/2024 G3', '2026-10-12', 100, 10, NULL, NULL, 1, 1, NULL, NULL, NULL, NULL, '2026-10-11 18:00:00.000'),
('g13', '2026-10-13 02:30:00.000', 'C/2025 R2', '2026-10-12', 'n/a', -5.3, NULL, NULL, 12.6, -3.3, NULL, NULL, NULL, NULL, '2026-10-12 18:00:00.000'),
('g29', '2026-10-13 02:00:00.000', 'C/2025 R2', '2026-10-12', 1, 1, NULL, NULL, 1, 1, NULL, NULL, NULL, NULL, '2026-10-12 18:00:00.000')");

            BodyPositionTable table;
            using (var conn = db.Connect()) { table = BodyPositionStore.Load(conn); }
            table.Count.Should().Be(4);

            var rows = table.Rows("G13", " C/2025 R2");
            rows.Select(r => r.At).Should().Equal(new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 13, 2, 10, 0, DateTimeKind.Utc));
            rows[0].At.Kind.Should().Be(DateTimeKind.Utc);
            rows[0].UncRa.Should().Be(0.2);
            rows[0].RaApp.Should().BeNull();
            rows[1].Ra.Should().Be(210.5);
            rows[1].Dec.Should().Be(-5.25);
            rows[1].RaApp.Should().Be(210.8);
            rows[1].RaRate.Should().Be(12.5);
            rows[1].DecRate.Should().Be(-3.25);
            rows[1].Mag.Should().Be(9.1);
            rows[1].Delta.Should().Be(0.65);
            rows[1].Night.Should().Be("2026-10-12");
            rows[1].ComputedAt.Should().Be(new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc));

            table.Rows("g13", "C/2024 G3").Should().ContainSingle().Which.Ra.Should().Be(100);
            table.Rows("g13", null).Should().BeEmpty();
            table.Rows("g13", "C/2026 A1").Should().BeEmpty();
            table.Rows("g12", "C/2025 R2").Should().BeEmpty();
        }

        [Test]
        public void CurrentFallsBackToEmpty() {
            TargetBodyTable.Current = null;
            TargetBodyTable.Current.Should().BeSameAs(TargetBodyTable.Empty);
            BodyPositionTable.Current = null;
            BodyPositionTable.Current.Should().BeSameAs(BodyPositionTable.Empty);
        }
    }
}
