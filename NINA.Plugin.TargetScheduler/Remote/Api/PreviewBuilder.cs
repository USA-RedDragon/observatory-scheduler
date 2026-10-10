using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class PreviewBuilder {
        public const string LowerScore = "lower score";
        public const string Complete = "its plans are complete";

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
                    current.Reason ??= EndReason(prev);
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
                current.Reason ??= EndReason(last);
            }
            return blocks;
        }

        public static string EndReason(PlannedExposure ending) {
            if (ending == null) {
                return null;
            }
            if (ending.CompletesTarget) {
                return Complete;
            }
            return ending.EndReason;
        }

        public static List<LeftOut> LeftOut(IEnumerable<Rejection> candidates, IEnumerable<PreviewBlock> blocks, IEnumerable<DateTime> passTimes, DateTime end) {
            var used = new HashSet<int>(blocks.Where(b => !b.Wait).Select(b => b.TargetId));
            var times = passTimes.Distinct().OrderBy(t => t).ToList();
            return candidates.Where(c => !used.Contains(c.TargetId))
                .GroupBy(c => c.TargetId)
                .Select(g => Summarise(g.OrderBy(c => c.At).ToList(), times, end))
                .OrderBy(c => c.ProjectName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.TargetName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static LeftOut Summarise(List<Rejection> all, List<DateTime> times, DateTime end) {
            var first = all[0];
            var result = new LeftOut {
                ProjectId = first.ProjectId,
                ProjectName = first.ProjectName,
                TargetId = first.TargetId,
                TargetName = first.TargetName,
            };
            var night = all.Where(c => c.Pass != Rejection.PassEnd).ToList();
            if (night.Count == 0) {
                var last = all[^1];
                result.Reason = last.Reason;
                result.At = last.At;
                result.Until = last.At;
                result.AfterNight = true;
                return result;
            }

            var spans = night.Select(c => (c, until: NextPass(times, c.At, end))).ToList();
            result.Reasons = spans.GroupBy(s => s.c.Reason)
                .Select(g => new LeftOutReason {
                    Reason = g.Key,
                    From = g.Min(s => s.c.At),
                    Until = g.Max(s => s.until),
                    Minutes = Math.Round(g.Sum(s => (s.until - s.c.At).TotalMinutes), 1),
                })
                .OrderBy(r => r.From)
                .ToList();

            var outscored = night.Where(c => c.Reason == LowerScore && c.Total.HasValue && c.WinnerTotal.HasValue)
                .OrderBy(c => c.WinnerTotal.Value - c.Total.Value).ThenBy(c => c.At).FirstOrDefault();
            if (outscored != null) {
                result.Reason = LowerScore;
                result.At = outscored.At;
                result.Until = NextPass(times, outscored.At, end);
                result.WinnerName = outscored.WinnerName;
                result.WinnerTotal = outscored.WinnerTotal;
                result.Total = outscored.Total;
                return result;
            }

            var main = result.Reasons.OrderByDescending(r => r.Minutes).ThenBy(r => r.From).First();
            result.Reason = main.Reason;
            result.At = main.From;
            result.Until = main.Until;
            return result;
        }

        private static DateTime NextPass(List<DateTime> times, DateTime at, DateTime end) {
            foreach (var t in times) {
                if (t > at) {
                    return t;
                }
            }
            return end > at ? end : at;
        }

        public static List<PreviewAssumption> Assumptions(int previewSampleSeconds, int liveSampleSeconds, double hours, int maxPlans) {
            return new List<PreviewAssumption> {
                new PreviewAssumption { Key = "weather", Text = "No weather data is used, so humidity never rejects a target and no weather stop happens." },
                new PreviewAssumption { Key = "grading", Text = "Every simulated sub counts as acquired, and as accepted on projects that use the grader." },
                new PreviewAssumption { Key = "overhead", Text = "Each sub takes exactly its exposure time: no download, dither, slew, centering, autofocus or meridian flip time." },
                new PreviewAssumption { Key = "visibility", Text = $"Target visibility is sampled every {previewSampleSeconds} s instead of the live planner's {liveSampleSeconds} s." },
                new PreviewAssumption { Key = "web", Text = "Web pauses, re-plan requests and edits made after the simulation starts are not simulated; skips active now are applied until they end." },
                new PreviewAssumption { Key = "limits", Text = $"The simulation stops after {hours:0} hours or {maxPlans} plans." },
            };
        }
    }
}
