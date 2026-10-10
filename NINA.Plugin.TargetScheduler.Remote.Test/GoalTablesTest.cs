using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class GoalTablesTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private long Tables() {
            return (long)db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('ts_goal', 'ts_goal_progress', 'ts_target_season')");
        }

        [Test]
        public void EnsureIsIdempotent() {
            using (var conn = db.Connect()) {
                GoalSchema.Ensure(conn);
                GoalSchema.Ensure(conn);
            }
            Tables().Should().Be(3);
        }

        [Test]
        public void CommandStoreSchemaIncludesGoalTables() {
            new SqlCommandStore(db.Connect).EnsureSchema();
            Tables().Should().Be(3);
        }

        [Test]
        public void MissingTablesMeanNoGoals() {
            using var conn = db.Connect();
            var source = new SqliteGoalProgressSource(new SqlGoalRowProvider(conn));
            source.Count.Should().Be(0);
            source.Get("x", "L").Should().BeNull();
        }

        [Test]
        public void GoalDefaultsComeFromTheSchema() {
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            db.Exec("INSERT INTO ts_goal (target_guid, filter) VALUES ('t1', 'Ha')");
            using var c = db.Connect();
            var goal = new SqlGoalRowProvider(c).LoadGoals();
            goal.Should().ContainSingle();
            goal[0].Kind.Should().Be(GoalKind.FaintSnr);
            goal[0].SnrGoal.Should().Be(10);
            goal[0].PlateauStop.Should().BeTrue();
            goal[0].DepthGoal.Should().BeNull();
        }

        [Test]
        public void ReadsProgressRows() {
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_goal (target_guid, filter, kind, snr_goal, depth_goal, plateau_stop) VALUES ('T1', 'Ha', 1, 10, 24.5, 0), ('T1', 'OIII', 0, 15, NULL, 1), ('T2', 'L', 0, 10, NULL, 1);
INSERT INTO ts_goal_progress (target_guid, filter, kind, goal_value, achieved_value, progress, snr, depth, effective_hours, hours_needed, gain_per_hour_pct, plateau, low_confidence, done, measured_at)
VALUES ('t1', 'ha ', 1, 24.5, 23.9, 0.82, NULL, 23.9, 6.5, 1.25, 3.5, 1, 0, 0, '2026-10-08T05:12:00Z'),
       ('T1', 'OIII', 0, 15, 16, '1.07', 16, NULL, 9, 0, 0.4, 0, 1, 1, 'garbage'),
       ('T3', 'L', 0, 10, 1, 0.1, 1, NULL, 1, 9, 10, 0, 0, 0, NULL);");

            using var c = db.Connect();
            var source = new SqliteGoalProgressSource(new SqlGoalRowProvider(c));
            source.Count.Should().Be(2);

            GoalProgress ha = source.Get("T1", "HA");
            ha.Kind.Should().Be(GoalKind.Depth);
            ha.GoalValue.Should().Be(24.5);
            ha.AchievedValue.Should().Be(23.9);
            ha.Progress.Should().Be(0.82);
            ha.EffectiveHours.Should().Be(6.5);
            ha.HoursNeeded.Should().Be(1.25);
            ha.GainPerHourPct.Should().Be(3.5);
            ha.Plateau.Should().BeTrue();
            ha.LowConfidence.Should().BeFalse();
            ha.Done.Should().BeFalse();
            ha.MeasuredAt.Should().Be(new DateTime(2026, 10, 8, 5, 12, 0, DateTimeKind.Utc));

            GoalProgress oiii = source.Get("t1", "oiii");
            oiii.Progress.Should().Be(1.07);
            oiii.Done.Should().BeTrue();
            oiii.LowConfidence.Should().BeTrue();
            oiii.MeasuredAt.Should().Be(DateTime.MinValue);

            source.Get("T2", "L").Should().BeNull();
            source.Get("T3", "L").Should().BeNull();
            source.ForTarget("t1").Should().HaveCount(2);
        }

        [Test]
        public void ReadsReadinessColumns() {
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_goal (target_guid, filter) VALUES ('T1', 'Ha'), ('T1', 'L'), ('T1', 'OIII');
INSERT INTO ts_goal_progress (target_guid, filter, kind, progress, done, state, reason, stack_subs, min_subs, sub_limit)
VALUES ('T1', 'Ha', 0, NULL, 0, 'collecting', 'insufficient data', 6, 8, 32),
       ('T1', 'L', 1, 0, 0, 'unmeasurable', 'no depth: 3 Gaia stars matched, 10 needed', 40, 8, 32);");

            using var c = db.Connect();
            var source = new SqliteGoalProgressSource(new SqlGoalRowProvider(c));
            GoalProgress ha = source.Get("T1", "Ha");
            ha.State.Should().Be(GoalState.Collecting);
            ha.Reason.Should().Be("insufficient data");
            ha.StackSubs.Should().Be(6);
            ha.MinSubs.Should().Be(8);
            GoalCompletion.IsComplete(ha, 100).Should().BeFalse();
            GoalProgress l = source.Get("T1", "L");
            l.State.Should().Be(GoalState.Unmeasurable);
            l.SubLimit.Should().Be(32);
            GoalCompletion.IsComplete(l, 0).Should().BeTrue();
            GoalProgress oiii = source.Get("T1", "OIII");
            oiii.State.Should().Be(GoalState.Pending);
            oiii.MinSubs.Should().Be(8);
        }

        [Test]
        public void EnsureAddsTheReadinessColumnsToAnOldTable() {
            db.Exec(@"CREATE TABLE ts_goal_progress (target_guid TEXT NOT NULL, filter TEXT NOT NULL, kind INTEGER, goal_value REAL, achieved_value REAL, progress REAL, snr REAL, depth REAL, effective_hours REAL, hours_needed REAL, gain_per_hour_pct REAL, plateau INTEGER, low_confidence INTEGER, done INTEGER, measured_at TEXT, PRIMARY KEY (target_guid, filter))");
            using var conn = db.Connect();
            GoalSchema.Ensure(conn);
            GoalSchema.Ensure(conn);
            GoalSchema.Columns(conn, "ts_goal_progress").Should().Contain(new[] { "state", "reason", "stack_subs", "min_subs", "sub_limit", "night_quality" });
        }

        [Test]
        public void SeasonUpsertKeepsOneRowPerTarget() {
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            var writer = new SqlTargetSeasonWriter(db.Connect);
            writer.Upsert(new TargetSeasonRow { TargetGuid = "t1", NightsLeft = 40, OutOfSeason = false, SeasonEnd = new DateTime(2026, 11, 17), ComputedFor = new DateTime(2026, 10, 9), ComputedAt = new DateTime(2026, 10, 9, 23, 0, 0, DateTimeKind.Utc) });
            writer.Upsert(new TargetSeasonRow { TargetGuid = "t1", NightsLeft = 0, OutOfSeason = true, SeasonEnd = null, ComputedFor = new DateTime(2026, 10, 10), ComputedAt = new DateTime(2026, 10, 10, 23, 0, 0, DateTimeKind.Utc) });
            writer.Upsert(new TargetSeasonRow { TargetGuid = "t2", NightsLeft = 365, ComputedFor = new DateTime(2026, 10, 10), ComputedAt = new DateTime(2026, 10, 10, 23, 0, 0, DateTimeKind.Utc) });

            db.Scalar("SELECT COUNT(*) FROM ts_target_season").Should().Be(2L);
            db.Scalar("SELECT nights_left FROM ts_target_season WHERE target_guid = 't1'").Should().Be(0L);
            db.Scalar("SELECT out_of_season FROM ts_target_season WHERE target_guid = 't1'").Should().Be(1L);
            db.Scalar("SELECT season_end FROM ts_target_season WHERE target_guid = 't1'").Should().Be(DBNull.Value);
            db.Scalar("SELECT computed_for FROM ts_target_season WHERE target_guid = 't1'").Should().Be("2026-10-10");
            db.Scalar("SELECT computed_at FROM ts_target_season WHERE target_guid = 't1'").Should().Be("2026-10-10T23:00:00Z");
        }

        [Test]
        public void SeasonReaderReturnsWrittenRows() {
            new SqlTargetSeasonReader(db.Connect).LoadAll().Should().BeEmpty();
            using (var conn = db.Connect()) { GoalSchema.Ensure(conn); }
            var writer = new SqlTargetSeasonWriter(db.Connect);
            writer.Upsert(new TargetSeasonRow { TargetGuid = "t1", NightsLeft = 40, SeasonEnd = new DateTime(2026, 11, 17), ComputedFor = new DateTime(2026, 10, 9), ComputedAt = DateTime.UtcNow });
            writer.Upsert(new TargetSeasonRow { TargetGuid = "t2", NightsLeft = 0, OutOfSeason = true, ComputedFor = new DateTime(2026, 10, 8), ComputedAt = DateTime.UtcNow });
            db.Exec("INSERT INTO ts_target_season (target_guid, nights_left, computed_for) VALUES ('bad', 3, 'not a date')");

            var rows = new SqlTargetSeasonReader(db.Connect).LoadAll();
            rows.Should().HaveCount(2);
            rows.Should().ContainSingle(r => r.TargetGuid == "t1" && r.NightsLeft == 40 && !r.OutOfSeason && r.ComputedFor == new DateTime(2026, 10, 9));
            rows.Should().ContainSingle(r => r.TargetGuid == "t2" && r.OutOfSeason && r.ComputedFor == new DateTime(2026, 10, 8));
        }

        [Test]
        public void SeasonUpsertWithoutTableThrowsForTheCallerToSwallow() {
            var writer = new SqlTargetSeasonWriter(db.Connect);
            Action act = () => writer.Upsert(new TargetSeasonRow { TargetGuid = "t1" });
            act.Should().Throw<Exception>();
        }
    }
}
