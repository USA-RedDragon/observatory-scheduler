using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    /// <summary>
    /// What the commands that wait for the running exposure to end will do when they apply. A preview that starts
    /// at the end of the running exposure starts where they apply, so it must plan with their effect.
    /// </summary>
    public static class PendingPreview {

        /// <summary>
        /// The pending commands in the order the processor applies them.
        /// </summary>
        public static List<CommandEnvelope> InApplyOrder(IEnumerable<PendingCommand> pending) {
            return (pending ?? Enumerable.Empty<PendingCommand>())
                .Select((p, i) => (p, i))
                .OrderBy(x => x.p.Command.CreatedAt).ThenBy(x => x.i)
                .Select(x => x.p.Command)
                .ToList();
        }

        /// <summary>
        /// Applies the pending skips and unskips to skips, as their handlers do when they apply at appliesAt.
        /// </summary>
        public static void ApplySkips(SkipList skips, IEnumerable<PendingCommand> pending, DateTime appliesAt, Func<DateTime, DateTime?> nightEnd) {
            foreach (var c in InApplyOrder(pending)) {
                if (c.Kind != CommandKinds.Skip && c.Kind != CommandKinds.Unskip) {
                    continue;
                }
                SkipPayload p;
                try {
                    p = c.PayloadAs<SkipPayload>();
                } catch (Exception) {
                    continue;
                }
                if (p == null || SkipHandler.CheckSkip(p) != null) {
                    continue;
                }
                if (c.Kind == CommandKinds.Skip) {
                    skips.Add(p, SkipHandler.Until(p, appliesAt, nightEnd));
                } else {
                    skips.Remove(p);
                }
            }
        }

        /// <summary>
        /// Why the live planner re-plans when the running exposure ends instead of keeping its target, given the
        /// commands still pending and the skips active now, or null when none of them asks for a re-plan. A pause
        /// does not, and an unskip does only when the skip it removes is there. Every other pending kind asks for
        /// one when it applies and changes something; an edit that changes nothing is counted too.
        /// </summary>
        public static string ReplanReason(IEnumerable<PendingCommand> pending, SkipList skips) {
            var replay = skips?.Copy() ?? new SkipList();
            var kinds = new List<string>();
            foreach (var c in InApplyOrder(pending)) {
                if (c.Kind == CommandKinds.Pause) {
                    continue;
                }
                if (c.Kind == CommandKinds.Skip || c.Kind == CommandKinds.Unskip) {
                    SkipPayload p;
                    try {
                        p = c.PayloadAs<SkipPayload>();
                    } catch (Exception) {
                        continue;
                    }
                    if (p == null || SkipHandler.CheckSkip(p) != null) {
                        continue;
                    }
                    if (c.Kind == CommandKinds.Skip) {
                        replay.Add(p, new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                    } else if (!replay.Remove(p)) {
                        continue;
                    }
                }
                if (!kinds.Contains(c.Kind)) {
                    kinds.Add(c.Kind);
                }
            }
            if (kinds.Count == 0) {
                return null;
            }
            return $"the waiting {string.Join(", ", kinds)} {(kinds.Count == 1 ? "command applies" : "commands apply")} when the exposure ends";
        }
    }
}
