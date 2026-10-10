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
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class PreviewRunner {
        private const int MaxPlans = 5000;
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
            };

            var projects = LoadProjects(profile, prefs, request.Overrides ?? new List<PreviewOverride>());
            if (projects.Count == 0) {
                return preview;
            }

            DitherManagerCache.ClearPreview();
            SmartExposureRotateCache.ClearPreview();
            var weather = new DisconnectedWeatherDataMediator();
            var planned = new List<PlannedExposure>();
            var leftOut = new List<LeftOut>();
            ITarget previous = null;
            DateTime current = startLocal;
            var stopAt = startLocal.AddHours(20);

            for (int i = 0; i < MaxPlans && current < stopAt; i++) {
                SchedulerPlan plan;
                try {
                    plan = new Planner(current, profile, prefs, weather, false, true, projects).GetPlan(previous);
                } catch (Exception e) {
                    TSLogger.Warning($"remote preview stopped at {current}: {e.Message}");
                    break;
                }
                if (plan == null) {
                    CollectRejections(projects, leftOut);
                    break;
                }
                if (plan.IsWait) {
                    CollectRejections(projects, leftOut);
                    var until = (DateTime)plan.WaitForNextTargetTime;
                    planned.Add(new PlannedExposure { Start = current.ToUniversalTime(), End = until.ToUniversalTime(), Wait = true, TargetName = plan.PlanTarget?.Name, TargetId = plan.PlanTarget?.DatabaseId ?? 0 });
                    current = until;
                    previous = null;
                } else {
                    var t = plan.PlanTarget;
                    bool newPick = !ReferenceEquals(t, previous);
                    if (newPick) {
                        CollectRejections(projects, leftOut);
                    }
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

            preview.Blocks = PreviewBuilder.Blocks(planned);
            preview.LeftOut = PreviewBuilder.LeftOut(leftOut, preview.Blocks);
            preview.End = planned.Count > 0 ? planned[^1].End : preview.Start;
            return preview;
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

        private static void CollectRejections(List<IProject> projects, List<LeftOut> leftOut) {
            foreach (var p in projects) {
                foreach (var t in p.Targets) {
                    if (t.Rejected || p.Rejected) {
                        leftOut.Add(new LeftOut {
                            ProjectId = p.DatabaseId,
                            ProjectName = p.Name,
                            TargetId = t.DatabaseId,
                            TargetName = t.Name,
                            Reason = t.RejectedReason ?? p.RejectedReason,
                        });
                    }
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
