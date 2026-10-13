using NINA.Astrometry;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NINA.Plugin.TargetScheduler.Sequencer;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class RemoteHost : IRemoteBackend, ICommandContext {
        private static readonly object initLock = new object();
        public static RemoteHost Instance { get; private set; }

        private readonly object sync = new object();
        private readonly IProfileService profileService;
        private readonly string connectionString;
        private readonly SqlSchedulerDatabase database;
        private readonly SqlCommandStore store;
        private Timer timer;
        private readonly Dictionary<int, ApiServer> servers = new Dictionary<int, ApiServer>();
        private volatile bool inPlan;
        private volatile bool containerRunning;
        private volatile bool containerPaused;
        private StatusTarget target;
        private readonly ExposureTracker tracker;
        private ISequenceContainer currentPlan;
        private string lastActivity;
        private StatusWait wait;
        private List<RuleScore> scores;
        private double? scoreTotal;
        private string scoreNote;
        private DateTime? lastPlanAt;
        private ITarget pickedTarget;
        private TargetSnapshot pickedSnapshot;
        private ImagingLimit liveLimit;
        private TargetSchedulerContainer container;
        private CancellationTokenSource waitCanceller;
        private readonly IFocuserMediator focuserMediator;
        private readonly AutofocusLatest autofocus = new AutofocusLatest();
        private AutofocusObserver autofocusObserver;
        private readonly IFilterWheelMediator filterWheelMediator;
        private LiveAutofocusProbe liveAutofocus;

        public RemoteSettings Settings { get; }
        public SchedulerControl Control { get; }
        public CommandProcessor Processor { get; }

        public event Action<string, object> Pushed;

        public DateTime Now => DateTime.UtcNow;
        public string ProfileId => profileService?.ActiveProfile?.Id.ToString();

        public IReadOnlyCollection<string> ProfileFilters {
            get {
                try {
                    var filters = profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                    return filters == null ? Array.Empty<string>() : filters.Select(f => f?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                } catch (Exception e) {
                    TSLogger.Warning($"could not read the profile's filter wheel filters, using the filters of its templates: {e.Message}");
                    return Array.Empty<string>();
                }
            }
        }
        public ISchedulerDatabase Database => database;
        public IReadOnlyDictionary<string, double> DefaultRuleWeights => Planning.Scoring.Rules.ScoringRule.GetDefaultRuleWeights().ToDictionary(w => w.name, w => w.weight);
        ISchedulerControl ICommandContext.Control => Control;

        public static void Start(IProfileService profileService, IFocuserMediator focuserMediator = null, IFilterWheelMediator filterWheelMediator = null) {
            lock (initLock) {
                if (Instance != null) {
                    return;
                }
                try {
                    Instance = new RemoteHost(profileService, focuserMediator, filterWheelMediator);
                    Instance.Run();
                } catch (Exception e) {
                    TSLogger.Error($"remote control failed to start: {e}");
                    Instance = null;
                }
            }
        }

        public static void Shutdown() {
            lock (initLock) {
                Instance?.Stop();
                Instance = null;
            }
        }

        private RemoteHost(IProfileService profileService, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator) {
            this.profileService = profileService;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            connectionString = $"Data Source={Path.Combine(Common.PLUGIN_HOME, "schedulerdb.sqlite")};";
            database = new SqlSchedulerDatabase(Connect);
            tracker = new ExposureTracker(null, (t, since) => database.CountAcquired(t, since));
            store = new SqlCommandStore(Connect);
            Settings = new RemoteSettings(Path.Combine(Common.PLUGIN_HOME, "observatory-scheduler.json"));
            Control = new SchedulerControl {
                NightEndSource = NightEnd,
                Invalidated = Invalidate,
            };
            Control.Skips.Load(Settings.Snapshot().Skips, DateTime.UtcNow);
            Control.Changed = OnControlChanged;
            Processor = new CommandProcessor(CommandRegistry.Builtin(), store, this, () => Settings.Snapshot().WebEditing, () => inPlan, () => tracker.Pending(DateTime.UtcNow)) {
                Log = m => TSLogger.Info($"remote: {m}"),
            };
            Processor.ResultChanged += r => {
                Push("result", r);
                Push("status", null);
            };
            Settings.Changed += ApplySettings;
            autofocus.Changed += () => Push("status", null);
        }

        private DbConnection Connect() {
            var c = new SQLiteConnection(connectionString) { BusyTimeout = 5000 };
            c.Open();
            return c;
        }

        private void Run() {
            store.EnsureSchema();
            ApplySettings();
            timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            var ev = TargetScheduler.EventMediator;
            ev.ContainerStarting += (s, e) => { containerRunning = true; Push("status", null); };
            ev.ContainerStopping += (s, e) => { containerRunning = false; inPlan = false; ClearActivity(); Push("status", null); };
            ev.ContainerPaused += (s, e) => { containerPaused = true; Push("status", null); };
            ev.ContainerUnpaused += (s, e) => { containerPaused = false; Push("status", null); };
            ev.WaitStarting += (s, e) => OnWaitStarting(e);
            ev.WaitStopping += (s, e) => { lock (sync) { wait = null; } Push("status", null); };
            ev.ExposureStarting += (s, e) => OnExposureStarting(e);
            ev.ExposureStopping += (s, e) => { EndPlan(); Push("status", null); };
            StartAutofocusObserver();
            TSLogger.Info("remote control started");
        }

        private void StartAutofocusObserver() {
            try {
                autofocusObserver = new AutofocusObserver(focuserMediator, new AutofocusStore(Connect), autofocus, () => ProfileId, CurrentTarget);
                autofocusObserver.Start();
            } catch (Exception e) {
                TSLogger.Error($"autofocus observer failed to start: {e}");
            }
            try {
                liveAutofocus = new LiveAutofocusProbe(filterWheelMediator, () => Settings.Snapshot().LiveAutofocus, () => Push("status", null));
                liveAutofocus.Start();
            } catch (Exception e) {
                TSLogger.Error($"live autofocus probe failed to start: {e}");
            }
        }

        private (string Name, string Guid) CurrentTarget() {
            lock (sync) {
                return (target?.TargetName, target?.TargetGuid);
            }
        }

        private void Stop() {
            liveAutofocus?.Stop();
            autofocusObserver?.Stop();
            timer?.Dispose();
            lock (sync) {
                foreach (var a in servers.Values) {
                    a.Stop();
                }
            }
        }

        private void ApplySettings() {
            var s = Settings.Snapshot();
            LocalEditing.Instance.Allowed = !s.WebEditing;
            lock (sync) {
                foreach (var kv in servers) {
                    if (!s.ApiEnabled || kv.Key != s.ApiPort) {
                        if (kv.Value.Running) {
                            kv.Value.Stop();
                            TSLogger.Info($"remote API on port {kv.Key} turned off");
                        }
                    }
                }
                if (s.ApiEnabled) {
                    try {
                        if (!servers.TryGetValue(s.ApiPort, out var a)) {
                            a = new ApiServer(this, $"http://*:{s.ApiPort}/", () => Settings.Snapshot().ApiToken, m => TSLogger.Warning(m));
                            servers[s.ApiPort] = a;
                        }
                        if (!a.Running) {
                            a.Start();
                            TSLogger.Info($"remote API listening on port {s.ApiPort}");
                        }
                    } catch (Exception e) {
                        TSLogger.Error($"remote API failed to start on port {s.ApiPort}: {e.Message}");
                    }
                }
            }
            Push("status", null);
        }

        private void Tick() {
            try {
                Processor.Poll();
                if (!inPlan) {
                    Processor.ApplyPending();
                }
                var activity = CurrentView().Activity;
                if (activity != lastActivity) {
                    lastActivity = activity;
                    Push("status", null);
                }
                if (Control.ShouldResume(DateTime.UtcNow)) {
                    TSLogger.Info("remote: pause time is up, resuming");
                    OnControlChanged();
                }
            } catch (Exception e) {
                TSLogger.Warning($"remote tick failed: {e.Message}");
            }
        }

        private void OnControlChanged() {
            Settings.Update(d => d.Skips = Control.Skips.Serialize(DateTime.UtcNow));
            EndWaitIfReplanning();
            var c = container;
            if (c != null) {
                if (Control.Paused != null && !c.ContainerPaused && !c.PauseRequested) {
                    c.RequestPause();
                } else if (Control.Paused == null && (c.ContainerPaused || c.PauseRequested)) {
                    c.RequestUnpause();
                }
            }
            Push("status", null);
        }

        public void SetWaitCanceller(CancellationTokenSource cts) {
            lock (sync) {
                waitCanceller = cts;
            }
            EndWaitIfReplanning();
        }

        public bool ReplanOrPauseRequested => Control.ReplanRequested || Control.Paused != null;

        private void EndWaitIfReplanning() {
            CancellationTokenSource cts;
            lock (sync) {
                cts = waitCanceller;
            }
            if (cts != null && (Control.ReplanRequested || Control.Paused != null)) {
                try {
                    cts.Cancel();
                } catch (ObjectDisposedException) {
                    return;
                }
            }
        }

        public void Attach(TargetSchedulerContainer c) {
            container = c;
            if (Control.Paused != null && !c.PauseRequested) {
                c.RequestPause();
            }
        }

        public void LocalPauseChanged(bool paused) {
            if (!paused && Control.Paused != null) {
                Control.Resume();
            }
        }

        public void AtPlanBoundary() {
            inPlan = false;
            try {
                Processor.Poll();
                Processor.ApplyPending();
            } catch (Exception e) {
                TSLogger.Warning($"remote boundary failed: {e.Message}");
            }
        }

        public void PlanStarting(ITarget planTarget, ITarget previous, ISequenceContainer plan) {
            if (!ReferenceEquals(planTarget, previous)) {
                RememberPick(planTarget);
            }
            lock (sync) {
                currentPlan = plan;
            }
            tracker.PlanStarted(planTarget.DatabaseId);
            inPlan = true;
        }

        public void PlanEnded() {
            inPlan = false;
            EndPlan();
        }

        private void EndPlan() {
            lock (sync) {
                currentPlan = null;
            }
            tracker.PlanEnded();
        }

        public CaptureKey ShutterOpening(IExposure x, string abortBlocked = null, bool syncClients = false) {
            if (x == null) {
                return null;
            }
            Control.Capture.Blocked = abortBlocked;
            var opened = tracker.ShutterOpened(x.DatabaseId, x.FilterName, x.ExposureLength, DateTime.UtcNow, abortBlocked, syncClients, x.LengthSource, x.LengthBucket);
            Processor.RefreshPending();
            Push("status", null);
            return new CaptureKey { PlanId = opened.PlanId, StartedAt = opened.StartedAt };
        }

        public void CaptureEnded(bool aborted = false) {
            Control.Capture.Blocked = null;
            if (aborted) {
                tracker.CaptureAborted();
            } else {
                tracker.CaptureEnded();
            }
            Processor.RefreshPending();
            Push("status", null);
        }

        private ExposureView CurrentView() {
            ISequenceContainer plan;
            List<ISequenceContainer> extra;
            lock (sync) {
                plan = currentPlan;
                var c = container;
                extra = c == null ? null : new List<ISequenceContainer> { c.AfterEachExposureContainer, c.AfterTargetCompleteContainer };
            }
            var steps = tracker.InPlan ? ActivityProbe.Running(plan, extra) : null;
            return tracker.View(DateTime.UtcNow, steps);
        }

        public void PlanComputed(ImagingLimit limit) {
            lastPlanAt = DateTime.UtcNow;
            lock (sync) {
                liveLimit = limit;
            }
            Control.TakeReplan();
        }

        /// <summary>
        /// The end of imaging the running sequence planned with at its last re-plan, or null.
        /// </summary>
        public ImagingLimit LiveLimit {
            get {
                lock (sync) {
                    return liveLimit;
                }
            }
        }

        public bool ReplanRequested => Control.ReplanRequested;

        private void RememberPick(ITarget t) {
            lock (sync) {
                pickedTarget = t;
                pickedSnapshot = new TargetSnapshot {
                    ProjectId = t.Project.DatabaseId,
                    TargetId = t.DatabaseId,
                    ProjectState = (int)t.Project.State,
                    ProjectPriority = (int)t.Project.Priority,
                    TargetActive = true,
                    EnabledPlanIds = new HashSet<int>(t.AllExposurePlans.Where(e => e.IsEnabled).Select(e => e.DatabaseId)),
                };
                scores = t.ScoringResults?.Results?.Select(r => new RuleScore { Rule = r.ScoringRule.Name, Weight = r.Weight, Score = r.Score }).ToList();
                scoreTotal = t.ScoringResults?.TotalScore;
                scoreNote = t.ScoringResults?.Held;
            }
        }

        public string ContinuationBlocker(ITarget previous, DateTime atTime) {
            if (previous == null) {
                return null;
            }
            if (Control.ReplanRequested) {
                return "a re-plan was requested from the web";
            }
            var skip = Control.Skips.Match(previous.Project.DatabaseId, previous.DatabaseId, atTime.ToUniversalTime());
            if (skip != null) {
                return $"{skip.Name} is skipped until {skip.Until.ToLocalTime():HH:mm}";
            }
            TargetSnapshot picked;
            lock (sync) {
                picked = ReferenceEquals(previous, pickedTarget) ? pickedSnapshot : null;
            }
            if (picked == null) {
                picked = new TargetSnapshot {
                    ProjectState = (int)previous.Project.State,
                    ProjectPriority = (int)previous.Project.Priority,
                    TargetActive = true,
                    EnabledPlanIds = null,
                };
            }
            try {
                return ContinuationGuard.Reason(picked, database.ReadSnapshot(previous.DatabaseId));
            } catch (Exception e) {
                TSLogger.Warning($"remote: could not re-check {previous.Name}: {e.Message}");
                return null;
            }
        }

        public string SkipReason(int projectId, int targetId, DateTime atTime) {
            return SkipList.Reason(Control.Skips.Match(projectId, targetId, atTime.ToUniversalTime()));
        }

        private void Invalidate(int? projectId, int? targetId) {
            if (targetId.HasValue) {
                SmartExposureRotateCache.Remove(targetId.Value.ToString());
            }
            SeasonWorker.Instance?.RequestPass("web edit");
            ITarget current;
            lock (sync) {
                current = pickedTarget;
            }
            if (current != null && (current.DatabaseId == targetId || current.Project.DatabaseId == projectId)) {
                TargetEditGuard.Instance.MarkEdited(current.DatabaseId);
            }
        }

        private ObserverInfo Observer() {
            var a = profileService.ActiveProfile.AstrometrySettings;
            return new ObserverInfo { Latitude = a.Latitude, Longitude = a.Longitude, Elevation = a.Elevation };
        }

        public TwilightCircumstances TwilightFor(DateTime localTime) {
            var date = localTime.Hour < 12 ? localTime.Date.AddDays(-1) : localTime.Date;
            return new TwilightCircumstances(Observer(), date);
        }

        private DateTime? NightEnd(DateTime nowUtc) {
            try {
                var tc = TwilightFor(nowUtc.ToLocalTime());
                var end = tc.NauticalTwilightEnd ?? tc.CivilTwilightEnd;
                return end?.ToUniversalTime();
            } catch (Exception) {
                return null;
            }
        }

        private void OnWaitStarting(WaitStartingEventArgs e) {
            lock (sync) {
                wait = new StatusWait { Until = e.WaitUntil.ToUniversalTime(), TargetName = e.Target?.Name };
                target = null;
            }
            tracker.PlanEnded();
            Push("status", null);
        }

        private void OnExposureStarting(ExposureStartingEventArgs e) {
            var t = e.Target;
            tracker.PlanStarted(t.DatabaseId);
            lock (sync) {
                wait = null;
                target = new StatusTarget {
                    ProjectId = t.Project.DatabaseId,
                    ProjectName = t.Project.Name,
                    ProjectGuid = t.Project.Guid,
                    TargetId = t.DatabaseId,
                    TargetName = t.Name,
                    TargetGuid = t.Guid,
                    Priority = (int)t.Project.Priority,
                    IsMosaic = t.Project.IsMosaic,
                    RaHours = t.Coordinates?.RA ?? 0,
                    DecDegrees = t.Coordinates?.Dec ?? 0,
                    Rotation = t.Rotation,
                    PickedAt = pickedTarget == t ? lastPlanAt : target?.PickedAt ?? lastPlanAt,
                    MinimumTimeEnd = t.MinimumTimeSpanEnd == default ? null : t.MinimumTimeSpanEnd.ToUniversalTime(),
                    HardStop = t.EndTime == default ? null : t.EndTime.ToUniversalTime(),
                };
            }
            Push("status", null);
        }

        private void ClearActivity() {
            lock (sync) {
                target = null;
                wait = null;
            }
            tracker.PlanEnded();
        }

        public Status Status() {
            var s = Settings.Snapshot();
            var p = Control.Paused;
            var pending = Processor.Pending.Select(x => new StatusPending { Id = x.Command.Id, Kind = x.Command.Kind, ReceivedAt = x.ReceivedAt }).ToList();
            var skips = Control.Skips.Active(DateTime.UtcNow);
            var view = CurrentView();
            var planning = tracker.InPlan;
            lock (sync) {
                string state;
                if (!containerRunning) {
                    state = "stopped";
                } else if (containerPaused) {
                    state = "paused";
                } else if (planning || view.Exposure != null) {
                    state = "imaging";
                } else if (wait != null) {
                    state = "waiting";
                } else {
                    state = "idle";
                }
                return new Status {
                    Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                    Time = DateTime.UtcNow,
                    WebEditing = s.WebEditing,
                    State = state,
                    Paused = containerPaused,
                    PauseRequested = container?.PauseRequested ?? false,
                    Pause = p == null ? null : new StatusPause { Mount = p.Mount, ResumeAt = p.ResumeAt },
                    Target = target,
                    Exposure = view.Exposure,
                    Activity = view.Activity,
                    ActivityDetail = view.ActivityDetail,
                    ActivitySince = view.ActivitySince,
                    Wait = wait,
                    Scores = target != null ? scores : null,
                    ScoreTotal = target != null ? scoreTotal : null,
                    ScoreNote = target != null ? scoreNote : null,
                    Skips = skips,
                    Pending = pending,
                    LastPlanAt = lastPlanAt,
                    Autofocus = liveAutofocus?.Snapshot() ?? autofocus.Snapshot(),
                };
            }
        }

        public string ApiProblem {
            get {
                var port = Settings.Snapshot().ApiPort;
                lock (sync) {
                    return servers.TryGetValue(port, out var a) ? a.Failed : null;
                }
            }
        }

        public bool MountShouldPark => Control.Paused?.Mount == "park";

        public CommandResult Submit(CommandEnvelope command) => Processor.Submit(command, "api");

        public CommandResult Get(string id) => Processor.Get(id);

        public CommandResult Cancel(string id) => Processor.Cancel(id);

        /// <summary>
        /// The target the running sequence is imaging, with the window the live planner holds for it and whether
        /// it keeps the target at its next re-plan. Between two plans of the same sequence the last target is
        /// still the live planner's current one, so it is reported then too.
        /// </summary>
        public LiveImaging Imaging() {
            var pending = Processor.Pending;
            var view = CurrentView();
            LiveImaging live;
            ITarget picked;
            lock (sync) {
                if (!containerRunning || containerPaused || target == null) {
                    return null;
                }
                live = new LiveImaging {
                    TargetId = target.TargetId,
                    TargetName = target.TargetName,
                    PlanId = view.Exposure?.PlanId,
                    Filter = view.Exposure?.Filter,
                    StartedAt = view.Exposure?.StartedAt,
                    EndsAt = view.Exposure?.EndsAt,
                };
                picked = pickedTarget != null && pickedTarget.DatabaseId == target.TargetId ? pickedTarget : null;
            }
            live.PauseWaiting = pending.Any(p => p.Command.Kind == CommandKinds.Pause);
            if (picked == null) {
                return live;
            }
            live.Window = new PlanWindow {
                Start = picked.StartTime,
                End = picked.EndTime,
                MinimumEnd = picked.MinimumTimeSpanEnd,
                BonusEnd = picked.BonusTimeSpanEnd,
                Culmination = picked.CulminationTime,
            };
            var at = live.EndsAt?.ToLocalTime() ?? DateTime.Now;
            live.ReplanReason = PendingPreview.ReplanReason(pending, Control.Skips)
                ?? ContinuationBlocker(picked, at)
                ?? (TargetEditGuard.Instance.Marked(picked.DatabaseId) ? "an edit changed it" : null)
                ?? BodyRecenter.EndReason(picked, at, BodyCenters.Live, BodyField.Current);
            return live;
        }

        public Preview Preview(PreviewRequest request) => new PreviewRunner(profileService, this).Run(request);

        public HorizonProfile Horizon() {
            var profile = profileService.ActiveProfile;
            var horizon = profile.AstrometrySettings.Horizon;
            return HorizonProfile.Sample(profile.Id.ToString(), horizon == null ? null : az => horizon.GetAltitude(az));
        }

        public GraderReport Grader(int? targetId) {
            int? id = targetId;
            if (id == null) {
                lock (sync) {
                    id = target?.TargetId;
                }
            }
            return id == null ? null : new GraderProbe(profileService.ActiveProfile).Report(id.Value);
        }

        private void Push(string type, object data) {
            try {
                Pushed?.Invoke(type, data);
            } catch (Exception e) {
                TSLogger.Warning($"remote push failed: {e.Message}");
            }
        }
    }
}
