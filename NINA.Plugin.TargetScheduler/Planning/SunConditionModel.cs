using System;

namespace NINA.Plugin.TargetScheduler.Planning {

    public static class SunConditionModel {
        private static readonly Version GeometricSince = new Version(3, 2);

        public static bool IsGeometric(Version nina) => nina != null && nina >= GeometricSince;

        public static double EffectiveOffset(double offset, bool geometric) => geometric && offset == 0.0 ? -0.833 : offset;
    }
}
