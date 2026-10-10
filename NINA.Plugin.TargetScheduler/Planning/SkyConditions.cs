using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Plugin.TargetScheduler.Planning {

    public static class SkyConditions {
        private static volatile ObserverInfo currentObserver;

        public static ObserverInfo CurrentObserver {
            get => currentObserver;
            set => currentObserver = value;
        }

        public static ObserverInfo Observer(IProfile profile) {
            return new ObserverInfo {
                Latitude = profile.AstrometrySettings.Latitude,
                Longitude = profile.AstrometrySettings.Longitude,
                Elevation = profile.AstrometrySettings.Elevation,
            };
        }

        public static double MoonGlare(ObserverInfo observer, Coordinates coordinates, DateTime atTime) {
            MoonSample moon = MoonEphemeris.At(observer, atTime);
            double separation = MoonAvoidance.Separation(moon.RaDegrees, moon.Dec, AstroUtil.HoursToDegrees(coordinates.RA), coordinates.Dec);
            return ConditionMatch.MoonGlare(moon.Altitude, ConditionMatch.MoonIllumination(moon.AgeDays), separation);
        }

        public static FilterCondition For(IExposure exposure, double glare) {
            return new FilterCondition {
                Filter = exposure.FilterName,
                Narrowband = FilterKind.IsNarrowband(exposure.FilterName),
                MoonGlare = glare
            };
        }

        public static double? NightQuality(DateTime atTime) {
            return PlannerSignals.Current.NightQuality(atTime.ToUniversalTime());
        }
    }
}
