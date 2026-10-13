using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class PreviewRunner {
        private const int MaxPlans = 5000;
        private const int Hours = 20;
        private static readonly object runLock = new object();
        private readonly IProfileService profileService;
        private readonly RemoteHost host;

        public PreviewRunner(IProfileService profileService, RemoteHost host) {
            this.profileService = profileService;
            this.host = host;
        }

        public Preview Run(PreviewRequest request) {
            lock (runLock) {
                return RunLocked(request ?? new PreviewRequest());
            }
        }

        private Preview RunLocked(PreviewRequest request) {
            var profile = profileService.ActiveProfile;
            var loader = new SchedulerPlanLoader(profile);
            var prefs = loader.GetProfilePreferences(new SchedulerDatabaseInteraction().GetContext());
            var nowLocal = DateTime.Now;
            var tc = host.TwilightFor(request.Start?.ToLocalTime() ?? nowLocal);
            DateTime startLocal = request.Start?.ToLocalTime() ?? DefaultStart(nowLocal, tc);
            var live = request.Start == null ? host.Imaging() : null;
            startLocal = PreviewBuilder.LiveStart(startLocal.ToUniversalTime(), live).ToLocalTime();

            var preview = new Preview {
                Start = startLocal.ToUniversalTime(),
                End = startLocal.ToUniversalTime(),
                GeneratedAt = DateTime.UtcNow,
                Twilight = new Twilight {
                    Sunset = tc.CivilTwilightStart?.ToUniversalTime(),
                    CivilDusk = tc.NauticalTwilightStart?.ToUniversalTime(),
                    NauticalDusk = tc.AstronomicalTwilightStart?.ToUniversalTime(),
                    AstronomicalDusk = tc.NighttimeStart?.ToUniversalTime(),
                    AstronomicalDawn = tc.NighttimeEnd?.ToUniversalTime(),
                    NauticalDawn = tc.AstronomicalTwilightEnd?.ToUniversalTime(),
                    CivilDawn = tc.NauticalTwilightEnd?.ToUniversalTime(),
                    Sunrise = tc.CivilTwilightEnd?.ToUniversalTime(),
                },
                Assumptions = PreviewBuilder.Assumptions(Hours, MaxPlans),
            };
            var pending = host.Processor.Pending;
            var skips = host.Control.Skips.Copy();
            var appliesAt = live?.EndsAt ?? DateTime.UtcNow;
            PendingPreview.ApplySkips(skips, pending, appliesAt, host.Control.NightEnd);
            var asked = PreviewOverrides.Skips(request.Overrides);
            foreach (var p in asked) {
                skips.Add(p, SkipHandler.Until(p, appliesAt, host.Control.NightEnd));
            }
            if (live != null && asked.Count > 0) {
                live.ReplanReason ??= "the skip being simulated applies when the exposure ends";
            }
            preview.Skips = skips.Active(startLocal.ToUniversalTime());
            var limit = host.LiveLimit is ImagingLimit l && l.Covers(startLocal) ? l : null;

            var simulated = new SimulatedStackSubs();
            var history = new PanelHistory(null);
            var projects = LoadProjects(profile, prefs, request.Overrides ?? new List<PreviewOverride>(), simulated, history);
            var running = live == null || live.PauseWaiting ? null : projects.SelectMany(p => p.Targets).FirstOrDefault(t => t.DatabaseId == live.TargetId);
            if (request.Start == null) {
                preview.Assumptions.Insert(0, PreviewBuilder.LiveAssumption(preview.Start, live, running != null));
            }
            if (projects.Count == 0) {
                return preview;
            }

            DitherManagerCache.ClearPreview();
            SmartExposureRotateCache.ClearPreview();
            var weather = new DisconnectedWeatherDataMediator();
            var planned = new List<PlannedExposure>();
            var rejections = new List<Rejection>();
            var passTimes = new List<DateTime>();
            ITarget previous = running;
            if (running != null && live.PlanId is int planId && live.EndsAt is DateTime endsAt && endsAt >= preview.Start) {
                CountRunning(running, planId, simulated, live.StartedAt, endsAt);
            }
            if (running != null) {
                KeepLiveWindow(running, live);
            }
            DateTime current = startLocal;
            var stopAt = startLocal.AddHours(Hours);
            string stopReason = null;
            int i = 0;

            for (; i < MaxPlans && current < stopAt; i++) {
                SchedulerPlan plan;
                Dictionary<ITarget, string> filtered = null;
                string preempted = null;
                try {
                    var planner = new Planner(current, profile, prefs, weather, false, true, projects) {
                        Filtered = ps => filtered = Snapshot(ps),
                        Skips = skips,
                        LiveSampling = true,
                        Limit = limit,
                    };
                    plan = planner.GetPlan(previous);
                    preempted = planner.PreemptedBy;
                } catch (Exception e) {
                    TSLogger.Warning($"remote preview stopped at {current}: {e.Message}");
                    preview.Error = e.Message;
                    stopReason = $"the simulation failed at {current:HH:mm}: {e.Message}";
                    EndLast(planned, previous, stopReason);
                    break;
                }
                bool continuing = plan != null && !plan.IsWait && ReferenceEquals(plan.PlanTarget, previous);
                if (previous != null && !continuing) {
                    EndLast(planned, previous, GaveWay(previous, filtered, plan, current, preempted));
                }
                if (filtered != null) {
                    passTimes.Add(current.ToUniversalTime());
                    string pass = plan == null ? Rejection.PassEnd : plan.IsWait ? Rejection.PassWait : Rejection.PassPick;
                    CollectRejections(projects, filtered, plan, current, pass, rejections);
                }
                if (plan == null) {
                    stopReason = $"the planner found nothing left to image at {current:HH:mm}";
                    break;
                }
                if (plan.IsWait) {
                    var until = (DateTime)plan.WaitForNextTargetTime;
                    planned.Add(new PlannedExposure { Start = current.ToUniversalTime(), End = until.ToUniversalTime(), Wait = true, TargetName = plan.PlanTarget?.Name, TargetId = plan.PlanTarget?.DatabaseId ?? 0 });
                    current = until;
                    previous = null;
                } else {
                    var t = plan.PlanTarget;
                    bool newPick = planned.Count == 0 || !ReferenceEquals(t, previous);
                    var p = new PlannedExposure {
                        Start = plan.StartTime.ToUniversalTime(),
                        End = plan.EndTime.ToUniversalTime(),
                        ProjectId = t.Project.DatabaseId,
                        ProjectName = t.Project.Name,
                        TargetId = t.DatabaseId,
                        TargetName = t.Name,
                        Filter = t.SelectedExposure.FilterName,
                        ExposureSeconds = t.SelectedExposure.ExposureLength,
                        LengthSource = t.SelectedExposure.LengthSource,
                        LengthBucket = t.SelectedExposure.LengthBucket,
                        NewPick = newPick,
                        TargetEnd = t.EndTime == default ? null : t.EndTime.ToUniversalTime(),
                        Held = HeldAt(t, current),
                    };
                    if (newPick) {
                        p.Scores = t.ScoringResults?.Results?.Select(r => new RuleScore { Rule = r.ScoringRule.Name, Weight = r.Weight, Score = r.Score }).ToList();
                        p.Total = t.ScoringResults?.TotalScore;
                        var runner = projects.SelectMany(x => x.Targets).Where(x => !ReferenceEquals(x, t) && x.ScoringResults != null)
                            .OrderByDescending(x => x.ScoringResults.TotalScore).FirstOrDefault();
                        if (runner != null) {
                            p.RunnerUp = new RunnerUp { TargetName = runner.Name, Total = runner.ScoringResults.TotalScore };
                        }
                    }
                    Advance(t, previous, simulated, plan.StartTime);
                    p.CompletesTarget = t.AllExposurePlans.All(ep => !ep.IsIncomplete());
                    planned.Add(p);
                    current = plan.EndTime;
                    previous = t;
                }
                Reset(projects);
            }
            if (stopReason == null) {
                stopReason = i >= MaxPlans
                    ? $"the simulation stopped after {MaxPlans} plans"
                    : $"the simulation covers {Hours} hours from its start";
                EndLast(planned, previous, stopReason);
            }

            preview.StopReason = stopReason;
            preview.End = planned.Count > 0 ? planned[^1].End : preview.Start;
            preview.Blocks = PreviewBuilder.Blocks(planned);
            preview.LeftOut = PreviewBuilder.LeftOut(rejections, preview.Blocks, passTimes, preview.End);
            return preview;
        }

        private static void EndLast(List<PlannedExposure> planned, ITarget previous, string reason) {
            if (previous == null || planned.Count == 0) {
                return;
            }
            var last = planned[^1];
            if (!last.Wait && last.TargetId == previous.DatabaseId) {
                last.EndReason ??= reason;
            }
        }

        private static Dictionary<ITarget, string> Snapshot(List<IProject> projects) {
            var reasons = new Dictionary<ITarget, string>(ReferenceEqualityComparer.Instance);
            foreach (var p in projects ?? new List<IProject>()) {
                foreach (var t in p.Targets) {
                    reasons[t] = t.Rejected ? t.RejectedReason ?? p.RejectedReason : null;
                }
            }
            return reasons;
        }

        private static bool LostOnScore(ITarget t, SchedulerPlan plan) {
            return t.Rejected && (t.RejectedReason == Reasons.TargetLowerScore || t.RejectedReason == Reasons.TargetHeldByRunning) && plan != null && !plan.IsWait && plan.PlanTarget != null;
        }

        private static string HeldAt(ITarget t, DateTime at) {
            return t.ScoringResults?.Held is string held ? string.Create(CultureInfo.InvariantCulture, $"{at:HH:mm}: {held}") : null;
        }

        private static bool YieldedToEvent(ITarget t) {
            return t.Rejected && EventWindows.IsPriorityReason(t.RejectedReason);
        }

        public static string GaveWay(ITarget previous, Dictionary<ITarget, string> filtered, SchedulerPlan plan, DateTime at, string preempted = null) {
            if (preempted != null) {
                return EventWindows.PreemptedReason(plan != null && !plan.IsWait && plan.PlanTarget != null ? $"{plan.PlanTarget.Project?.Name}/{plan.PlanTarget.Name}" : preempted);
            }
            if (YieldedToEvent(previous) && plan != null && !plan.IsWait && plan.PlanTarget != null) {
                return $"{previous.RejectedReason} ({plan.PlanTarget.Name})";
            }
            if (LostOnScore(previous, plan)) {
                var w = plan.PlanTarget;
                return string.Create(CultureInfo.InvariantCulture,
                    $"{w.Name} scored higher at the re-plan ({w.ScoringResults?.TotalScore:0.00} vs {previous.ScoringResults?.TotalScore:0.00})");
            }
            string reason = null;
            filtered?.TryGetValue(previous, out reason);
            bool readyLater = plan != null && plan.IsWait && !previous.Rejected && previous.StartTime > at;
            if (reason != null) {
                return readyLater ? $"{reason}; next ready at {previous.StartTime:HH:mm}" : reason;
            }
            if (readyLater) {
                return $"it is not ready again until {previous.StartTime:HH:mm}";
            }
            return "the planner did not offer it at the re-plan";
        }

        private static DateTime DefaultStart(DateTime nowLocal, Astrometry.TwilightCircumstances tc) {
            var dusk = tc.CivilTwilightStart;
            if (dusk.HasValue && nowLocal < dusk.Value) {
                return dusk.Value;
            }
            return nowLocal;
        }

        /// <summary>
        /// Gives the running target the window the live planner holds for it, so the simulation keeps it exactly
        /// as long as the live planner will. A target loaded fresh has no window, and the planner would re-plan
        /// at once. When the live planner re-plans at the end of the running exposure anyway, the target keeps
        /// no window and the simulation re-plans there too.
        /// </summary>
        public static void KeepLiveWindow(ITarget running, LiveImaging live) {
            if (running == null || live?.Window == null || live.ReplanReason != null) {
                return;
            }
            running.StartTime = live.Window.Start;
            running.EndTime = live.Window.End;
            running.MinimumTimeSpanEnd = live.Window.MinimumEnd;
            running.BonusTimeSpanEnd = live.Window.BonusEnd;
            running.CulminationTime = live.Window.Culmination;
        }

        private static void CountRunning(ITarget t, int planId, SimulatedStackSubs simulated, DateTime? startedAt, DateTime endsAt) {
            var exposure = t.AllExposurePlans.FirstOrDefault(e => e.DatabaseId == planId);
            if (exposure == null) {
                return;
            }
            exposure.Acquired++;
            if (t.Project.EnableGrader) {
                exposure.Accepted++;
            }
            simulated.Add(t.Guid, exposure.FilterName);
            PanelBalance.Record(t, exposure, startedAt ?? endsAt.AddSeconds(-exposure.ExposureLength));
        }

        private static void Advance(ITarget t, ITarget previous, SimulatedStackSubs simulated, DateTime startTime) {
            if (previous != null && !ReferenceEquals(t, previous)) {
                t.ExposureSelector.TargetReset();
            }
            t.ExposureSelector.ExposureTaken(t.SelectedExposure);
            t.SelectedExposure.Acquired++;
            simulated.Add(t.Guid, t.SelectedExposure.FilterName);
            PanelBalance.Record(t, t.SelectedExposure, startTime);
            if (t.Project.EnableGrader) {
                t.SelectedExposure.Accepted++;
            }
        }

        private static void CollectRejections(List<IProject> projects, Dictionary<ITarget, string> filtered, SchedulerPlan plan, DateTime at, string pass, List<Rejection> rejections) {
            var winner = plan != null && !plan.IsWait ? plan.PlanTarget : null;
            foreach (var p in projects) {
                foreach (var t in p.Targets) {
                    filtered.TryGetValue(t, out var reason);
                    bool lost = LostOnScore(t, plan);
                    if (reason == null && !lost && YieldedToEvent(t)) {
                        reason = t.RejectedReason;
                    }
                    if (reason == null && !lost) {
                        continue;
                    }
                    var r = new Rejection {
                        ProjectId = p.DatabaseId,
                        ProjectName = p.Name,
                        TargetId = t.DatabaseId,
                        TargetName = t.Name,
                        Reason = lost ? t.RejectedReason : reason,
                        At = at.ToUniversalTime(),
                        Pass = pass,
                    };
                    if (lost) {
                        r.Total = t.ScoringResults?.TotalScore;
                        r.WinnerName = winner.Name;
                        r.WinnerTotal = winner.ScoringResults?.TotalScore;
                    }
                    rejections.Add(r);
                }
            }
        }

        private static void Reset(List<IProject> projects) {
            foreach (var project in projects) {
                project.Rejected = false;
                project.RejectedReason = null;
                foreach (var target in project.Targets) {
                    target.ScoringResults = null;
                    target.Rejected = false;
                    target.RejectedReason = null;
                    foreach (var exposure in target.ExposurePlans) {
                        exposure.Rejected = false;
                        exposure.RejectedReason = null;
                        exposure.MoonAvoidanceScore = MoonAvoidanceExpert.SCORE_OFF;
                    }
                }
            }
        }

        private static List<IProject> LoadProjects(IProfile profile, ProfilePreference prefs, List<PreviewOverride> overrides, SimulatedStackSubs simulated, PanelHistory history) {
            var profileId = profile.Id.ToString();
            var overridden = new HashSet<int>(overrides.Where(o => o.Entity == PreviewOverrides.Project).Select(o => o.Id));
            var result = new List<IProject>();
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                ForkDatabase.RefreshGoalSource(context);
                var projects = context.ProjectSet
                    .Include("targets.exposureplans.exposuretemplate")
                    .Include("ruleweights")
                    .Where(p => p.ProfileId == profileId && (p.state_col == (int)ProjectState.Active || overridden.Contains(p.Id)))
                    .ToList();
                foreach (var project in projects) {
                    foreach (var o in overrides.Where(o => o.Entity == PreviewOverrides.Project && o.Id == project.Id)) {
                        switch (o.Field) {
                            case "priority":
                                project.priority_col = (int)o.Value;
                                break;

                            case "state":
                                project.state_col = (int)o.Value;
                                break;

                            case "minimumtime":
                                project.minimumTime = (int)o.Value;
                                break;

                            case "minimumaltitude":
                                project.minimumAltitude = o.Value;
                                break;
                        }
                    }
                    if (project.state_col != (int)ProjectState.Active) {
                        continue;
                    }
                    var weights = PreviewOverrides.RuleWeights(overrides, project.Id);
                    if (weights.Count > 0) {
                        project.RuleWeights ??= new List<RuleWeight>();
                        PreviewOverrides.Apply(project.RuleWeights, weights, r => r.Name, (r, w) => r.Weight = w, (n, w) => new RuleWeight(n, w) { ProjectId = project.Id });
                    }
                    foreach (var target in project.Targets) {
                        target.OverrideExposureOrders = context.GetOverrideExposureOrders(target.Id);
                        target.FilterCadences = context.GetFilterCadences(target.Id);
                        target.ExposurePlans.RemoveAll(ep => !ep.IsEnabled);
                    }
                    var helper = new ExposureCompletionHelper(project.EnableGrader, prefs.DelayGrading, prefs.ExposureThrottle) { PreviewSubs = simulated, PreviewHistory = history };
                    var planning = new PlanningProject(profile, project, helper);
                    planning.Targets.ForEach(t => t.IsPreview = true);
                    result.Add(planning);
                }
            }
            return result;
        }
    }
}
