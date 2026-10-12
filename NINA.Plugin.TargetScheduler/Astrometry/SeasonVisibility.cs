using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using System;

namespace NINA.Plugin.TargetScheduler.Astrometry {

    public class SeasonVisibility : INightUsability {
        public const int DEFAULT_SAMPLE_MINUTES = 10;
        public const int TWILIGHT_STRIDE_NIGHTS = 7;

        private readonly ObserverInfo observerInfo;
        private readonly Coordinates coordinates;
        private readonly HorizonDefinition horizon;
        private readonly TwilightLevel twilightLevel;
        private readonly DateTime firstNight;
        private readonly int sampleMinutes;
        private readonly SeasonConstraints constraints;
        private readonly double raDegrees;
        private readonly Func<DateTime, BodyPosition?> position;

        public int Samples { get; private set; }
        public int MoonSamples { get; private set; }

        public SeasonVisibility(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel twilightLevel, DateTime firstNight, int sampleMinutes = DEFAULT_SAMPLE_MINUTES)
            : this(observerInfo, coordinates, horizon, twilightLevel, firstNight, SeasonConstraints.None, sampleMinutes) {
        }

        public SeasonVisibility(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel twilightLevel, DateTime firstNight, SeasonConstraints constraints, int sampleMinutes = DEFAULT_SAMPLE_MINUTES) {
            this.observerInfo = observerInfo;
            this.coordinates = coordinates;
            this.horizon = horizon;
            this.twilightLevel = twilightLevel;
            this.firstNight = firstNight.Date;
            this.sampleMinutes = sampleMinutes;
            this.constraints = constraints ?? SeasonConstraints.None;
            this.raDegrees = AstroUtil.HoursToDegrees(coordinates.RA);
        }

        public SeasonVisibility(ObserverInfo observerInfo, Func<DateTime, BodyPosition?> position, HorizonDefinition horizon, TwilightLevel twilightLevel, DateTime firstNight, SeasonConstraints constraints, int sampleMinutes = DEFAULT_SAMPLE_MINUTES) {
            this.observerInfo = observerInfo;
            this.position = position ?? (_ => null);
            this.horizon = horizon;
            this.twilightLevel = twilightLevel;
            this.firstNight = firstNight.Date;
            this.sampleMinutes = sampleMinutes;
            this.constraints = constraints ?? SeasonConstraints.None;
        }

        public double UsableMinutes(int night) {
            TimeInterval span = DarkSpan(night);
            if (span == null || span.EndTime <= span.StartTime) { return 0; }

            double longest = 0;
            DateTime? runStart = null;
            DateTime t = span.StartTime;
            while (true) {
                bool last = t >= span.EndTime;
                DateTime at = last ? span.EndTime : t;
                Samples++;
                bool visible = Usable(at);

                if (visible && runStart == null) {
                    runStart = at;
                } else if (!visible && runStart != null) {
                    longest = Math.Max(longest, (at - runStart.Value).TotalMinutes);
                    runStart = null;
                }

                if (last) { break; }
                t = t.AddMinutes(sampleMinutes);
            }

            if (runStart != null) {
                longest = Math.Max(longest, (span.EndTime - runStart.Value).TotalMinutes);
            }

            return longest;
        }

        private bool Usable(DateTime at) {
            if (position == null) { return Usable(at, coordinates.RA, coordinates.Dec, raDegrees); }
            BodyPosition? p = position(at);
            if (!p.HasValue) { return false; }
            return Usable(at, AstroUtil.DegreesToHours(p.Value.Ra), p.Value.Dec, p.Value.Ra);
        }

        private bool Usable(DateTime at, double raHours, double dec, double raDeg) {
            double siderealTime = AstroUtil.GetLocalSiderealTime(at, observerInfo.Longitude);
            double hourAngle = AstroUtil.GetHourAngle(siderealTime, raHours);
            double altitude = AstroUtil.GetAltitude(AstroUtil.HoursToDegrees(hourAngle), observerInfo.Latitude, dec);
            double azimuth = AstroUtil.GetAzimuth(AstroUtil.HoursToDegrees(hourAngle), altitude, observerInfo.Latitude, dec);

            if (altitude - horizon.GetTargetAltitude(azimuth) <= 0) { return false; }
            if (!constraints.PositionUsable(altitude, hourAngle)) { return false; }
            if (!constraints.NeedsMoon) { return true; }

            MoonSamples++;
            return constraints.MoonAllows(MoonEphemeris.At(observerInfo, at), raDeg, dec);
        }

        private TimeInterval DarkSpan(int night) {
            int anchor = night - night % TWILIGHT_STRIDE_NIGHTS;
            TwilightCircumstances twilight = new TwilightCircumstances(observerInfo, firstNight.AddDays(anchor));
            TimeInterval span = twilight.GetTwilightSpan(twilightLevel);
            if (span == null || anchor == night) { return span; }
            int shift = night - anchor;
            return new TimeInterval(span.StartTime.AddDays(shift), span.EndTime.AddDays(shift));
        }
    }
}
