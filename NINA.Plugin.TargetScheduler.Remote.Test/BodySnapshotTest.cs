using FluentAssertions;
using Microsoft.Data.Sqlite;
using NINA.Plugin.TargetScheduler.Bodies;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class BodySnapshotTest {
        private TestDb db;
        private List<string> warnings;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
            warnings = new List<string>();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
            BodySnapshot.Current = BodySnapshot.Empty;
        }

        private static BodySnapshot Previous() {
            return new BodySnapshot(
                new TargetBodyTable(new[] { new TargetBodyRow { TargetGuid = "g13", Designation = "C/2025 R2" } }),
                new BodyPositionTable(new[] { new BodyPositionRow { TargetGuid = "g13", Designation = "C/2025 R2", At = new System.DateTime(2026, 10, 13, 2, 0, 0, System.DateTimeKind.Utc), Ra = 1, Dec = 2 } }));
        }

        private void Good() {
            using (var conn = db.Connect()) { BodySchema.TryEnsure(conn, warnings.Add).Should().BeTrue(); }
            db.Exec(@"INSERT INTO ts_target_body (target_guid, designation) VALUES ('g13', 'C/2024 G3');
INSERT INTO ts_body_position (target_guid, at, designation, night, ra, dec, ra_rate, dec_rate, computed_at) VALUES ('g13', '2026-10-13 02:00:00.000', 'C/2024 G3', '2026-10-12', 5, 6, 1, 1, '2026-10-12 18:00:00.000')");
        }

        [Test]
        public void LoadsBothTablesIntoOneSnapshot() {
            Good();
            BodySnapshot s;
            using (var conn = db.Connect()) { s = BodySchema.Load(conn, Previous(), warnings.Add); }
            s.Targets.Designation("g13").Should().Be("C/2024 G3");
            s.Positions.Ephemeris("g13", "C/2024 G3").Count.Should().Be(1);
            warnings.Should().BeEmpty();
        }

        [Test]
        public void ATargetTableFailureKeepsThePreviousSnapshot() {
            db.Exec("CREATE TABLE ts_target_body (target_guid TEXT)");
            BodySnapshot previous = Previous();
            BodySnapshot s;
            using (var conn = db.Connect()) { s = BodySchema.Load(conn, previous, warnings.Add); }
            s.Should().BeSameAs(previous);
            s.Targets.Designation("g13").Should().Be("C/2025 R2");
            warnings.Should().ContainSingle().Which.Should().Contain("keeping the previous");
        }

        [Test]
        public void APositionFailureStillPublishesTheBodiesWithoutPositions() {
            db.Exec("CREATE TABLE ts_target_body (target_guid TEXT, designation TEXT, kind TEXT, updated_at TEXT); INSERT INTO ts_target_body VALUES ('g13', 'C/2024 G3', 'comet', NULL); CREATE TABLE ts_body_position (target_guid TEXT)");
            BodySnapshot s;
            using (var conn = db.Connect()) { s = BodySchema.Load(conn, Previous(), warnings.Add); }
            s.Targets.Designation("g13").Should().Be("C/2024 G3");
            s.Positions.Should().BeSameAs(BodyPositionTable.Empty);
            s.Positions.Ephemeris("g13", "C/2024 G3").Count.Should().Be(0);
            warnings.Should().ContainSingle().Which.Should().Contain("not imaged");
        }

        [Test]
        public void TablesPublishTogether() {
            BodySnapshot.Current = Previous();
            TargetBodyTable.Current.Designation("g13").Should().Be("C/2025 R2");
            BodyPositionTable.Current.Count.Should().Be(1);
            TargetBodyTable.Current = null;
            BodySnapshot.Current.Targets.Should().BeSameAs(TargetBodyTable.Empty);
            BodySnapshot.Current.Positions.Count.Should().Be(1);
            BodySnapshot.Current = null;
            BodySnapshot.Current.Should().BeSameAs(BodySnapshot.Empty);
        }

        [Test]
        public void EnsureFailureIsReportedNotThrown() {
            using var conn = new SqliteConnection("Data Source=/nonexistent-dir/nowhere/ts.sqlite;Mode=ReadOnly");
            BodySchema.TryEnsure(conn, warnings.Add).Should().BeFalse();
            warnings.Should().ContainSingle().Which.Should().Contain("ts_target_body");
        }
    }
}
