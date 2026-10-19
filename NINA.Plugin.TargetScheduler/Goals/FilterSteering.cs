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

        /// <summary>Identifies the target and filter for <see cref="ScoreGate"/>; null leaves the candidate without a memory.</summary>
        public string Key { get; set; }

        /// <summary>The poor-sky gate's decision with hysteresis from <see cref="ScoreGate"/>; null uses <see cref="FilterSteering.BelowCut"/> as is.</summary>
        public bool? Held { get; set; }
    }

    /// <summary>
    /// Hysteresis for the poor-sky gate, per target and filter. A filter is held out when its moon-scaled expected
    /// score is under the cut, and comes back only at <see cref="ReleaseMargin"/> or more over it. A decision stands
    /// for at least <see cref="MinHold"/> unless the margin is past the other side by <see cref="ReleaseMargin"/>
    /// (held out: twice the margin over the cut), and a held decision is kept for up to <see cref="MinHold"/> while
    /// the stacker's numbers are briefly unknown, as after a new night quality before the stacker rewrites the row.
    /// </summary>
    public class ScoreGate {
        public const double ReleaseMargin = 0.03;
        public static readonly TimeSpan MinHold = TimeSpan.FromMinutes(20);

        public static ScoreGate Current { get; } = new ScoreGate();

        private readonly object sync = new object();
        private readonly Dictionary<string, (bool Held, DateTime Since, DateTime Seen)> state = new Dictionary<string, (bool, DateTime, DateTime)>();

        /// <summary>
        /// Whether the candidate's filter sits out at <paramref name="atUtc"/>. With <paramref name="commit"/> false
        /// (previews and target scoring) the decision is read without being remembered.
        /// </summary>
        public bool Decide(SteeringCandidate c, double? quality, DateTime atUtc, bool commit) {
            double? margin = FilterSteering.CutMargin(c, quality);
            lock (sync) {
                bool known = c?.Key != null && state.ContainsKey(c.Key);
                (bool Held, DateTime Since, DateTime Seen) last = known ? state[c.Key] : default;
                if (!margin.HasValue) {
                    return known && last.Held && Elapsed(last.Seen, atUtc) <= MinHold;
                }
                bool held;
                if (!known) {
                    held = margin.Value < 0;
                } else if (last.Held) {
                    held = !(margin.Value >= 2 * ReleaseMargin || (margin.Value >= ReleaseMargin && Elapsed(last.Since, atUtc) >= MinHold));
                } else {
                    held = margin.Value < -ReleaseMargin || (margin.Value < 0 && Elapsed(last.Since, atUtc) >= MinHold);
                }
                if (commit && c?.Key != null) {
                    state[c.Key] = (held, !known || held != last.Held ? atUtc : last.Since, atUtc);
                }
                return held;
            }
        }

        public void Clear() {
            lock (sync) {
                state.Clear();
            }
        }

        private static TimeSpan Elapsed(DateTime from, DateTime to) {
            return to > from ? to - from : TimeSpan.Zero;
        }
    }

    public static class FilterSteering {
        public const double FreshGainPct = 100;
        public const double KeepCurrentWithin = 0.02;
        public const double ExpectedQualityTolerance = 0.025;

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
        /// True when the stacker expects a sub of this broadband filter taken now to fail its low-score cut: the
        /// score it expects at the current night quality is under the target's score cut. Always false for a
        /// narrowband filter, which the steering already favors on a poor sky. False while the night quality is
        /// unknown or stale, when the stacker has not published both numbers, or when its expected score was
        /// estimated for another night quality than the current one (a row it has not rewritten since).
        /// </summary>
        public static bool BelowCut(SteeringCandidate c, double? quality) {
            double? margin = CutMargin(c, quality);
            return margin.HasValue && margin.Value < 0;
        }

        /// <summary>
        /// The stacker's expected score for a broadband sub taken now, scaled by the filter's moon keep worth under the
        /// current Moon (the expected score pools subs of every Moon at about this night quality), minus its score cut.
        /// Null in every case <see cref="BelowCut"/> treats as unknown.
        /// </summary>
        public static double? CutMargin(SteeringCandidate c, double? quality) {
            if (!quality.HasValue || c?.Goal == null || c.Narrowband) { return null; }
            double? cut = c.Goal.ScoreCut;
            double? expected = c.Goal.ExpectedScore;
            double? at = c.Goal.ExpectedAtQuality;
            if (at.HasValue && !(Math.Abs(at.Value - quality.Value) <= ExpectedQualityTolerance)) { return null; }
            if (!cut.HasValue || !expected.HasValue || double.IsNaN(cut.Value) || double.IsNaN(expected.Value)) { return null; }
            double moon = c.Worth.HasValue && !double.IsNaN(c.Worth.Value) ? GoalCompletion.Clamp01(c.Worth.Value) : 1;
            return expected.Value * moon - cut.Value;
        }

        /// <summary>True when the poor-sky gate holds the candidate out: its <see cref="SteeringCandidate.Held"/> decision, else <see cref="BelowCut"/>.</summary>
        public static bool SitsOut(SteeringCandidate c, double? quality) {
            return c?.Held ?? BelowCut(c, quality);
        }

        /// <summary>
        /// While any non-H-alpha filter is bootstrapping, every open non-H-alpha filter takes its turn in plan order,
        /// starting at the current one, whatever the deficits, Moon, sky or kept rates favor. Returns -1 when no
        /// filter is bootstrapping. Call after Evaluate: a filter whose conditions value it at 0 sits out, and so
        /// does a filter whose subs the stacker expects to fail its score cut at this night quality. When every
        /// bootstrapping filter sits out, it returns -1 and the usual steering picks; the rotation resumes when
        /// the sky is good enough again.
        /// </summary>
        public static int Bootstrap(IList<SteeringCandidate> candidates, int currentIndex, double? quality = null) {
            List<SteeringCandidate> open = candidates.Where(c => c != null && c.Value > 0 && !IsHydrogenAlpha(c.Filter) && !SitsOut(c, quality)).OrderBy(c => c.Index).ToList();
            if (!open.Any(Bootstrapping)) { return -1; }

            return (open.FirstOrDefault(c => c.Index >= currentIndex) ?? open[0]).Index;
        }

        /// <summary>
        /// The candidate index Filter Steering takes, or -1 to leave the pick to the default order. A filter the poor-sky
        /// gate holds out is never taken; when only such filters have any value, it returns -1 and the target's
        /// Condition Match score is 0, so another target gets the time.
        /// </summary>
        public static int Choose(IList<SteeringCandidate> candidates, int currentIndex, double? quality = null) {
            if (!Applies(candidates)) { return -1; }

            Evaluate(candidates, quality);
            int bootstrap = Bootstrap(candidates, currentIndex, quality);
            if (bootstrap >= 0) { return bootstrap; }

            List<SteeringCandidate> passing = candidates.Where(c => c != null && !SitsOut(c, quality)).ToList();
            if (!passing.Any(c => c.Value > 0)) {
                if (candidates.Any(c => c != null && SitsOut(c, quality))) { return -1; }
                passing = candidates.Where(c => c != null).ToList();
            }

            List<SteeringCandidate> pool = passing.Where(c => c.Value > 0 && Favored(c, quality)).ToList();
            if (pool.Count == 0) { pool = passing; }

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
