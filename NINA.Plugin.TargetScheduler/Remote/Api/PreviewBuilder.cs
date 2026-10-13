using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class PreviewBuilder {
        public const string LowerScore = "lower score";
        public const string HeldByRunning = "the running target scored within 1% and kept its turn";
        public const string Complete = "its plans are complete";
        public const string SystemSource = "system";

        public static List<PreviewBlock> Blocks(IList<PlannedExposure> plans) {
            var blocks = new List<PreviewBlock>();
            PreviewBlock current = null;
            PlannedExposure last = null;
            foreach (var p in plans) {
                var prev = last;
                last = p;
                bool sameTarget = current != null && !current.Wait && !p.Wait && current.TargetId == p.TargetId;
                if (sameTarget && !p.NewPick && SameLength(current, p)) {
                    current.End = p.End;
                    current.Count++;
                    current.Held ??= p.Held;
                    if (current.LengthBucket != p.LengthBucket) {
                        current.LengthBucket = null;
                    }
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
                    LengthSource = p.Wait ? null : p.LengthSource,
                    LengthBucket = p.Wait ? null : p.LengthBucket,
                    Count = p.Wait ? 0 : 1,
                    Picked = picked,
                    Scores = picked ? p.Scores : null,
                    Total = picked ? p.Total : null,
                    RunnerUp = picked ? p.RunnerUp : null,
                    Held = p.Held,
                };
                blocks.Add(current);
            }
            if (current != null && !current.Wait) {
                current.Reason ??= EndReason(last);
            }
            return blocks;
        }

        public static bool SameLength(PreviewBlock block, PlannedExposure p) {
            if (block.Filter != p.Filter || block.ExposureSeconds != p.ExposureSeconds || block.LengthSource != p.LengthSource) {
                return false;
            }
            return block.LengthSource != SystemSource || block.LengthBucket == p.LengthBucket;
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

            var outscored = night.Where(c => (c.Reason == LowerScore || c.Reason == HeldByRunning) && c.Total.HasValue && c.WinnerTotal.HasValue)
                .OrderBy(c => c.WinnerTotal.Value - c.Total.Value).ThenBy(c => c.At).FirstOrDefault();
            if (outscored != null) {
                result.Reason = outscored.Reason;
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

        public static DateTime LiveStart(DateTime startUtc, LiveImaging live) {
            return live?.EndsAt is DateTime end && end > startUtc ? end : startUtc;
        }

        public static PreviewAssumption LiveAssumption(DateTime startUtc, LiveImaging live, bool simulated) {
            const string key = "live";
            if (live == null) {
                return new PreviewAssumption { Key = key, Text = "Nothing is imaging now, so no target starts as the current one." };
            }
            if (live.PauseWaiting) {
                return new PreviewAssumption { Key = key, Text = $"{live.TargetName} is imaging now, but a waiting pause stops the scheduler when the exposure ends. The pause is not simulated: the simulation plans as if the scheduler resumed at once, with no current target." };
            }
            if (!simulated) {
                return new PreviewAssumption { Key = key, Text = $"{live.TargetName} is imaging now but is not among the simulated projects, so no target starts as the current one." };
            }
            var current = $"{live.TargetName} starts as the current target, so it keeps the Target Switch Penalty score the live planner gives it";
            if (live.ReplanReason != null) {
                current += $". The live planner re-plans then because {live.ReplanReason}, so the simulation re-plans too";
            } else if (live.Window != null) {
                current += $", and it keeps imaging while the live planner would keep it: its minimum time runs to {live.Window.MinimumEnd.ToLocalTime():HH:mm}";
            }
            if (live.EndsAt is DateTime end && end >= startUtc) {
                return new PreviewAssumption { Key = key, Text = $"The simulation starts when the running {live.Filter} exposure ends at {end.ToLocalTime():HH:mm} and counts that exposure as acquired. {current}." };
            }
            return new PreviewAssumption { Key = key, Text = $"No exposure is running, so the simulation starts now; the rest of the current step (slew, centering, focus, download or save) is not simulated. {current}." };
        }

        public static List<PreviewAssumption> Assumptions(double hours, int maxPlans) {
            return new List<PreviewAssumption> {
                new PreviewAssumption { Key = "weather", Text = "No weather data is used, so humidity never rejects a target and no weather stop happens." },
                new PreviewAssumption { Key = "grading", Text = "Every simulated sub counts as acquired, and as accepted on projects that use the grader." },
                new PreviewAssumption { Key = "overhead", Text = "Each sub takes exactly its exposure time: no download, dither, slew, centering, autofocus or meridian flip time." },
                new PreviewAssumption { Key = "web", Text = "Skips waiting for the running exposure to end apply when it ends, and run until they end. A waiting re-plan request or edit makes the simulation re-plan when the exposure ends, as the live planner does, but it plans with the values from before the edit. Web pauses, and anything sent after the simulation starts, are not simulated." },
                new PreviewAssumption { Key = "limits", Text = $"The simulation stops after {hours:0} hours or {maxPlans} plans." },
            };
        }
    }

    public class LiveImaging {
        public int TargetId { get; set; }
        public string TargetName { get; set; }
        public int? PlanId { get; set; }
        public string Filter { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndsAt { get; set; }

        /// <summary>
        /// The live planner's time window for the target, which decides whether it keeps the target at its
        /// next re-plan. Null when the live planner holds no window for it.
        /// </summary>
        public PlanWindow Window { get; set; }

        /// <summary>
        /// Why the live planner will re-plan when the running exposure ends instead of keeping the target, or
        /// null when it keeps the target while its window allows.
        /// </summary>
        public string ReplanReason { get; set; }

        /// <summary>
        /// A pause waits for the running exposure to end. After a pause the live planner has no current target.
        /// </summary>
        public bool PauseWaiting { get; set; }
    }

    /// <summary>
    /// The times the live planner set for a target when it picked it: visibility, the end of the project's
    /// minimum time, the end of any extension to the end of visibility, and the transit.
    /// </summary>
    public class PlanWindow {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public DateTime MinimumEnd { get; set; }
        public DateTime BonusEnd { get; set; }
        public DateTime Culmination { get; set; }
    }
}
