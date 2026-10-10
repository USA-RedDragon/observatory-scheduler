using NINA.Core.Enum;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public static class ActivityProbe {
        private const int MaxDepth = 16;

        public static IReadOnlyList<RunningStep> Running(ISequenceContainer plan, IEnumerable<ISequenceContainer> extra) {
            try {
                var triggers = RunningTrigger(plan);
                if (triggers.Count > 0) {
                    return triggers;
                }
                var chain = new List<RunningStep>();
                if (plan != null && Items(plan, chain, 0)) {
                    return chain;
                }
                foreach (var c in extra ?? Enumerable.Empty<ISequenceContainer>()) {
                    chain.Clear();
                    if (c != null && Items(c, chain, 0)) {
                        return chain;
                    }
                }
            } catch (Exception) {
            }
            return Array.Empty<RunningStep>();
        }

        private static List<RunningStep> RunningTrigger(ISequenceContainer plan) {
            var chain = new List<RunningStep>();
            var depth = 0;
            for (ISequenceContainer c = plan; c != null && depth < MaxDepth; c = (c as ISequenceItem)?.Parent, depth++) {
                if (c is not SequenceContainer sc) {
                    continue;
                }
                foreach (var t in sc.Triggers.ToArray()) {
                    if (t == null || t.Status != SequenceEntityStatus.RUNNING) {
                        continue;
                    }
                    chain.Add(Step(t));
                    if (t is SequenceTrigger st && st.TriggerRunner != null) {
                        Items(st.TriggerRunner, chain, 0);
                    }
                    return chain;
                }
            }
            return chain;
        }

        private static bool Items(ISequenceContainer container, List<RunningStep> chain, int depth) {
            if (depth >= MaxDepth) {
                return false;
            }
            foreach (var item in container.Items.ToArray()) {
                if (item == null) {
                    continue;
                }
                var running = item.Status == SequenceEntityStatus.RUNNING;
                if (item is ISequenceContainer inner) {
                    var mark = chain.Count;
                    chain.Add(Step(item));
                    if (Items(inner, chain, depth + 1) || running) {
                        return true;
                    }
                    chain.RemoveRange(mark, chain.Count - mark);
                } else if (running) {
                    chain.Add(Step(item));
                    return true;
                }
            }
            return false;
        }

        private static RunningStep Step(ISequenceEntity e) {
            return RunningStep.Of(e.GetType().FullName, string.IsNullOrEmpty(e.Name) ? e.GetType().Name : e.Name);
        }
    }
}
