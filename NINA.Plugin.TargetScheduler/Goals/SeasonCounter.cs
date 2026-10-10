using System;

namespace NINA.Plugin.TargetScheduler.Goals {

    public interface INightUsability {

        double UsableMinutes(int night);
    }

    public class SeasonResult {
        public int NightsLeft { get; set; }
        public bool OutOfSeason { get; set; }
        public int LastUsableNight { get; set; }
        public bool Capped { get; set; }
        public int NightsScanned { get; set; }
    }

    public static class SeasonCounter {
        public const int MaxNights = 365;
        public const int GapNights = 14;

        public static bool IsUsable(double usableMinutes, int minimumMinutes) {
            return usableMinutes > 0 && usableMinutes >= minimumMinutes;
        }

        public static SeasonResult Count(INightUsability nights, int minimumMinutes, int maxNights = MaxNights, int gapNights = GapNights) {
            if (!IsUsable(nights.UsableMinutes(0), minimumMinutes)) {
                return new SeasonResult { NightsLeft = 0, OutOfSeason = true, LastUsableNight = -1, NightsScanned = 1 };
            }

            int usable = 1;
            int lastUsable = 0;
            int gap = 0;
            int night = 1;
            for (; night < maxNights; night++) {
                if (IsUsable(nights.UsableMinutes(night), minimumMinutes)) {
                    usable++;
                    lastUsable = night;
                    gap = 0;
                } else if (++gap >= gapNights) {
                    night++;
                    break;
                }
            }

            return new SeasonResult {
                NightsLeft = usable,
                OutOfSeason = false,
                LastUsableNight = lastUsable,
                Capped = night >= maxNights && gap < gapNights,
                NightsScanned = night
            };
        }
    }
}
