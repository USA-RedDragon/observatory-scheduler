using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Remote.Api;
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
                Skips = host.Control.Skips.Active(DateTime.UtcNow),
                Assumptions = PreviewBuilder.Assumptions(TargetImagingExpert.TARGET_VISIBILITY_SAMPLE_INTERVAL * TargetImagingExpert.PREVIEW_SAMPLE_FACTOR, TargetImagingExpert.TARGET_VISIBILITY_SAMPLE_INTERVAL, Hours, MaxPlans),
            };

            var projects = LoadProjects(profile, prefs, request.Overrides ?? new List<PreviewOverride>());
            if (projects.Count == 0) {
                return preview;
            }

            DitherManagerCache.ClearPreview();
            SmartExposureRotateCache.ClearPreview();
            var weather = new DisconnectedWeatherDataMediator();
            var planned = new List<PlannedExposure>();
            var rejections = new List<Rejection>();
            var passTimes = new List<DateTime>();
            ITarget previous = null;
            DateTime current = startLocal;
            var stopAt = startLocal.AddHours(Hours);
            string stopReason = null;
            int i = 0;

            for (; i < MaxPlans && current < stopAt; i++) {
                SchedulerPlan plan;
                Dictionary<ITarget, string> filtered = null;
                try {
                    var planner = new Planner(current, profile, prefs, weather, false, true, projects) {
                        Filtered = ps => filtered = Snapshot(ps),
                    };
                    plan = planner.GetPlan(previous);
                } catch (Exception e) {
                    TSLogger.Warning($"remote preview stopped at {current}: {e.Message}");
                    preview.Error = e.Message;
                    stopReason = $"the simulation failed at {current:HH:mm}: {e.Message}";
                    EndLast(planned, previous, stopReason);
                    break;
                }
                bool continuing = plan != null && !plan.IsWait && ReferenceEquals(plan.PlanTarget, previous);
                if (previous != null && !continuing) {
                    EndLast(planned, previous, GaveWay(previous, filtered, plan, current));
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
                    bool newPick = !ReferenceEquals(t, previous);
                    var p = new PlannedExposure {
                        Start = plan.StartTime.ToUniversalTime(),
                        End = plan.EndTime.ToUniversalTime(),
                        ProjectId = t.Project.DatabaseId,
                        ProjectName = t.Project.Name,
                        TargetId = t.DatabaseId,
                        TargetName = t.Name,
                        Filter = t.SelectedExposure.FilterName,
                        ExposureSeconds = t.SelectedExposure.ExposureLength,
                        NewPick = newPick,
                        TargetEnd = t.EndTime == default ? null : t.EndTime.ToUniversalTime(),
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
                    Advance(t, previous);
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
            return t.Rejected && t.RejectedReason == Reasons.TargetLowerScore && plan != null && !plan.IsWait && plan.PlanTarget != null;
        }

        public static string GaveWay(ITarget previous, Dictionary<ITarget, string> filtered, SchedulerPlan plan, DateTime at) {
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

        private static void Advance(ITarget t, ITarget previous) {
            if (previous != null && !ReferenceEquals(t, previous)) {
                t.ExposureSelector.TargetReset();
            }
            t.ExposureSelector.ExposureTaken(t.SelectedExposure);
            t.SelectedExposure.Acquired++;
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
                    if (reason == null && !lost) {
                        continue;
                    }
                    var r = new Rejection {
                        ProjectId = p.DatabaseId,
                        ProjectName = p.Name,
                        TargetId = t.DatabaseId,
                        TargetName = t.Name,
                        Reason = lost ? Reasons.TargetLowerScore : reason,
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

        private static List<IProject> LoadProjects(IProfile profile, ProfilePreference prefs, List<PreviewOverride> overrides) {
            var profileId = profile.Id.ToString();
            var overridden = new HashSet<int>(overrides.Where(o => o.Entity == "project").Select(o => o.Id));
            var result = new List<IProject>();
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                var projects = context.ProjectSet
                    .Include("targets.exposureplans.exposuretemplate")
                    .Include("ruleweights")
                    .Where(p => p.ProfileId == profileId && (p.state_col == (int)ProjectState.Active || overridden.Contains(p.Id)))
                    .ToList();
                foreach (var project in projects) {
                    foreach (var o in overrides.Where(o => o.Entity == "project" && o.Id == project.Id)) {
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
                    foreach (var target in project.Targets) {
                        target.OverrideExposureOrders = context.GetOverrideExposureOrders(target.Id);
                        target.FilterCadences = context.GetFilterCadences(target.Id);
                        target.ExposurePlans.RemoveAll(ep => !ep.IsEnabled);
                    }
                    var helper = new ExposureCompletionHelper(project.EnableGrader, prefs.DelayGrading, prefs.ExposureThrottle);
                    var planning = new PlanningProject(profile, project, helper);
                    planning.Targets.ForEach(t => t.IsPreview = true);
                    result.Add(planning);
                }
            }
            return result;
        }
    }
}
