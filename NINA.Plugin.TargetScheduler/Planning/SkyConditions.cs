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

        public static MoonState Moon(ObserverInfo observer, Coordinates coordinates, DateTime atTime) {
            MoonSample moon = MoonEphemeris.At(observer, atTime);
            double separation = MoonAvoidance.Separation(moon.RaDegrees, moon.Dec, AstroUtil.HoursToDegrees(coordinates.RA), coordinates.Dec);
            return new MoonState { Illumination = ConditionMatch.MoonIllumination(moon.AgeDays), Altitude = moon.Altitude, Separation = separation };
        }

        public static FilterCondition For(IExposure exposure, double glare) {
            return new FilterCondition {
                Filter = exposure.FilterName,
                Narrowband = FilterKind.IsNarrowband(exposure.FilterName),
                MoonGlare = glare
            };
        }

        public static FilterCondition For(IExposure exposure, double glare, MoonState? moon) {
            FilterCondition condition = For(exposure, glare);
            if (moon.HasValue) {
                MoonKeepTable table = MoonKeepTable.Current;
                condition.Worth = MoonKeep.Worth(table.Lookup(exposure.FilterName, moon.Value));
                condition.BroadbandWorth = MoonKeep.Worth(table.Broadband(moon.Value));
            }
            return condition;
        }

        public static bool KeepApplies(ITarget target, IExposure exposure) {
            return exposure != null && exposure.IsManaged && target?.Window()?.IgnoreMoon != true;
        }

        public static MoonState? KeepState(ObserverInfo observer, Coordinates coordinates, DateTime atTime) {
            if (observer == null || coordinates == null || MoonKeepTable.Current.Count == 0) { return null; }
            return Moon(observer, coordinates, atTime);
        }

        public static double? NightQuality(DateTime atTime) {
            return PlannerSignals.Current.NightQuality(atTime.ToUniversalTime());
        }
    }
}
