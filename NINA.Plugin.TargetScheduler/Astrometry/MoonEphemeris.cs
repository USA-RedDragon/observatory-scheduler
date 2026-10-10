using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Astrometry {

    public static class MoonEphemeris {
        private const int MaxEntries = 200000;
        private static readonly ConcurrentDictionary<string, MoonSample> cache = new ConcurrentDictionary<string, MoonSample>();

        public static MoonSample At(ObserverInfo observer, DateTime atTime) {
            DateTime minute = new DateTime(atTime.Ticks - atTime.Ticks % TimeSpan.TicksPerMinute, atTime.Kind);
            string key = string.Join("|",
                observer.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                observer.Longitude.ToString("0.0000", CultureInfo.InvariantCulture),
                minute.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
            if (cache.TryGetValue(key, out MoonSample hit)) { return hit; }

            NOVAS.SkyPosition pos = AstroUtil.GetMoonPosition(minute, AstroUtil.GetJulianDate(minute), observer);
            MoonSample sample = new MoonSample {
                Altitude = AstroUtil.GetMoonAltitude(minute, observer),
                AgeDays = AstrometryUtils.GetMoonAge(minute),
                RaDegrees = AstroUtil.HoursToDegrees(pos.RA),
                Dec = pos.Dec
            };

            if (cache.Count > MaxEntries) { cache.Clear(); }
            cache[key] = sample;
            return sample;
        }

        public static void Clear() {
            cache.Clear();
        }
    }
}
