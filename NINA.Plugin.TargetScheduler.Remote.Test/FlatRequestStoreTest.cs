using FluentAssertions;
using NINA.Plugin.TargetScheduler.Flats;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class FlatRequestStoreTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc);
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private static FlatRequest Request(string id, DateTime? requestedAt = null) {
            return new FlatRequest { RequestId = id, TargetId = 12, TargetName = "M 31", Night = "2026-10-09", RequestedAt = requestedAt ?? Now.AddHours(-5) };
        }

        [Test]
        public void StartupCreatesTheFlatRequestTablesOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            ((long)db.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('ts_flat_request', 'ts_flat_request_taken')")).Should().Be(2);
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            var store = new FlatRequestStore(db.Connect);
            store.LoadRequests().Should().BeEmpty();
            store.LoadTaken(Now.AddDays(-2)).Should().BeEmpty();
            store.Ensure();
            db.Exec(@"INSERT INTO ts_flat_request (request_id, target_id, target_name, night, filters, requested_at, computed_at) VALUES
('b-2', '29', 'Triangulum', ' 2026-10-08 ', NULL, '2026-10-10 20:00:00.000', '2026-10-10 21:00:00.000'),
('a-1', 12, 'M 31', '2026-10-09', 'Red,Green,Blue', '2026-10-10 18:30:00.000', '2026-10-10 21:00:00.000'),
('bad', 0, 'nobody', '2026-10-09', NULL, '2026-10-10 18:00:00.000', '2026-10-10 21:00:00.000')");
            IList<FlatRequest> requests = store.LoadRequests();
            requests.Should().HaveCount(2);
            requests[0].RequestId.Should().Be("a-1");
            requests[0].TargetId.Should().Be(12);
            requests[0].TargetName.Should().Be("M 31");
            requests[0].Night.Should().Be("2026-10-09");
            requests[0].Filters.Should().Be("Red,Green,Blue");
            requests[0].RequestedAt.Should().Be(new DateTime(2026, 10, 10, 18, 30, 0, DateTimeKind.Utc));
            requests[1].TargetId.Should().Be(29);
            requests[1].Night.Should().Be("2026-10-08");
            requests[1].Filters.Should().BeNull();
            requests[1].ComputedAt.Should().Be(new DateTime(2026, 10, 10, 21, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void LedgerRoundTrips() {
            var store = new FlatRequestStore(db.Connect);
            store.RecordTaken(Request("a-1"), Now.AddHours(-1), 3, null);
            store.RecordTaken(Request("b-2"), Now.AddHours(-40), 0, "no lights");
            store.LoadTaken(Now.AddHours(-36)).Should().ContainSingle().Which.Sets.Should().Be(3);
            IList<FlatRequestTaken> all = store.LoadTaken(Now.AddDays(-7));
            all.Should().HaveCount(2);
            db.Dump("ts_flat_request_taken", "id").Should().Be(
                "request_id=a-1|taken_at=2026-10-11 10:00:00.000|sets=3|note=null\n" +
                "request_id=b-2|taken_at=2026-10-09 19:00:00.000|sets=0|note=no lights");
        }

        [Test]
        public void ARequestIsDueUntilItHasALedgerRowInTheLast36Hours() {
            var requests = new List<FlatRequest> { Request("late", Now.AddHours(-1)), Request("early", Now.AddHours(-3)), Request("done"), Request("old"), null, new FlatRequest { RequestId = "" } };
            var taken = new List<FlatRequestTaken> {
                new FlatRequestTaken { RequestId = "done", TakenAt = Now.AddHours(-35.9), Sets = 2 },
                new FlatRequestTaken { RequestId = "old", TakenAt = Now.AddHours(-36.1), Sets = 0 },
                new FlatRequestTaken { RequestId = "other", TakenAt = Now.AddHours(-1), Sets = 1 },
                null
            };
            FlatRequestPlanner.Due(requests, taken, Now).ConvertAll(r => r.RequestId).Should().Equal("old", "early", "late");
            FlatRequestPlanner.Due(requests, null, Now).Should().HaveCount(4);
            FlatRequestPlanner.Due(null, taken, Now).Should().BeEmpty();
            FlatRequestPlanner.LedgerWindow.Should().Be(TimeSpan.FromHours(36));
        }

        [Test]
        public void TheLedgerFromTheStoreFeedsDue() {
            var store = new FlatRequestStore(db.Connect);
            var requests = new List<FlatRequest> { Request("a-1"), Request("b-2") };
            store.RecordTaken(requests[0], Now.AddMinutes(-10), 3, null);
            FlatRequestPlanner.Due(requests, store.LoadTaken(Now - FlatRequestPlanner.LedgerWindow), Now).Should().ContainSingle().Which.RequestId.Should().Be("b-2");
            FlatRequestPlanner.Due(requests, store.LoadTaken(Now.AddHours(40) - FlatRequestPlanner.LedgerWindow), Now.AddHours(40)).Should().HaveCount(2);
        }

        [Test]
        public void SameNightComparesTheLightSessionDate() {
            FlatRequestPlanner.SameNight(new DateTime(2026, 10, 9, 12, 0, 0), "2026-10-09").Should().BeTrue();
            FlatRequestPlanner.SameNight(new DateTime(2026, 10, 9, 12, 0, 0), " 2026-10-09 ").Should().BeTrue();
            FlatRequestPlanner.SameNight(new DateTime(2026, 10, 10, 12, 0, 0), "2026-10-09").Should().BeFalse();
            FlatRequestPlanner.SameNight(new DateTime(2026, 10, 9, 12, 0, 0), null).Should().BeFalse();
        }

        [Test]
        public void NotesSayWhyNoSetsWereTaken() {
            FlatRequestPlanner.NoLightsNote(Request("a-1")).Should().Be("Target Scheduler has no lights of target 12 (M 31) on the night of 2026-10-09, so no flats were taken for this request");
            FlatRequestPlanner.FailedNote(0, 3).Should().BeNull();
            FlatRequestPlanner.FailedNote(1, 3).Should().Be("1 of 3 light sessions got no flats; NINA's log has the reason");
        }
    }
}
