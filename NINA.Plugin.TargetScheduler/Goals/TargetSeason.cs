using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class TargetSeasonRow {
        public string TargetGuid { get; set; }
        public int NightsLeft { get; set; }
        public bool OutOfSeason { get; set; }
        public DateTime? SeasonEnd { get; set; }
        public DateTime ComputedFor { get; set; }
        public DateTime ComputedAt { get; set; }

        public static TargetSeasonRow From(string targetGuid, SeasonResult season, DateTime nightDate, DateTime computedAtUtc) {
            return new TargetSeasonRow {
                TargetGuid = targetGuid,
                NightsLeft = season.NightsLeft,
                OutOfSeason = season.OutOfSeason,
                SeasonEnd = season.OutOfSeason || season.Capped ? (DateTime?)null : nightDate.Date.AddDays(season.LastUsableNight),
                ComputedFor = nightDate.Date,
                ComputedAt = computedAtUtc
            };
        }
    }

    public interface ITargetSeasonWriter {

        void Upsert(TargetSeasonRow row);
    }

    public interface ITargetSeasonReader {

        IList<TargetSeasonRow> LoadAll();
    }
}
