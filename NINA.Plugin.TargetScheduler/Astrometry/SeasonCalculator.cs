using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
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
            return Key(observerInfo, coordinates, horizon, minimumTime, level, SeasonConstraints.None);
        }

        public static string Key(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, int minimumTime, TwilightLevel level, SeasonConstraints constraints) {
            return string.Join("|",
                coordinates.RA.ToString("0.00000", CultureInfo.InvariantCulture),
                coordinates.Dec.ToString("0.00000", CultureInfo.InvariantCulture),
                horizon.GetCacheKey(),
                minimumTime.ToString(CultureInfo.InvariantCulture),
                level.ToString(),
                observerInfo.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                observerInfo.Longitude.ToString("0.0000", CultureInfo.InvariantCulture),
                (constraints ?? SeasonConstraints.None).Key());
        }

        public static string BodyKey(ObserverInfo observerInfo, string designation, BodyNightTrack track, HorizonDefinition horizon, int minimumTime, TwilightLevel level, SeasonConstraints constraints) {
            return SeasonStore.BodyKey(BodySettingsKey(observerInfo, designation, horizon, minimumTime, level, constraints), (track ?? BodyNightTrack.Empty).Version);
        }

        public static string BodySettingsKey(ObserverInfo observerInfo, string designation, HorizonDefinition horizon, int minimumTime, TwilightLevel level, SeasonConstraints constraints) {
            return string.Join("|",
                "body",
                designation?.Trim().ToLowerInvariant() ?? "",
                horizon.GetCacheKey(),
                minimumTime.ToString(CultureInfo.InvariantCulture),
                level.ToString(),
                observerInfo.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                observerInfo.Longitude.ToString("0.0000", CultureInfo.InvariantCulture),
                (constraints ?? SeasonConstraints.None).Key());
        }

        public static SeasonResult ComputeBody(ObserverInfo observerInfo, BodyNightTrack track, HorizonDefinition horizon, TwilightLevel level, int minimumTime, DateTime nightDate, SeasonConstraints constraints) {
            track ??= BodyNightTrack.Empty;
            SeasonVisibility visibility = new SeasonVisibility(observerInfo, t => track.At(t), horizon, level, nightDate, constraints);
            return SeasonCounter.Count(new BodyNightUsability(visibility, track, nightDate), minimumTime);
        }

        public static DateTime NightDate(ObserverInfo observerInfo, DateTime atTime) {
            return TwilightCircumstances.AdjustTwilightCircumstances(observerInfo, atTime).OnDate.Date;
        }

        public static SeasonResult Compute(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel level, int minimumTime, DateTime nightDate) {
            return Compute(observerInfo, coordinates, horizon, level, minimumTime, nightDate, SeasonConstraints.None);
        }

        public static SeasonResult Compute(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel level, int minimumTime, DateTime nightDate, SeasonConstraints constraints) {
            SeasonVisibility visibility = new SeasonVisibility(observerInfo, coordinates, horizon, level, nightDate, constraints);
            return SeasonCounter.Count(visibility, minimumTime);
        }

        public static SeasonConstraints Constraints(double maximumAltitude, int meridianWindow, IEnumerable<MoonParams> moon) {
            return new SeasonConstraints {
                MaximumAltitude = maximumAltitude,
                MeridianWindowMinutes = meridianWindow,
                Moon = moon?.Where(m => m != null).ToList() ?? new List<MoonParams>()
            };
        }

        public static MoonParams Moon(bool enabled, double separation, int width, double relaxScale, double relaxMax, double relaxMin, bool downEnabled) {
            return new MoonParams {
                Enabled = enabled,
                Separation = separation,
                Width = width,
                RelaxScale = relaxScale,
                RelaxMaxAltitude = relaxMax,
                RelaxMinAltitude = relaxMin,
                DownEnabled = downEnabled
            };
        }
    }
}
