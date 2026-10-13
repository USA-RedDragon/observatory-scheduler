using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning {

    public class Planner {
        private bool checkCondition = false;
        private bool isPreview = false;
        private DateTime atTime;
        private IProfile activeProfile;
        private ProfilePreference profilePreferences;
        private IWeatherDataMediator weatherDataMediator;
        private ObserverInfo observerInfo;
        private PreviousTargetExpert previousTargetExpert;
        private List<IProject> projects;

        public Action<List<IProject>> Filtered { get; set; }

        /// <summary>
        /// The skips this run honors. Null means the web skips the live scheduler holds now.
        /// </summary>
        public SkipList Skips { get; set; }

        /// <summary>
        /// Sample target visibility at the live planner's interval even in a preview, so the preview finds
        /// the same visibility windows as the live planner.
        /// </summary>
        public bool LiveSampling { get; set; }

        private ImagingLimit limit;

        public ImagingLimit Limit {
            get => limit;
            set {
                limit = value;
                previousTargetExpert.Limit = value;
            }
        }

        private TargetImagingExpert Expert() {
            return new TargetImagingExpert(activeProfile, profilePreferences, isPreview && !LiveSampling) { Limit = limit };
        }

        public Planner(DateTime atTime, IProfile profile, ProfilePreference profilePreferences, IWeatherDataMediator weatherDataMediator, bool checkCondition, bool isPreview)
            : this(atTime, profile, profilePreferences, weatherDataMediator, checkCondition, isPreview, null) {
        }

        public Planner(DateTime atTime, IProfile profile, ProfilePreference profilePreferences, IWeatherDataMediator weatherDataMediator, bool checkCondition, bool isPreview, List<IProject> projects) {
            this.atTime = atTime;
            this.activeProfile = profile;
            this.profilePreferences = profilePreferences;
            this.weatherDataMediator = weatherDataMediator;
            this.checkCondition = checkCondition;
            this.isPreview = isPreview;
            this.projects = projects;
            this.observerInfo = new ObserverInfo {
                Latitude = activeProfile.AstrometrySettings.Latitude,
                Longitude = activeProfile.AstrometrySettings.Longitude,
                Elevation = activeProfile.AstrometrySettings.Elevation,
            };

            SkyConditions.CurrentObserver = observerInfo;
            previousTargetExpert = new PreviousTargetExpert(activeProfile, profilePreferences, isPreview, observerInfo);
        }

        public SchedulerPlan GetPlan(ITarget previousTarget) {
            string type = checkCondition ? "CONDITION" : "EXECUTE";
            string title = $"PLANNING ENGINE RUN ({type})";

            TSLogger.Info($"-- BEGIN {title} ---------------------------------------------------");
            TSLogger.Info($"getting current plan for {Utils.FormatDateTimeFull(atTime)}");

            using (MyStopWatch.Measure("Scheduler Plan Generation")) {
                try {
                    if (projects == null) {
                        projects = GetProjects();
                    }

                    if (limit != null) {
                        TSLogger.Info($"sequence end of imaging: {limit.End:yyyy-MM-dd HH:mm:ss} ({limit.Basis})");
                    }

                    previousTargetExpert.Skips = ActiveSkips();

                    // If the previous target still has time in it's initial minimum time span and is still acceptable, then stick with it
                    if (previousTargetExpert.CanContinue(atTime, weatherDataMediator, previousTarget) && !HighEventPreempts(previousTarget)) {
                        TSLogger.Info($"previous target still within permitted time span, continuing: {previousTarget.Project.Name}/{previousTarget.Name}: {previousTarget.BonusTimeSpanEnd} > {atTime} ");
                        List<IInstruction> instructions = new InstructionGenerator().Generate(previousTarget, previousTarget, atTime);
                        return new SchedulerPlan(atTime, projects, previousTarget, instructions, !checkCondition);
                    }

                    projects = FilterForSkips(projects);

                    // Filter all targets for suitability
                    projects = FilterForIncomplete(projects);
                    projects = FilterForVisibility(projects);
                    projects = FilterForMoonAvoidance(projects);
                    projects = FilterForTwilight(projects);
                    projects = FilterForHumidity(projects, weatherDataMediator);
                    Filtered?.Invoke(projects);

                    // See if one or more targets are ready to image now
                    List<ITarget> readyTargets = GetTargetsReadyNow(projects);
                    if (readyTargets.Count > 0) {
                        SelectTargetExposures(readyTargets);
                        FilterForSequenceEnd(readyTargets);
                        FilterForEventPriority(readyTargets);
                    }

                    if (readyTargets.Count > 0) {
                        ITarget selectedTarget = readyTargets.Count == 1
                            ? readyTargets[0]
                            : SelectTargetByScore(readyTargets, new ScoringEngine(activeProfile, profilePreferences, atTime, previousTarget));
                        List<IInstruction> instructions = new InstructionGenerator().Generate(selectedTarget, previousTarget, atTime);

                        HandleTargetSwitch(previousTarget, selectedTarget);
                        if (!isPreview) { TargetEditGuard.Instance.Clear(); }

                        // Target ready now
                        return new SchedulerPlan(atTime, projects, selectedTarget, instructions, !checkCondition);
                    } else {
                        // If everything was rejected due to humidity, see if we're done (since it's very unlikely to improve)
                        if (!isPreview && projects?.Count > 0 && AllRejectedForHumidity(projects)) {
                            if (profilePreferences.EnableStopOnHumidity) {
                                TSLogger.Warning("all remaining targets were rejected for unacceptable humidity, ending for the night");
                                Notification.ShowWarning("Target Scheduler: all remaining targets were rejected for unacceptable humidity, ending for the night");
                                return null;
                            } else {
                                ITarget target = NextRejectedForHumidity(projects);
                                if (target != null) {
                                    TSLogger.Info("all remaining targets were rejected for unacceptable humidity, waiting 15 minutes and trying again");
                                    return new SchedulerPlan(atTime, projects, target, 15 * 60, !checkCondition);
                                }
                            }
                        }

                        ITarget nextTarget = GetNextPossibleTarget(projects);
                        if (nextTarget != null) {
                            // Wait for next possible target
                            return new SchedulerPlan(atTime, projects, nextTarget, !checkCondition);
                        } else if (SkippedTargetToWaitFor(projects) is (ITarget skipped, int waitSeconds)) {
                            TSLogger.Info($"only skipped targets remain, waiting {waitSeconds}s for {skipped.Name}'s skip to end");
                            return new SchedulerPlan(atTime, projects, skipped, waitSeconds, !checkCondition);
                        } else {
                            // Otherwise done for the night
                            TSLogger.Info("Scheduler Planner: no target selected");
                            return null;
                        }
                    }
                } catch (Exception ex) {
                    if (ex is SequenceEntityFailedException) {
                        throw;
                    }

                    TSLogger.Error($"exception generating plan: {ex.Message}\n{ex.StackTrace}");
                    throw new SequenceEntityFailedException($"Scheduler: exception generating plan: {ex.Message}", ex);
                } finally {
                    TSLogger.Info($"-- END {title} -----------------------------------------------------");
                }
            }
        }

        private SkipList ActiveSkips() {
            return Skips ?? Remote.Plugin.RemoteHost.Instance?.Control.Skips;
        }

        private (ITarget, int)? SkippedTargetToWaitFor(List<IProject> projects) {
            var host = Remote.Plugin.RemoteHost.Instance;
            var skips = ActiveSkips();
            if (host == null || skips == null || projects == null) { return null; }
            var nightEnd = host.Control.NightEnd(atTime.ToUniversalTime());
            ITarget best = null;
            DateTime bestUntil = DateTime.MaxValue;
            foreach (IProject project in projects) {
                foreach (ITarget target in project.Targets) {
                    var skip = skips.Match(project.DatabaseId, target.DatabaseId, atTime.ToUniversalTime());
                    if (skip == null || skip.Until >= bestUntil) { continue; }
                    if (nightEnd.HasValue && skip.Until >= nightEnd.Value) { continue; }
                    best = target;
                    bestUntil = skip.Until;
                }
            }
            if (best == null) { return null; }
            int seconds = (int)Math.Ceiling((bestUntil - atTime.ToUniversalTime()).TotalSeconds) + 5;
            return (best, Math.Max(60, seconds));
        }

        public List<IProject> FilterForSkips(List<IProject> projects) {
            var skips = ActiveSkips();
            if (NoProjects(projects) || skips == null) { return projects; }

            foreach (IProject project in projects) {
                foreach (ITarget target in project.Targets) {
                    string reason = SkipList.Reason(skips.Match(project.DatabaseId, target.DatabaseId, atTime));
                    if (reason != null) {
                        SetRejected(target, reason);
                    }
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Review the project list and reject those projects that are already complete.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public List<IProject> FilterForIncomplete(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }

            foreach (IProject project in projects) {
                if (!ProjectIsInComplete(project)) {
                    SetRejected(project, Reasons.ProjectComplete);
                    foreach (ITarget target in project.Targets) {
                        SetRejected(target, Reasons.TargetComplete);
                    }
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Review each project and the list of associated targets: reject those targets that are not visible.  If all targets
        /// for the project are rejected, mark the project rejected too.  A target is visible if it is above the horizon
        /// within the time window set by the most inclusive twilight over all incomplete exposure plans for that target, is clipped
        /// to any meridian window, is below the maximum altitude, and the remaining visible time is greater than the minimum imaging
        /// time preference for the project.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public List<IProject> FilterForVisibility(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }
            TargetImagingExpert targetExpert = Expert();

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }

                foreach (ITarget target in project.Targets) {
                    targetExpert.Visibility(atTime, target);
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Review each project and the list of associated targets.  For each filter plan where moon avoidance is enabled,
        /// calculate the avoidance criteria and reject as needed.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public List<IProject> FilterForMoonAvoidance(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }
            IMoonAvoidanceExpert moonExpert = new MoonAvoidanceExpert(observerInfo);
            TargetImagingExpert targetExpert = Expert();

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }

                foreach (ITarget target in project.Targets) {
                    targetExpert.MoonAvoidanceFilter(atTime, target, moonExpert);
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Reject target exposures that are not suitable for the current level of twilight.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public List<IProject> FilterForTwilight(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }
            TwilightCircumstances twilightCircumstances = TwilightCircumstances.AdjustTwilightCircumstances(observerInfo, atTime);
            TwilightLevel? currentTwilightLevel = twilightCircumstances.GetCurrentTwilightLevel(atTime);
            TargetImagingExpert targetExpert = Expert();

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }

                foreach (ITarget target in project.Targets) {
                    targetExpert.TwilightFilter(target, atTime, twilightCircumstances, currentTwilightLevel);
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Reject target exposures that are not suitable for the current level of humidity.
        /// </summary>
        /// <param name="projects"></param>
        /// <param name="weatherDataMediator"></param>
        /// <returns></returns>
        public List<IProject> FilterForHumidity(List<IProject> projects, IWeatherDataMediator weatherDataMediator) {
            if (NoProjects(projects)) { return null; }
            TargetImagingExpert targetExpert = Expert();

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }

                foreach (ITarget target in project.Targets) {
                    targetExpert.HumidityFilter(target, weatherDataMediator);
                }
            }

            return PropagateRejections(projects);
        }

        /// <summary>
        /// Determine if all projects/targets were rejected for humidity.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public bool AllRejectedForHumidity(List<IProject> projects) {
            if (NoProjects(projects)) { throw new Exception("unexpected: no projects in AllRejectedForHumidity"); }

            foreach (IProject project in projects) {
                foreach (ITarget target in project.Targets) {
                    if (!target.Rejected) { return false; }
                    if (target.RejectedReason == Reasons.TargetComplete) { continue; }
                    if (target.RejectedReason != Reasons.TargetHumidity) { return false; }
                }
            }

            return true;
        }

        /// <summary>
        /// Assuming all targets were rejected for humidity, return the one that would be starting 'now', otherwise null if later.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public ITarget NextRejectedForHumidity(List<IProject> projects) {
            if (NoProjects(projects)) { throw new Exception("unexpected: no projects in NextRejectedForHumidity"); }

            List<ITarget> targets = new List<ITarget>();
            foreach (IProject project in projects) {
                foreach (ITarget target in project.Targets) {
                    if (target.Rejected && target.RejectedReason == Reasons.TargetHumidity) { targets.Add(target); }
                }
            }

            if (targets.Count == 0) {
                TSLogger.Warning("unexpected: no targets found in NextRejectedForHumidity");
                return null;
            }

            ITarget nextTarget = targets.OrderBy(t => t.StartTime).First();
            TargetImagingExpert targetExpert = Expert();
            return targetExpert.ReadyNow(atTime, nextTarget, false) ? nextTarget : null;
        }

        /// <summary>
        /// Return the list of targets that are imagable now.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public List<ITarget> GetTargetsReadyNow(List<IProject> projects) {
            List<ITarget> targets = new List<ITarget>();

            if (NoProjects(projects)) { return targets; }
            TargetImagingExpert targetExpert = Expert();

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }

                foreach (ITarget target in project.Targets) {
                    if (targetExpert.ReadyNow(atTime, target)) {
                        targets.Add(target);
                    }
                }
            }

            return targets;
        }

        /// <summary>
        /// Determine the target (if any) that could potentially be imaged at a later time.  This takes into account
        /// both the changing visibility of the target as well as well as other circumstances (like moon impact) that
        /// may reject the target or exposure plans.
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        private static bool WaitsForManagedTwilight(ITarget target) {
            return target.RejectedReason == Reasons.TargetTwilight && target.ExposurePlans.Any(e => e.IsManaged);
        }

        public ITarget GetNextPossibleTarget(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }

            List<ITarget> potentialTargets = new List<ITarget>();

            // Find all targets that could possibly be imaged later
            foreach (IProject project in projects) {
                foreach (ITarget target in project.Targets) {
                    if (target.Rejected
                        && target.RejectedReason != Reasons.TargetNotYetVisible
                        && target.RejectedReason != Reasons.TargetMoonAvoidance
                        && target.RejectedReason != Reasons.TargetMoonKeep
                        && target.RejectedReason != Reasons.TargetAltKeep
                        && target.RejectedReason != Reasons.TargetBeforeMeridianWindow
                        && target.RejectedReason != Reasons.TargetMeridianFlipClipped
                        && target.RejectedReason != Reasons.TargetMaxAltitude
                        && target.RejectedReason != Reasons.TargetBeforeEventWindow
                        && !WaitsForManagedTwilight(target)) {
                        continue;
                    }

                    potentialTargets.Add(target);
                }
            }

            // Of the potential targets, find those that actually could be imaged given the actual circumstances
            // present when visible.  For example, moon conditions later in the night could either reject or allow
            // certain exposures - and perhaps not until later in the target's visibility time span.
            List<ITarget> imagableTargets = new List<ITarget>();
            TargetImagingExpert targetExpert = Expert();
            IMoonAvoidanceExpert moonExpert = new MoonAvoidanceExpert(observerInfo);

            foreach (ITarget target in potentialTargets) {
                targetExpert.ClearRejections(target);

                // A currently-visible target that is rejected now (e.g. moon avoidance or max altitude) still
                // carries its original visibility StartTime, which can be at or before the current planning time.
                // Floor it to atTime so CheckFuture searches forward from now rather than re-validating the target
                // at a stale, in-the-past time - which would yield a past StartTime and an invalid WAIT interval in
                // the SchedulerPlan ctor (the 'startTime must be before endTime' crash).
                if (target.StartTime < atTime) {
                    target.StartTime = atTime;
                }

                targetExpert.CheckFuture(target, moonExpert);

                // Only accept a target whose next opportunity is strictly in the future; a StartTime at or before
                // now cannot form a valid wait interval.
                if (!target.Rejected && target.StartTime > atTime) {
                    imagableTargets.Add(target);
                }
            }

            if (imagableTargets.Count == 0) {
                return null;
            }

            // Find the soonest of the imagable targets
            ITarget nextAvailableTarget = null;
            DateTime? nextAvailableTime = DateTime.MaxValue;
            foreach (ITarget target in imagableTargets) {
                if (target.StartTime < nextAvailableTime || (target.StartTime == nextAvailableTime && TieBreak(target, nextAvailableTarget) < 0)) {
                    nextAvailableTarget = target;
                    nextAvailableTime = target.StartTime;
                }
            }

            return nextAvailableTarget;
        }

        public void FilterForEventPriority(List<ITarget> readyTargets) {
            EventWindows.Prefer(readyTargets, t => t.Window(), (t, reason) => {
                TSLogger.Info($"not selecting {t.Project.Name}/{t.Name}: {reason}");
                SetRejected(t, reason);
            });
        }

        public string PreemptedBy { get; private set; }

        public bool HighEventPreempts(ITarget previousTarget) {
            if (previousTarget == null || TargetWindowTable.Current.Count == 0 || NoProjects(projects)) { return false; }
            HashSet<ITarget> candidates = new HashSet<ITarget>(
                EventWindows.Preempting(projects.SelectMany(p => p.Targets), previousTarget, t => t.DatabaseId, t => t.Window(), atTime),
                ReferenceEqualityComparer.Instance as IEqualityComparer<ITarget>);
            List<IProject> probe = projects.Where(p => p.Targets.Any(candidates.Contains)).ToList();
            if (probe.Count == 0) { return false; }

            PlanningState saved = new PlanningState(probe);
            ITarget ready = null;
            using (TSLogger.Quiet("event probe")) {
                try {
                    List<IProject> filtered = FilterForSkips(probe);
                    filtered = FilterForIncomplete(filtered);
                    filtered = FilterForVisibility(filtered);
                    filtered = FilterForMoonAvoidance(filtered);
                    filtered = FilterForTwilight(filtered);
                    filtered = FilterForHumidity(filtered, weatherDataMediator);
                    List<ITarget> readyEvents = GetTargetsReadyNow(filtered).Where(candidates.Contains).ToList();
                    SelectTargetExposures(readyEvents);
                    FilterForSequenceEnd(readyEvents);
                    ready = readyEvents.FirstOrDefault();
                } finally {
                    saved.Restore();
                }
            }
            if (ready == null) { return false; }
            PreemptedBy = $"{ready.Project.Name}/{ready.Name}";
            TSLogger.Info($"not continuing previous target at {atTime}, {previousTarget.Name}: high-priority event {PreemptedBy} is ready");
            return true;
        }

        public void FilterForSequenceEnd(List<ITarget> readyTargets) {
            if (limit == null || Common.IsEmpty(readyTargets)) {
                return;
            }

            foreach (ITarget target in readyTargets) {
                string reason = limit.FitReason(atTime, target.SelectedExposure.ExposureLength, target.CulminationTime);
                if (reason != null) {
                    TSLogger.Info($"not selecting {target.Project.Name}/{target.Name}: {reason}");
                    SetRejected(target, Reasons.TargetSequenceEnd);
                }
            }

            readyTargets.RemoveAll(t => t.Rejected && t.RejectedReason == Reasons.TargetSequenceEnd);
        }

        /// <summary>
        /// Select the best exposure plan now for each potential target.
        /// </summary>
        /// <param name="readyTargets"></param>
        /// <param name="previousTarget"></param>
        public void SelectTargetExposures(List<ITarget> readyTargets) {
            if (Common.IsEmpty(readyTargets)) {
                return;
            }

            foreach (ITarget target in readyTargets) {
                SubLengthPlanning.Apply(target, atTime);
                target.SelectedExposure = target.ExposureSelector.Select(atTime, target.Project, target);
            }

            // Remove any target where we couldn't determine the selected exposure
            readyTargets.RemoveAll(t => t.SelectedExposure == null);
        }

        /// <summary>
        /// Run the scoring engine, applying the weighted rules to determine the target with the highest score.
        /// </summary>
        /// <param name="readyTargets"></param>
        /// <param name="scoringEngine"></param>
        /// <returns></returns>
        public ITarget SelectTargetByScore(List<ITarget> readyTargets, IScoringEngine scoringEngine) {
            if (Common.IsEmpty(readyTargets)) {
                throw new ArgumentException("no ready targets in SelectTargetByScore");
            }

            if (readyTargets.Count == 1) { return readyTargets[0]; }

            ITarget highScoreTarget = null;
            double highScore = double.MinValue;
            ITarget current = RunningTarget(readyTargets, scoringEngine?.PreviousPlanTarget);
            double currentScore = 0;
            Dictionary<ITarget, double> scores = new Dictionary<ITarget, double>(ReferenceEqualityComparer.Instance as IEqualityComparer<ITarget>);

            foreach (ITarget target in readyTargets) {
                TSLogger.Trace($"running scoring engine for project/target {target.Project.Name}/{target.Name}");
                scoringEngine.RuleWeights = target.Project.RuleWeights;
                double score = scoringEngine.ScoreTarget(target);
                scores[target] = score;
                if (ReferenceEquals(target, current)) { currentScore = score; }
                if (score > highScore || (score == highScore && highScoreTarget != null && TieBreak(target, highScoreTarget) < 0)) {
                    highScoreTarget = target;
                    highScore = score;
                }
            }

            ITarget challenger = null;
            if (current != null && !ReferenceEquals(current, highScoreTarget) && WithinTieBand(highScore, currentScore)) {
                challenger = highScoreTarget;
                highScoreTarget = current;
                string held = HeldNote(current, currentScore, challenger, highScore);
                if (current.ScoringResults != null) { current.ScoringResults.Held = held; }
                TSLogger.Info(string.Create(CultureInfo.InvariantCulture,
                    $"kept {current.Project?.Name}/{current.Name}: challenger {challenger.Project?.Name}/{challenger.Name} within {TieBand * 100:0.#}% ({highScore:0.#####} vs {currentScore:0.#####})"));
            }

            foreach (ITarget target in readyTargets) {
                if (target != highScoreTarget) {
                    target.Rejected = true;
                    target.RejectedReason = challenger != null && (ReferenceEquals(target, challenger) || scores[target] > currentScore) ? Reasons.TargetHeldByRunning : Reasons.TargetLowerScore;
                }
            }

            return highScoreTarget;
        }

        /// <summary>
        /// The share of the running target's score a challenger must beat it by to replace it at a re-plan.
        /// </summary>
        public const double TieBand = 0.01;

        public static bool WithinTieBand(double challenger, double current) {
            return challenger <= current + Math.Abs(current) * TieBand;
        }

        private static ITarget RunningTarget(List<ITarget> readyTargets, ITarget previous) {
            if (previous == null) { return null; }
            return readyTargets.FirstOrDefault(t => ReferenceEquals(t, previous))
                ?? readyTargets.FirstOrDefault(t => t.DatabaseId == previous.DatabaseId && (t.Project?.DatabaseId ?? 0) == (previous.Project?.DatabaseId ?? 0));
        }

        public static string HeldNote(ITarget current, double currentScore, ITarget challenger, double challengerScore) {
            double percent = currentScore == 0 ? 0 : (challengerScore - currentScore) / Math.Abs(currentScore) * 100;
            string gap = percent < 0.005 ? "scored the same" : string.Create(CultureInfo.InvariantCulture, $"scored only {percent:0.##}% higher");
            return string.Create(CultureInfo.InvariantCulture,
                $"{challenger.Name} {gap} ({challengerScore:0.0000} vs {currentScore:0.0000}), and a switch needs more than {TieBand * 100:0.#}%, so {current.Name} kept running");
        }

        /// <summary>
        /// Orders targets that tie on score or start time by project and target id, so the pick does not depend
        /// on the order the database returned them in.
        /// </summary>
        public static int TieBreak(ITarget a, ITarget b) {
            if (b == null) { return -1; }
            int byProject = (a.Project?.DatabaseId ?? 0).CompareTo(b.Project?.DatabaseId ?? 0);
            return byProject != 0 ? byProject : a.DatabaseId.CompareTo(b.DatabaseId);
        }

        private List<IProject> GetProjects() {
            try {
                SchedulerDatabaseInteraction database = new SchedulerDatabaseInteraction();
                SchedulerPlanLoader loader = new SchedulerPlanLoader(activeProfile);
                return loader.LoadActiveProjects(database.GetContext());
            } catch (Exception ex) {
                TSLogger.Error($"exception reading database: {ex.StackTrace}");
                throw new SequenceEntityFailedException($"Scheduler: exception reading database: {ex.Message}", ex);
            }
        }

        private void HandleTargetSwitch(ITarget previousTarget, ITarget selectedTarget) {
            if (previousTarget == null) return;

            // If we're switching targets, we need to clear any cached dither manager for
            // the previous target so it starts fresh if selected in the future
            if (selectedTarget.DatabaseId != previousTarget.DatabaseId) {
                DitherManagerCache.Remove(previousTarget);
            }
        }

        public bool HasActiveProjects(List<IProject> projects) {
            if (projects == null) {
                projects = GetProjects();
            }

            if (NoProjects(projects)) {
                return false;
            }

            foreach (IProject project in projects) {
                if (ProjectIsInComplete(project)) {
                    return true;
                }
            }

            return false;
        }

        private bool NoProjects(List<IProject> projects) {
            return projects == null || projects.Count == 0;
        }

        private void SetRejected(IProject project, string reason) {
            project.Rejected = true;
            project.RejectedReason = reason;
        }

        private void SetRejected(ITarget target, string reason) {
            target.Rejected = true;
            target.RejectedReason = reason;
        }

        private void SetRejected(IExposure exposure, string reason) {
            exposure.Rejected = true;
            exposure.RejectedReason = reason;
        }

        public List<IProject> PropagateRejections(List<IProject> projects) {
            if (NoProjects(projects)) { return null; }

            foreach (IProject project in projects) {
                if (project.Rejected) { continue; }
                bool projectRejected = true;

                foreach (ITarget target in project.Targets) {
                    if (target.Rejected) { continue; }
                    if (target.ExposurePlans.All(ep => ep.Rejected)) { SetRejected(target, GetTargetExposureRejectReason(target)); }
                    if (!target.Rejected) { projectRejected = false; }
                }

                if (projectRejected) {
                    SetRejected(project, Reasons.ProjectAllTargets);
                }
            }

            return projects;
        }

        public string GetTargetExposureRejectReason(ITarget target) {
            if (target.ExposurePlans.All(ep => ep.RejectedReason == Reasons.FilterComplete)) { return Reasons.TargetComplete; }
            if (target.ExposurePlans.All(ep => ep.RejectedReason == Reasons.FilterTwilight)) { return Reasons.TargetTwilight; }
            if (target.ExposurePlans.All(ep => ep.RejectedReason == Reasons.FilterMoonAvoidance)) { return Reasons.TargetMoonAvoidance; }
            if (MoonKeep.Waits(target.ExposurePlans.Select(ep => ep.RejectedReason), Reasons.FilterMoonAvoidance, Reasons.FilterTwilight, Reasons.FilterHumidity, Reasons.FilterComplete, Reasons.FilterAltKeep)) { return Reasons.TargetMoonKeep; }
            if (AltKeep.Waits(target.ExposurePlans.Select(ep => ep.RejectedReason), Reasons.FilterMoonAvoidance, Reasons.FilterTwilight, Reasons.FilterHumidity, Reasons.FilterComplete)) { return Reasons.TargetAltKeep; }
            if (target.ExposurePlans.All(ep => ep.RejectedReason == Reasons.FilterBodyPositionUnknown)) { return Reasons.TargetBodyPositionUnknown; }
            if (target.ExposurePlans.All(ep => ep.RejectedReason == Reasons.FilterHumidity)) { return Reasons.TargetHumidity; }
            return Reasons.TargetAllExposurePlans; // implicit assumption that all are indeed rejected
        }

        private bool ProjectIsInComplete(IProject project) {
            bool incomplete = false;

            foreach (ITarget target in project.Targets) {
                foreach (IExposure exposure in target.ExposurePlans) {
                    if (exposure.NeededExposures() > 0) {
                        incomplete = true;
                    } else {
                        SetRejected(exposure, Reasons.FilterComplete);
                    }
                }
            }

            return incomplete;
        }
    }

    internal sealed class PlanningState {
        private readonly List<Action> restore = new List<Action>();

        public PlanningState(IEnumerable<IProject> projects) {
            foreach (IProject p in projects) {
                bool rejected = p.Rejected;
                string reason = p.RejectedReason;
                restore.Add(() => { p.Rejected = rejected; p.RejectedReason = reason; });
                foreach (ITarget t in p.Targets) {
                    Save(t);
                    foreach (IExposure e in (t.AllExposurePlans ?? new List<IExposure>()).Concat(t.ExposurePlans ?? new List<IExposure>()).Distinct()) {
                        Save(e);
                    }
                }
            }
        }

        private void Save(ITarget t) {
            bool rejected = t.Rejected;
            string reason = t.RejectedReason;
            var scoring = t.ScoringResults;
            IExposure selected = t.SelectedExposure;
            DateTime start = t.StartTime, end = t.EndTime, minimum = t.MinimumTimeSpanEnd, bonus = t.BonusTimeSpanEnd, culmination = t.CulminationTime;
            TimeInterval meridian = t.MeridianWindow;
            restore.Add(() => {
                t.Rejected = rejected;
                t.RejectedReason = reason;
                t.ScoringResults = scoring;
                t.SelectedExposure = selected;
                t.StartTime = start;
                t.EndTime = end;
                t.MinimumTimeSpanEnd = minimum;
                t.BonusTimeSpanEnd = bonus;
                t.CulminationTime = culmination;
                t.MeridianWindow = meridian;
            });
        }

        private void Save(IExposure e) {
            bool rejected = e.Rejected;
            string reason = e.RejectedReason;
            double moon = e.MoonAvoidanceScore, length = e.ExposureLength;
            string source = e.LengthSource, bucket = e.LengthBucket;
            bool preDither = e.PreDither;
            restore.Add(() => {
                e.Rejected = rejected;
                e.RejectedReason = reason;
                e.MoonAvoidanceScore = moon;
                e.ExposureLength = length;
                e.LengthSource = source;
                e.LengthBucket = bucket;
                e.PreDither = preDither;
            });
        }

        public void Restore() {
            restore.ForEach(a => a());
        }
    }

    public class Reasons {
        public const string ProjectComplete = "complete";
        public const string ProjectNoVisibleTargets = "no visible targets";
        public const string ProjectMoonAvoidance = "moon avoidance";
        public const string ProjectAllTargets = "all targets rejected";

        public const string TargetComplete = "complete";
        public const string TargetNeverRises = "never rises at location";
        public const string TargetNotVisible = "not visible";
        public const string TargetBodyPositionUnknown = "moving body position unknown";
        public const string TargetNotYetVisible = "not yet visible";
        public const string TargetMaxAltitude = "max altitude";
        public const string TargetMeridianWindowClipped = "clipped by meridian window";
        public const string TargetBeforeMeridianWindow = "before meridian window";
        public const string TargetMeridianFlipClipped = "clipped by meridian flip safety";
        public const string TargetTwilight = "all exposure plans rejected for twilight";
        public const string TargetMoonAvoidance = "all exposure plans rejected for moon avoidance";
        public const string TargetMoonKeep = MoonKeep.TargetReason;
        public const string TargetAltKeep = AltKeep.TargetReason;
        public const string TargetHumidity = "all exposure plans rejected for humidity";
        public const string TargetLowerScore = "lower score";
        public const string TargetHeldByRunning = Remote.Api.PreviewBuilder.HeldByRunning;
        public const string TargetAllExposurePlans = "all exposure plans rejected";
        public const string TargetSequenceEnd = "exposure ends after the sequence ends imaging";
        public const string TargetBeforeEventWindow = EventWindows.ReasonBefore;
        public const string TargetOutsideEventWindow = EventWindows.ReasonOutside;

        public const string FilterComplete = "complete";
        public const string FilterMoonAvoidance = "moon avoidance";
        public const string FilterMoonKeep = MoonKeep.FilterReason;
        public const string FilterAltKeep = AltKeep.FilterReason;
        public const string FilterBodyPositionUnknown = "moving body position unknown";
        public const string FilterTwilight = "twilight";
        public const string FilterHumidity = "humidity";
        public const string FilterNoExposuresPlanned = "no exposures planned";

        private Reasons() {
        }
    }
}