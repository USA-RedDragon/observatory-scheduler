using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public static class EventWindows {
        public const string ReasonBefore = "before its event window";
        public const string ReasonOutside = "outside its event window";
        public const string ReasonGapsOnly = "normal event yields to deep-sky targets";
        public const string ReasonHighEvent = "a high-priority event is ready";
        public const string ReasonNearerMiddle = "a high-priority event is nearer the middle of its window";
        public const string ReasonRunningHighEvent = "the running high-priority event is about as near the middle of its window";

        /// <summary>
        /// Ready high-priority events whose window phase is within this much of the nearest one go on to the score.
        /// </summary>
        public const double PhaseBand = 0.05;

        /// <summary>
        /// How much nearer the middle of its window another high-priority event must be to take over from a running one.
        /// </summary>
        public const double PreemptMargin = 0.05;

        private const int TierNormal = 0;
        private const int TierPlain = 1;
        private const int TierHigh = 2;

        public static string RejectReason(TargetWindowRow window, DateTime atTime) {
            if (window == null) { return null; }
            DateTime utc = TargetWindowTable.Utc(atTime);
            if (utc < window.StartUtc) { return ReasonBefore; }
            if (utc >= window.EndUtc) { return ReasonOutside; }
            return null;
        }

        public static bool Ended(TargetWindowRow window, DateTime atTime) {
            return window != null && TargetWindowTable.Utc(atTime) >= window.EndUtc;
        }

        public static (DateTime Start, DateTime End)? Clip(TargetWindowRow window, DateTime start, DateTime end) {
            if (window == null) { return (start, end); }
            DateTime windowStart = InKindOf(window.StartUtc, start);
            DateTime windowEnd = InKindOf(window.EndUtc, end);
            DateTime s = start > windowStart ? start : windowStart;
            DateTime e = end < windowEnd ? end : windowEnd;
            return s < e ? (s, e) : null;
        }

        public static DateTime InKindOf(DateTime utc, DateTime reference) {
            DateTime u = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return reference.Kind == DateTimeKind.Utc ? u : DateTime.SpecifyKind(u.ToLocalTime(), reference.Kind);
        }

        /// <summary>
        /// How far the time is from the middle of the window, as a share of half the window: 0 at the middle, 1 at either end.
        /// </summary>
        public static double Phase(TargetWindowRow window, DateTime atTime) {
            if (window == null) { return double.PositiveInfinity; }
            double half = (window.EndUtc - window.StartUtc).TotalSeconds / 2;
            if (half <= 0) { return 0; }
            DateTime middle = window.StartUtc.AddSeconds(half);
            return Math.Abs((TargetWindowTable.Utc(atTime) - middle).TotalSeconds) / half;
        }

        public static void Prefer<T>(List<T> ready, Func<T, TargetWindowRow> window, Action<T, string> reject) {
            Prefer(ready, window, reject, null, null);
        }

        public static void Prefer<T>(List<T> ready, Func<T, TargetWindowRow> window, Action<T, string> reject, Func<T, bool> running, DateTime? atTime) {
            if (ready == null || ready.Count < 2) { return; }
            List<int> tiers = ready.Select(t => Tier(window(t))).ToList();
            if (tiers.All(t => t == TierPlain)) { return; }
            int top = tiers.Max();
            string reason = top == TierHigh ? ReasonHighEvent : ReasonGapsOnly;
            List<T> keep = new List<T>(ready.Count);
            for (int i = 0; i < ready.Count; i++) {
                if (tiers[i] < top) {
                    reject?.Invoke(ready[i], reason);
                } else {
                    keep.Add(ready[i]);
                }
            }
            ready.Clear();
            ready.AddRange(keep);
            if (top == TierHigh && atTime.HasValue) {
                PreferNearerMiddle(ready, window, reject, running, atTime.Value);
            }
        }

        private static void PreferNearerMiddle<T>(List<T> ready, Func<T, TargetWindowRow> window, Action<T, string> reject, Func<T, bool> running, DateTime atTime) {
            if (ready.Count < 2) { return; }
            List<double> phases = ready.Select(t => Phase(window(t), atTime)).ToList();
            int run = running == null ? -1 : ready.FindIndex(t => running(t));
            bool hold = run >= 0 && !phases.Where((p, i) => i != run).Any(p => p < phases[run] - PreemptMargin);
            double best = phases.Min();
            List<T> keep = new List<T>(ready.Count);
            for (int i = 0; i < ready.Count; i++) {
                bool kept = hold ? i == run : i != run && phases[i] <= best + PhaseBand;
                if (kept) {
                    keep.Add(ready[i]);
                } else {
                    reject?.Invoke(ready[i], hold ? ReasonRunningHighEvent : ReasonNearerMiddle);
                }
            }
            ready.Clear();
            ready.AddRange(keep);
        }

        public static bool Preempts(TargetWindowRow running, TargetWindowRow candidate, DateTime atTime) {
            if (candidate == null || !candidate.IsHigh || !candidate.Contains(atTime)) { return false; }
            if (running == null || !running.IsHigh) { return true; }
            return Phase(candidate, atTime) < Phase(running, atTime) - PreemptMargin;
        }

        public static List<T> Preempting<T>(IEnumerable<T> targets, T running, Func<T, int> id, Func<T, TargetWindowRow> window, DateTime atTime) where T : class {
            List<T> result = new List<T>();
            if (targets == null || running == null) { return result; }
            TargetWindowRow runningWindow = window(running);
            int runningId = id(running);
            foreach (T t in targets) {
                if (t == null || ReferenceEquals(t, running) || id(t) == runningId) { continue; }
                if (Preempts(runningWindow, window(t), atTime)) { result.Add(t); }
            }
            return result;
        }

        public static int MinimumSeconds(TargetWindowRow window, int minimumSeconds, DateTime spanStart, DateTime spanEnd, DateTime atTime) {
            if (window == null) { return minimumSeconds; }
            DateTime at = TargetWindowTable.Utc(atTime);
            DateTime start = TargetWindowTable.Utc(spanStart);
            DateTime from = at > start ? at : start;
            int remaining = (int)Math.Floor((TargetWindowTable.Utc(spanEnd) - from).TotalSeconds);
            return Math.Max(1, Math.Min(minimumSeconds, remaining));
        }

        public static DateTime NotBefore(TargetWindowRow window, DateTime start) {
            if (window == null) { return start; }
            DateTime windowStart = InKindOf(window.StartUtc, start);
            return start < windowStart ? windowStart : start;
        }

        public static bool IsPriorityReason(string reason) {
            return reason == ReasonGapsOnly || reason == ReasonHighEvent || reason == ReasonNearerMiddle || reason == ReasonRunningHighEvent;
        }

        public static string PreemptedReason(string eventName) {
            return $"high-priority event {eventName} was ready";
        }

        private static int Tier(TargetWindowRow window) {
            return window == null ? TierPlain : window.IsHigh ? TierHigh : TierNormal;
        }
    }
}
