using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class SeasonEntry {
        public string TargetGuid { get; set; }
        public string Key { get; set; }
        public SeasonResult Season { get; set; }
        public DateTime ComputedFor { get; set; }
        public bool FromDatabase { get; set; }
    }

    public class SeasonStore {
        private readonly ConcurrentDictionary<string, SeasonEntry> entries = new ConcurrentDictionary<string, SeasonEntry>();
        private readonly object seedLock = new object();
        private volatile bool seeded;

        public bool Seeded => seeded;

        public int Count => entries.Count;

        public void Put(SeasonEntry entry) {
            string guid = SqliteGoalProgressSource.NormalizeGuid(entry?.TargetGuid);
            if (guid == null) { return; }
            entries[guid] = entry;
        }

        public void Seed(IEnumerable<TargetSeasonRow> rows) {
            lock (seedLock) {
                if (seeded) { return; }
                foreach (TargetSeasonRow row in rows ?? Array.Empty<TargetSeasonRow>()) {
                    string guid = SqliteGoalProgressSource.NormalizeGuid(row.TargetGuid);
                    if (guid == null) { continue; }
                    entries.TryAdd(guid, new SeasonEntry {
                        TargetGuid = row.TargetGuid,
                        Season = new SeasonResult { NightsLeft = row.NightsLeft, OutOfSeason = row.OutOfSeason },
                        ComputedFor = row.ComputedFor.Date,
                        FromDatabase = true
                    });
                }
                seeded = true;
            }
        }

        public void MarkSeeded() {
            seeded = true;
        }

        public SeasonEntry Find(string targetGuid, string key, DateTime today) {
            string guid = SqliteGoalProgressSource.NormalizeGuid(targetGuid);
            if (guid == null || !entries.TryGetValue(guid, out SeasonEntry entry)) { return null; }
            if (!entry.FromDatabase && key != null && entry.Key != key) { return null; }
            DateTime d = entry.ComputedFor.Date;
            return d == today.Date || d == today.Date.AddDays(-1) ? entry : null;
        }

        public bool IsCurrent(string targetGuid, string key, DateTime nightDate) {
            string guid = SqliteGoalProgressSource.NormalizeGuid(targetGuid);
            return guid != null && entries.TryGetValue(guid, out SeasonEntry entry)
                && !entry.FromDatabase && entry.Key == key && entry.ComputedFor.Date == nightDate.Date;
        }

        public void Clear() {
            lock (seedLock) {
                entries.Clear();
                seeded = false;
            }
        }
    }

    public static class SeasonSchedule {
        public const int NoonWindowStartHour = 11;
        public const int NoonWindowEndHour = 14;

        public static bool DailyPassDue(DateTime nowLocal, DateTime nightDate, DateTime? lastPassNight, bool imaging) {
            if (lastPassNight.HasValue && lastPassNight.Value.Date == nightDate.Date) { return false; }
            if (!imaging) { return true; }
            return nowLocal.Hour >= NoonWindowStartHour && nowLocal.Hour < NoonWindowEndHour;
        }
    }
}
