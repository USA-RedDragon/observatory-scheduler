using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SeasonStoreTest {
        private static readonly DateTime Today = new DateTime(2026, 10, 9);

        private static SeasonEntry Entry(string guid, string key, int nights, DateTime computedFor) {
            return new SeasonEntry { TargetGuid = guid, Key = key, Season = new SeasonResult { NightsLeft = nights }, ComputedFor = computedFor };
        }

        [Test]
        public void FindAcceptsTodayAndYesterdayOnly() {
            var store = new SeasonStore();
            store.Put(Entry("A", "k", 5, Today));
            store.Find("a", "k", Today).Season.NightsLeft.Should().Be(5);
            store.Find("a", "k", Today.AddDays(1)).Should().NotBeNull();
            store.Find("a", "k", Today.AddDays(2)).Should().BeNull();
            store.Find("a", "k", Today.AddDays(-1)).Should().BeNull();
            store.Find("b", "k", Today).Should().BeNull();
            store.Find(null, "k", Today).Should().BeNull();
        }

        [Test]
        public void FindRejectsAComputedEntryForOtherSettings() {
            var store = new SeasonStore();
            store.Put(Entry("a", "k1", 5, Today));
            store.Find("a", "k2", Today).Should().BeNull();
        }

        [Test]
        public void SeededRowsServeAnyKeyButNeverReplaceComputedEntries() {
            var store = new SeasonStore();
            store.Put(Entry("a", "k", 5, Today));
            store.Seed(new List<TargetSeasonRow> {
                new TargetSeasonRow { TargetGuid = "A", NightsLeft = 99, ComputedFor = Today },
                new TargetSeasonRow { TargetGuid = "b", NightsLeft = 30, OutOfSeason = false, ComputedFor = Today.AddDays(-1) },
                new TargetSeasonRow { TargetGuid = " ", NightsLeft = 1, ComputedFor = Today }
            });
            store.Seeded.Should().BeTrue();
            store.Find("a", "k", Today).Season.NightsLeft.Should().Be(5);
            SeasonEntry b = store.Find("b", "whatever", Today);
            b.FromDatabase.Should().BeTrue();
            b.Season.NightsLeft.Should().Be(30);
            store.Count.Should().Be(2);
        }

        [Test]
        public void SeedRunsOnce() {
            var store = new SeasonStore();
            store.Seed(new List<TargetSeasonRow> { new TargetSeasonRow { TargetGuid = "a", NightsLeft = 1, ComputedFor = Today } });
            store.Seed(new List<TargetSeasonRow> { new TargetSeasonRow { TargetGuid = "c", NightsLeft = 1, ComputedFor = Today } });
            store.Find("c", null, Today).Should().BeNull();
        }

        [Test]
        public void IsCurrentNeedsAComputedEntryForTheNightAndKey() {
            var store = new SeasonStore();
            store.Seed(new List<TargetSeasonRow> { new TargetSeasonRow { TargetGuid = "s", NightsLeft = 1, ComputedFor = Today } });
            store.Put(Entry("a", "k", 5, Today));
            store.IsCurrent("a", "k", Today).Should().BeTrue();
            store.IsCurrent("a", "k2", Today).Should().BeFalse();
            store.IsCurrent("a", "k", Today.AddDays(1)).Should().BeFalse();
            store.IsCurrent("s", null, Today).Should().BeFalse();
        }

        [TestCase("2026-10-09 07:30", "2026-10-08", false, true)]
        [TestCase("2026-10-09 07:30", "2026-10-09", false, false)]
        [TestCase("2026-10-09 03:00", "2026-10-08", false, false)]
        [TestCase("2026-10-09 03:00", "2026-10-07", true, false)]
        [TestCase("2026-10-09 03:00", null, false, true)]
        [TestCase("2026-10-09 09:00", "2026-10-08", true, false)]
        [TestCase("2026-10-09 11:00", "2026-10-08", true, true)]
        [TestCase("2026-10-09 13:59", "2026-10-08", true, true)]
        [TestCase("2026-10-09 14:00", "2026-10-08", true, false)]
        [TestCase("2026-10-09 22:00", "2026-10-09", true, false)]
        public void DailyPassSchedule(string now, string lastPass, bool imaging, bool due) {
            DateTime n = DateTime.Parse(now);
            DateTime night = n.Hour < 7 ? n.Date.AddDays(-1) : n.Date;
            DateTime? last = lastPass == null ? null : DateTime.Parse(lastPass);
            SeasonSchedule.DailyPassDue(n, night, last, imaging).Should().Be(due);
        }
    }
}
