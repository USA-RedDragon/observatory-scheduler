using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class PreviewBuilder {

        public static List<PreviewBlock> Blocks(IList<PlannedExposure> plans) {
            var blocks = new List<PreviewBlock>();
            PreviewBlock current = null;
            PlannedExposure last = null;
            foreach (var p in plans) {
                var prev = last;
                last = p;
                bool sameTarget = current != null && !current.Wait && !p.Wait && current.TargetId == p.TargetId;
                if (sameTarget && !p.NewPick && current.Filter == p.Filter && current.ExposureSeconds == p.ExposureSeconds) {
                    current.End = p.End;
                    current.Count++;
                    continue;
                }
                bool picked = !p.Wait && (p.NewPick || current == null || current.Wait || current.TargetId != p.TargetId);
                if (current != null && !current.Wait && (p.Wait || current.TargetId != p.TargetId)) {
                    current.Reason ??= EndReason(prev, p);
                }
                current = new PreviewBlock {
                    Start = p.Start,
                    End = p.End,
                    Wait = p.Wait,
                    ProjectId = p.ProjectId,
                    ProjectName = p.ProjectName,
                    TargetId = p.TargetId,
                    TargetName = p.TargetName,
                    Filter = p.Wait ? null : p.Filter,
                    ExposureSeconds = p.Wait ? 0 : p.ExposureSeconds,
                    Count = p.Wait ? 0 : 1,
                    Picked = picked,
                    Scores = picked ? p.Scores : null,
                    Total = picked ? p.Total : null,
                    RunnerUp = picked ? p.RunnerUp : null,
                };
                blocks.Add(current);
            }
            if (current != null && !current.Wait) {
                current.Reason ??= last != null && last.CompletesTarget ? "its plans are complete" : "end of the night";
            }
            return blocks;
        }

        public static string EndReason(PlannedExposure ending, PlannedExposure next) {
            if (ending == null) {
                return null;
            }
            if (ending.CompletesTarget) {
                return "its plans are complete";
            }
            if (ending.TargetEnd.HasValue && next != null && (ending.TargetEnd.Value - next.Start).TotalMinutes < 15) {
                return $"it leaves its window at {ending.TargetEnd.Value.ToLocalTime():HH:mm}";
            }
            if (next == null || next.Wait) {
                return "nothing else is ready";
            }
            return $"{next.TargetName} scored higher at the re-plan";
        }

        public static List<LeftOut> LeftOut(IEnumerable<LeftOut> candidates, IEnumerable<PreviewBlock> blocks) {
            var used = new HashSet<int>(blocks.Where(b => !b.Wait).Select(b => b.TargetId));
            return candidates.Where(c => !used.Contains(c.TargetId))
                .GroupBy(c => c.TargetId).Select(g => g.Last())
                .OrderBy(c => c.ProjectName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
