using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class DarkStoreTest {
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
        public void StartupCreatesBothTablesOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            ((long)db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('ts_dark_need', 'ts_dark_taken')")).Should().Be(2);
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            var store = new DarkStore(db.Connect);
            store.LoadNeeds().Should().BeEmpty();
            store.Ensure();
            db.Exec(@"INSERT INTO ts_dark_need (combo_key, priority, exposure, gain, camera_offset, binning, readout_mode, set_temp, frames_needed, frames_have, frames_rejected, lights_blocked, lights_scaled, newest_dark_at, session_gap_hours, basis, computed_at) VALUES
('300|0|50|1|-3', 2, '300.0', '0.0', '50.0', '1.0', NULL, '-3.0', 3, 1, 1, 0, 1299, '2026-10-10 06:12:00.000', '36.0', 'b2', '2026-10-10 07:00:00.000'),
('300|0|50|1|-20', 1, 300, 0, 50, 1, 'Default', -20, 3, 0, 0, 1496, 0, NULL, 36, 'b1', '2026-10-10 07:00:00.000')");
            IList<DarkNeed> needs = store.LoadNeeds();
            needs.Should().HaveCount(2);
            needs[0].ComboKey.Should().Be("300|0|50|1|-20");
            needs[0].ReadoutMode.Should().Be("Default");
            needs[0].NewestDarkAt.Should().BeNull();
            needs[1].SetTemp.Should().Be(-3);
            needs[1].Offset.Should().Be(50);
            needs[1].NewestDarkAt.Should().Be(new DateTime(2026, 10, 10, 6, 12, 0, DateTimeKind.Utc));
            needs[1].NewestDarkAt.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Test]
        public void LedgerFeedsRemaining() {
            var store = new DarkStore(db.Connect);
            DateTime now = new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc);
            var need = new DarkNeed { ComboKey = "300|0|50|1|-3", Exposure = 300, SetTemp = -3, FramesNeeded = 3, FramesHave = 1, NewestDarkAt = now.AddHours(-1), SessionGapHours = 36 };
            store.RecordTaken(need, now.AddHours(-2), -3.1, 0.3, "A:\\NINA\\DARK\\x.xisf");
            store.RecordTaken(need, now.AddMinutes(-10), -3.0, 0.2, null);
            IList<DarkTaken> taken = store.LoadTaken(now.AddDays(-7));
            taken.Should().HaveCount(2);
            DarkPlanner.Remaining(need, taken, now).Should().Be(1);
            store.LoadTaken(now.AddHours(-1)).Should().HaveCount(1);
        }
    }
}
