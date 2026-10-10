using System;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class GoalCompletion {
        public const double NotDoneCeiling = 99.9;

        public static double PercentComplete(GoalProgress goal) {
            if (goal.Done) { return 100; }
            return Math.Min(Clamp01(goal.Progress) * 100, NotDoneCeiling);
        }

        public static bool IsComplete(GoalProgress goal) {
            return goal.Done;
        }

        public static int RemainingExposures(GoalProgress goal, double exposureSeconds) {
            if (goal.Done) { return 0; }
            if (exposureSeconds > 0 && IsFinite(goal.HoursNeeded) && goal.HoursNeeded > 0) {
                double n = Math.Ceiling(goal.HoursNeeded * 3600 / exposureSeconds);
                if (n >= 100000) { return 100000; }
                return Math.Max(1, (int)n);
            }

            return 1;
        }

        public static double Clamp01(double value) {
            if (double.IsNaN(value)) { return 0; }
            if (value < 0) { return 0; }
            if (value > 1) { return 1; }
            return value;
        }

        private static bool IsFinite(double value) {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
