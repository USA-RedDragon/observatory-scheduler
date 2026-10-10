using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class PlannerSignalTablesTest {
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
        public void EnsureAddsTheSignalColumnsOnce() {
            using (var conn = db.Connect()) {
                GoalSchema.Ensure(conn);
                GoalSchema.Ensure(conn);
                GoalSchema.EnsureColumns(conn);
            }

            using var c = db.Connect();
            c.Open();
            var columns = GoalSchema.Columns(c, GoalSchema.GoalProgressTable);
            columns.Should().Contain(new[] { "night_quality", "night_quality_at", "season_boost" });
        }

        [Test]
        public void EnsureColumnsUpgradesAnOldProgressTable() {
            db.Exec(@"CREATE TABLE ts_goal_progress (target_guid TEXT NOT NULL, filter TEXT NOT NULL, kind INTEGER, goal_value REAL, achieved_value REAL, progress REAL, snr REAL, depth REAL, effective_hours REAL, hours_needed REAL, gain_per_hour_pct REAL, plateau INTEGER, low_confidence INTEGER, done INTEGER, measured_at TEXT, PRIMARY KEY (target_guid, filter));
INSERT INTO ts_goal_progress (target_guid, filter, progress) VALUES ('t1', 'Ha', 0.5);");

            using (var conn = db.Connect()) { GoalSchema.EnsureColumns(conn); }

            db.Scalar("SELECT progress FROM ts_goal_progress WHERE target_guid = 't1'").Should().Be(0.5);
            db.Scalar("SELECT season_boost FROM ts_goal_progress WHERE target_guid = 't1'").Should().Be(DBNull.Value);
        }

        [Test]
        public void EnsureColumnsSkipsAMissingTable() {
            using var conn = db.Connect();
            GoalSchema.EnsureColumns(conn);
            db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_goal_progress'").Should().Be(0L);
        }

        [Test]
        public void ReadsSignalsWhenTheColumnsExist() {
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_goal_progress (target_guid, filter, progress, night_quality, night_quality_at, season_boost)
VALUES ('T1', 'Ha', 0.4, 0.8, '2026-10-09 04:10:00', 0.75), ('T1', 'OIII', 0.2, 0.6, '2026-10-09 02:00:00', 0.75), ('T2', 'L', 0.1, NULL, NULL, NULL);");

            using var c = db.Connect();
            var rows = new SqlGoalRowProvider(c).LoadProgress();
            rows.Should().HaveCount(3);
            var ha = rows.Single(r => r.Filter == "Ha");
            ha.NightQuality.Should().Be(0.8);
            ha.NightQualityAt.Should().Be(new DateTime(2026, 10, 9, 4, 10, 0, DateTimeKind.Utc));
            ha.SeasonBoost.Should().Be(0.75);
            rows.Single(r => r.Filter == "L").NightQualityAt.Should().BeNull();

            var signals = new PlannerSignals(rows);
            signals.SeasonBoost("t1").Should().Be(0.75);
            signals.SeasonBoost("T2").Should().Be(0);
            signals.Quality.Should().Be(0.8);
        }

        [Test]
        public void ReadsOldProgressTablesWithoutSignals() {
            db.Exec(@"CREATE TABLE ts_goal_progress (target_guid TEXT NOT NULL, filter TEXT NOT NULL, kind INTEGER, goal_value REAL, achieved_value REAL, progress REAL, snr REAL, depth REAL, effective_hours REAL, hours_needed REAL, gain_per_hour_pct REAL, plateau INTEGER, low_confidence INTEGER, done INTEGER, measured_at TEXT, PRIMARY KEY (target_guid, filter));
INSERT INTO ts_goal_progress (target_guid, filter, progress) VALUES ('t1', 'Ha', 0.5);");

            using var c = db.Connect();
            var rows = new SqlGoalRowProvider(c).LoadProgress();
            rows.Should().ContainSingle();
            rows[0].Progress.Should().Be(0.5);
            rows[0].SeasonBoost.Should().BeNull();
            rows[0].NightQuality.Should().BeNull();
        }
    }
}
