using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Astrometry {

    public static class SeasonCalculator {

        public static TwilightLevel LevelFor(IEnumerable<TwilightLevel> levels) {
            List<TwilightLevel> list = levels?.ToList() ?? new List<TwilightLevel>();
            return list.Count == 0 ? TwilightLevel.Astronomical : list.Max();
        }

        public static string Key(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, int minimumTime, TwilightLevel level) {
            return string.Join("|",
                coordinates.RA.ToString("0.00000", CultureInfo.InvariantCulture),
                coordinates.Dec.ToString("0.00000", CultureInfo.InvariantCulture),
                horizon.GetCacheKey(),
                minimumTime.ToString(CultureInfo.InvariantCulture),
                level.ToString(),
                observerInfo.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                observerInfo.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
        }

        public static DateTime NightDate(ObserverInfo observerInfo, DateTime atTime) {
            return TwilightCircumstances.AdjustTwilightCircumstances(observerInfo, atTime).OnDate.Date;
        }

        public static SeasonResult Compute(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel level, int minimumTime, DateTime nightDate) {
            SeasonVisibility visibility = new SeasonVisibility(observerInfo, coordinates, horizon, level, nightDate);
            return SeasonCounter.Count(visibility, minimumTime);
        }
    }
}
