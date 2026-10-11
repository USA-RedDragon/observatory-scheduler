using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class BiasStoreTest {
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
        public void StartupCreatesTheBiasTablesOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            ((long)db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('ts_bias_need', 'ts_bias_taken')")).Should().Be(2);
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            var store = new BiasStore(db.Connect);
            store.LoadNeeds().Should().BeEmpty();
            store.LoadTaken(DateTime.UtcNow.AddDays(-7)).Should().BeEmpty();
            store.Ensure();
            db.Exec(@"INSERT INTO ts_bias_need (combo_key, priority, gain, camera_offset, binning, readout_mode, frames_needed, frames_have, newest_bias_at, session_gap_hours, basis, computed_at) VALUES
('bias|100|50|1', 2, '100.0', '50.0', '1.0', NULL, 30, 4, '2025-02-05 03:10:00.000', '36.0', 'stale', '2026-10-10 07:00:00.000'),
('bias|0|240|1', 0, 0, 240, 1, 'High Gain', 30, 0, NULL, 36, 'requested', '2026-10-10 07:00:00.000'),
('', 1, 0, 0, 1, NULL, 30, 0, NULL, 36, NULL, '2026-10-10 07:00:00.000')");
            IList<BiasNeed> needs = store.LoadNeeds();
            needs.Should().HaveCount(2);
            needs[0].ComboKey.Should().Be("bias|0|240|1");
            needs[0].Priority.Should().Be(0);
            needs[0].Offset.Should().Be(240);
            needs[0].ReadoutMode.Should().Be("High Gain");
            needs[0].NewestBiasAt.Should().BeNull();
            needs[1].Gain.Should().Be(100);
            needs[1].Binning.Should().Be(1);
            needs[1].FramesNeeded.Should().Be(30);
            needs[1].FramesHave.Should().Be(4);
            needs[1].SessionGapHours.Should().Be(36);
            needs[1].Basis.Should().Be("stale");
            needs[1].NewestBiasAt.Should().Be(new DateTime(2025, 2, 5, 3, 10, 0, DateTimeKind.Utc));
            needs[1].ComputedAt.Should().Be(new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void LedgerFeedsRemaining() {
            var store = new BiasStore(db.Connect);
            DateTime now = new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc);
            var need = new BiasNeed { ComboKey = "bias|100|50|1", Gain = 100, Offset = 50, FramesNeeded = 30, FramesHave = 26, NewestBiasAt = now.AddHours(-1), SessionGapHours = 36 };
            store.RecordTaken(need, now.AddHours(-2), 0.000032, -10.1, "A:\\NINA\\BIAS\\x.xisf");
            store.RecordTaken(need, now.AddMinutes(-10), 0.000032, double.NaN, null);
            store.RecordTaken(need, now.AddMinutes(-9), 0, null, null);
            IList<BiasTaken> taken = store.LoadTaken(now.AddDays(-7));
            taken.Should().HaveCount(3);
            BiasPlanner.Remaining(need, taken, now).Should().Be(2);
            store.LoadTaken(now.AddHours(-1)).Should().HaveCount(2);
            db.Dump("ts_bias_taken", "id").Should().Be(
                "combo_key=bias|100|50|1|taken_at=2026-10-10 05:00:00.000|exposure=3.2E-05|sensor_temp=-10.1|path=A:\\NINA\\BIAS\\x.xisf\n" +
                "combo_key=bias|100|50|1|taken_at=2026-10-10 06:50:00.000|exposure=3.2E-05|sensor_temp=null|path=null\n" +
                "combo_key=bias|100|50|1|taken_at=2026-10-10 06:51:00.000|exposure=0|sensor_temp=null|path=null");
        }
    }
}
