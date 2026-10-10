using System;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class GoalCompletion {
        public const double NotDoneCeiling = 99.9;

        public static double PercentComplete(GoalProgress goal) {
            if (goal.Done) { return 100; }
            return Math.Min(Clamp01(goal.Progress) * 100, NotDoneCeiling);
        }

        public static bool IsComplete(GoalProgress goal) {
            return IsComplete(goal, 0);
        }

        public static int Subs(GoalProgress goal, int localSubs) {
            return goal.StackSubs ?? Math.Max(0, localSubs);
        }

        public static bool IsComplete(GoalProgress goal, int localSubs) {
            if (goal.Done) { return true; }
            switch (goal.State) {
                case GoalState.Collecting:
                case GoalState.Pending:
                    return goal.MinSubs.HasValue && Subs(goal, localSubs) >= goal.MinSubs.Value;

                case GoalState.Unmeasurable:
                    return goal.SubLimit.HasValue && Subs(goal, localSubs) >= goal.SubLimit.Value;

                default:
                    return false;
            }
        }

        public static int RemainingExposures(GoalProgress goal, double exposureSeconds) {
            return RemainingExposures(goal, exposureSeconds, 0);
        }

        public static int RemainingExposures(GoalProgress goal, double exposureSeconds, int localSubs) {
            if (IsComplete(goal, localSubs)) { return 0; }
            switch (goal.State) {
                case GoalState.Collecting:
                case GoalState.Pending:
                    return goal.MinSubs.HasValue ? Math.Max(1, goal.MinSubs.Value - Subs(goal, localSubs)) : 1;

                case GoalState.Unmeasurable:
                    return goal.SubLimit.HasValue ? Math.Max(1, goal.SubLimit.Value - Subs(goal, localSubs)) : 1;
            }

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
