using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public static class EventWindows {
        public const string ReasonBefore = "before its event window";
        public const string ReasonOutside = "outside its event window";
        public const string ReasonGapsOnly = "normal event yields to deep-sky targets";
        public const string ReasonHighEvent = "a high-priority event is ready";

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

        public static void Prefer<T>(List<T> ready, Func<T, TargetWindowRow> window, Action<T, string> reject) {
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
        }

        public static bool Preempts(TargetWindowRow running, TargetWindowRow candidate, DateTime atTime) {
            return candidate != null && candidate.IsHigh && candidate.Contains(atTime) && (running == null || !running.IsHigh);
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
            return reason == ReasonGapsOnly || reason == ReasonHighEvent;
        }

        public static string PreemptedReason(string eventName) {
            return $"high-priority event {eventName} was ready";
        }

        private static int Tier(TargetWindowRow window) {
            return window == null ? TierPlain : window.IsHigh ? TierHigh : TierNormal;
        }
    }
}
