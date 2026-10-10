using NINA.Astrometry;
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

        public int Samples { get; private set; }

        public SeasonVisibility(ObserverInfo observerInfo, Coordinates coordinates, HorizonDefinition horizon, TwilightLevel twilightLevel, DateTime firstNight, int sampleMinutes = DEFAULT_SAMPLE_MINUTES) {
            this.observerInfo = observerInfo;
            this.coordinates = coordinates;
            this.horizon = horizon;
            this.twilightLevel = twilightLevel;
            this.firstNight = firstNight.Date;
            this.sampleMinutes = sampleMinutes;
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
                HorizontalCoordinate hc = AstrometryUtils.GetHorizontalCoordinates(observerInfo, coordinates, at);
                bool visible = hc.Altitude - horizon.GetTargetAltitude(hc.Azimuth) > 0;

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
