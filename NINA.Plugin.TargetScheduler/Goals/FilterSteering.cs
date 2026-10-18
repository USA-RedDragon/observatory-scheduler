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
        public double? Worth { get; set; }
        public double? BroadbandWorth { get; set; }
        public double? AltWorth { get; set; }
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
            return c.Narrowband == ConditionMatch.NarrowbandFavored(c.MoonGlare, quality, c.BroadbandWorth);
        }

        public static void Evaluate(IList<SteeringCandidate> candidates, double? quality = null) {
            if (candidates == null || candidates.Count == 0) { return; }

            double maxGain = candidates.Where(c => c != null).Select(Gain).DefaultIfEmpty(0).Max();
            foreach (SteeringCandidate c in candidates) {
                if (c == null) { continue; }
                double gainNorm = maxGain > 0 ? GoalCompletion.Clamp01(Gain(c) / maxGain) : 1;
                double condition = ConditionMatch.MoonScore(c.Narrowband, c.MoonGlare, c.Worth) * ConditionMatch.SkyScore(c.Narrowband, quality) * AltKeep.Weight(c.AltWorth);
                c.Value = Deficit(c) * (0.5 + 0.5 * gainNorm) * condition * GoalCompletion.Clamp01(c.PanelMatch);
            }
        }

        /// <summary>
        /// True for a non-H-alpha filter whose goal is not measurable yet: the stacker still collects the subs its
        /// first goal and plateau measurement needs.
        /// </summary>
        public static bool Bootstrapping(SteeringCandidate c) {
            return c?.Goal != null && !c.Goal.Done && (c.Goal.State == GoalState.Collecting || c.Goal.State == GoalState.Pending)
                && !IsHydrogenAlpha(c.Filter);
        }

        public static bool IsHydrogenAlpha(string filter) {
            string k = MoonKeep.FilterKey(filter);
            if (k == null) { return false; }
            if (k == "ha" || k.StartsWith("halpha", StringComparison.Ordinal) || k.StartsWith("hydrogenalpha", StringComparison.Ordinal) || k.StartsWith("hα", StringComparison.Ordinal)) { return true; }
            return k.Length > 2 && k.StartsWith("ha", StringComparison.Ordinal) && char.IsDigit(k[2]);
        }

        /// <summary>
        /// While any non-H-alpha filter is bootstrapping, every open non-H-alpha filter takes its turn in plan order,
        /// starting at the current one, whatever the deficits, Moon, sky or kept rates favor. Returns -1 when no
        /// filter is bootstrapping. Call after Evaluate: a filter whose conditions value it at 0 sits out.
        /// </summary>
        public static int Bootstrap(IList<SteeringCandidate> candidates, int currentIndex) {
            List<SteeringCandidate> open = candidates.Where(c => c != null && c.Value > 0 && !IsHydrogenAlpha(c.Filter)).OrderBy(c => c.Index).ToList();
            if (!open.Any(Bootstrapping)) { return -1; }

            return (open.FirstOrDefault(c => c.Index >= currentIndex) ?? open[0]).Index;
        }

        public static int Choose(IList<SteeringCandidate> candidates, int currentIndex, double? quality = null) {
            if (!Applies(candidates)) { return -1; }

            Evaluate(candidates, quality);
            int bootstrap = Bootstrap(candidates, currentIndex);
            if (bootstrap >= 0) { return bootstrap; }

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
