using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.Trigger;
using System;
using System.Globalization;
using System.IO;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    public static class SequenceLimits {
        private static readonly Lazy<FlipState> flips = new Lazy<FlipState>(() => new FlipState(Path.Combine(Common.PLUGIN_HOME, "observatory-scheduler-flips.json")));

        public static FlipState Flips => flips.Value;

        private static readonly bool geometricSun = SunConditionModel.IsGeometric(typeof(SunAltitudeCondition).Assembly.GetName().Version);

        public static double ConditionSunAltitude(DateTime t, ObserverInfo observer) {
            double altitude = AstroUtil.GetSunAltitude(t, observer);
            if (geometricSun) {
                return altitude;
            }
            altitude += AstroUtil.ArcminToDegree(0.25);
            var location = new NOVAS.OnSurface { Latitude = observer.Latitude, Longitude = observer.Longitude };
            return altitude + NOVAS.Refract(ref location, NOVAS.RefractionOption.StandardRefraction, 90.0 - altitude);
        }

        public static ImagingLimit Find(ISequenceContainer start, IProfile profile, DateTime atTime) {
            var a = profile?.AstrometrySettings;
            if (start == null || a == null) {
                return null;
            }

            var observer = new ObserverInfo { Latitude = a.Latitude, Longitude = a.Longitude, Elevation = a.Elevation };
            double SunAltitude(DateTime t) => ConditionSunAltitude(t, observer);

            ImagingLimit limit = null;
            bool flipTrigger = false;
            for (ISequenceContainer c = start; c != null; c = c.Parent) {
                if (c is IConditionable conditionable) {
                    foreach (ISequenceCondition condition in conditionable.GetConditionsSnapshot()) {
                        if (condition is not SunAltitudeCondition sun || sun.Status == SequenceEntityStatus.DISABLED) {
                            continue;
                        }
                        bool endsAbove = sun.Data.Comparator == ComparisonOperatorEnum.GREATER_THAN || sun.Data.Comparator == ComparisonOperatorEnum.GREATER_THAN_OR_EQUAL;
                        double offset = SunConditionModel.EffectiveOffset(sun.Data.Offset, geometricSun);
                        DateTime? end = ImagingLimit.FindEnd(SunAltitude, offset, endsAbove, atTime, TimeSpan.FromHours(24));
                        if (!end.HasValue || (limit != null && limit.End <= end.Value)) {
                            continue;
                        }
                        limit = new ImagingLimit {
                            End = end.Value,
                            Basis = string.Format(CultureInfo.InvariantCulture,
                                "the Sun Altitude loop condition on '{0}' ends the loop when the sun is {1} {2:0.##}°",
                                c.Name, endsAbove ? "above" : "at or below", offset)
                        };
                    }
                }
                if (c is ITriggerable triggerable) {
                    foreach (ISequenceTrigger trigger in triggerable.GetTriggersSnapshot()) {
                        if (trigger is IMeridianFlipTrigger && trigger.Status != SequenceEntityStatus.DISABLED) {
                            flipTrigger = true;
                        }
                    }
                }
            }

            if (limit != null && flipTrigger && profile.MeridianFlipSettings != null) {
                var m = profile.MeridianFlipSettings;
                limit.Flip = new FlipSettings {
                    MinutesAfterMeridian = m.MinutesAfterMeridian,
                    MaxMinutesAfterMeridian = m.MaxMinutesAfterMeridian,
                    PauseTimeBeforeMeridian = m.PauseTimeBeforeMeridian,
                    UseSideOfPier = m.UseSideOfPier
                };
                limit.FlipOverhead = Flips.Overhead();
            }

            return limit;
        }

        public static string Describe(ImagingLimit limit) {
            if (limit == null) {
                return "no Sun Altitude loop condition encloses the Target Scheduler, so imaging ends at each exposure template's twilight";
            }
            string flip = limit.Flip == null
                ? "no Meridian Flip trigger encloses it"
                : limit.FlipOverhead.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "a NINA meridian flip counts as its wait to pass the meridian plus {0:0} s, the longest of the last {1} measured flips", limit.FlipOverhead.Value.TotalSeconds, FlipState.Keep)
                    : "a NINA meridian flip's slew, recenter and settle time has not been measured yet";
            return string.Format(CultureInfo.InvariantCulture, "the sequence ends imaging at {0:yyyy-MM-dd HH:mm:ss}: {1}; {2}", limit.End, limit.Basis, flip);
        }
    }
}
