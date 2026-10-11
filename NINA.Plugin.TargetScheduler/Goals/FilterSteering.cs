using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Goals {

    public class SteeringCandidate {
        public int Index { get; set; }
        public string Filter { get; set; }
        public GoalProgress Goal { get; set; }
        public double PercentComplete { get; set; }
        public bool Narrowband { get; set; }
        public double MoonGlare { get; set; }
        public double PanelMatch { get; set; } = 1;
        public double Value { get; set; }
    }

    public static class FilterSteering {
        public const double FreshGainPct = 100;
        public const double KeepCurrentWithin = 0.02;

        public static bool Applies(IEnumerable<SteeringCandidate> candidates) {
            return candidates != null && candidates.Any(c => c != null && c.Goal != null && !c.Goal.Done);
        }

        public static double Deficit(SteeringCandidate c) {
            if (c.Goal != null) {
                return c.Goal.Done ? 0 : 1 - GoalCompletion.Clamp01(c.Goal.Progress);
            }

            return 1 - GoalCompletion.Clamp01(c.PercentComplete / 100);
        }

        public static double Gain(SteeringCandidate c) {
            if (c.Goal == null) { return FreshGainPct; }
            if (c.Goal.Plateau) { return 0; }
            if (c.Goal.EffectiveHours <= 0 || c.Goal.GainPerHourPct <= 0 || double.IsNaN(c.Goal.GainPerHourPct)) { return FreshGainPct; }
            return c.Goal.GainPerHourPct;
        }

        public static bool Favored(SteeringCandidate c, double? quality) {
            return c.Narrowband == ConditionMatch.NarrowbandFavored(c.MoonGlare, quality);
        }

        public static void Evaluate(IList<SteeringCandidate> candidates, double? quality = null) {
            if (candidates == null || candidates.Count == 0) { return; }

            double maxGain = candidates.Where(c => c != null).Select(Gain).DefaultIfEmpty(0).Max();
            foreach (SteeringCandidate c in candidates) {
                if (c == null) { continue; }
                double gainNorm = maxGain > 0 ? GoalCompletion.Clamp01(Gain(c) / maxGain) : 1;
                double condition = ConditionMatch.MoonScore(c.Narrowband, c.MoonGlare) * ConditionMatch.SkyScore(c.Narrowband, quality);
                c.Value = Deficit(c) * (0.5 + 0.5 * gainNorm) * condition * GoalCompletion.Clamp01(c.PanelMatch);
            }
        }

        public static int Choose(IList<SteeringCandidate> candidates, int currentIndex, double? quality = null) {
            if (!Applies(candidates)) { return -1; }

            Evaluate(candidates, quality);
            List<SteeringCandidate> pool = candidates.Where(c => c != null && c.Value > 0 && Favored(c, quality)).ToList();
            if (pool.Count == 0) { pool = candidates.Where(c => c != null).ToList(); }

            SteeringCandidate best = null;
            foreach (SteeringCandidate c in pool) {
                if (best == null || c.Value > best.Value) { best = c; }
            }

            if (best == null || best.Value <= 0) { return -1; }

            SteeringCandidate current = pool.FirstOrDefault(c => c.Index == currentIndex);
            if (current != null && current.Value >= best.Value * (1 - KeepCurrentWithin)) {
                return current.Index;
            }

            return best.Index;
        }
    }
}
