using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace NINA.Plugin.TargetScheduler.Planning {

    public class SeasonWorker {
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan RequestDebounce = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan TargetYield = TimeSpan.FromMilliseconds(25);

        public static SeasonStore Store { get; } = new SeasonStore();
        public static SeasonWorker Instance { get; private set; }
        public static Func<ITargetSeasonReader> ReaderFactory { get; set; } = ForkDatabase.TargetSeasonReader;
        public static Func<ITargetSeasonWriter> WriterFactory { get; set; } = ForkDatabase.TargetSeasonWriter;
        public static Func<BodyNightTable> BodyNightsFactory { get; set; } = ForkDatabase.BodyNights;

        private static readonly object instanceLock = new object();

        private readonly IProfileService profileService;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread thread;
        private int passRequested;
        private int fullPassRequested;
        private volatile bool imaging;
        private DateTime? lastPassNight;

        public bool Imaging => imaging;

        private SeasonWorker(IProfileService profileService) {
            this.profileService = profileService;
        }

        public static void Start(IProfileService profileService) {
            lock (instanceLock) {
                if (Instance != null) { return; }
                SeasonWorker worker = new SeasonWorker(profileService);
                TargetScheduler.EventMediator.ContainerStarting += worker.OnContainerStarting;
                TargetScheduler.EventMediator.ContainerStopping += worker.OnContainerStopping;
                profileService.ProfileChanged += worker.OnProfileChanged;
                worker.thread = new Thread(worker.Loop) {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "Observatory Scheduler season worker"
                };
                Instance = worker;
                worker.thread.Start();
            }
        }

        public static void Stop() {
            SeasonWorker worker;
            lock (instanceLock) {
                worker = Instance;
                Instance = null;
            }

            if (worker == null) { return; }
            TargetScheduler.EventMediator.ContainerStarting -= worker.OnContainerStarting;
            TargetScheduler.EventMediator.ContainerStopping -= worker.OnContainerStopping;
            worker.profileService.ProfileChanged -= worker.OnProfileChanged;
            worker.cancel.Cancel();
            worker.wake.Set();
            if (!worker.thread.Join(TimeSpan.FromSeconds(5))) {
                TSLogger.Warning("season worker did not stop within 5s");
            }
        }

        public static void SeedStore() {
            if (Store.Seeded) { return; }
            try {
                Store.Seed(ReaderFactory().LoadAll());
            } catch (Exception ex) {
                TSLogger.Warning($"season worker: could not read ts_target_season: {ex.Message}");
                Store.MarkSeeded();
            }
        }

        public void RequestPass(string reason) {
            if (Interlocked.Exchange(ref passRequested, 1) == 0) {
                TSLogger.Debug($"season worker: pass requested: {reason}");
                wake.Set();
            }
        }

        public void RequestFullPass(string reason) {
            Interlocked.Exchange(ref fullPassRequested, 1);
            RequestPass(reason);
        }

        private void OnContainerStarting(object sender, EventArgs e) {
            imaging = true;
        }

        private void OnContainerStopping(object sender, EventArgs e) {
            imaging = false;
        }

        private void OnProfileChanged(object sender, EventArgs e) {
            RequestFullPass("profile changed");
        }

        private void Loop() {
            CancellationToken token = cancel.Token;
            try {
                SeedStore();
                SafePass(true, token);

                while (!token.IsCancellationRequested) {
                    int signalled = WaitHandle.WaitAny(new[] { token.WaitHandle, wake }, CheckInterval);
                    if (token.IsCancellationRequested) { break; }
                    if (signalled == 1 && token.WaitHandle.WaitOne(RequestDebounce)) { break; }

                    bool full = Interlocked.Exchange(ref fullPassRequested, 0) == 1;
                    bool requested = Interlocked.Exchange(ref passRequested, 0) == 1;
                    if (full || DailyPassDue()) {
                        SafePass(true, token);
                    } else if (requested) {
                        SafePass(false, token);
                    }
                }
            } catch (Exception ex) {
                TSLogger.Error($"season worker stopped: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private bool DailyPassDue() {
            try {
                DateTime night = SeasonCalculator.NightDate(Observer(), DateTime.Now);
                return SeasonSchedule.DailyPassDue(DateTime.Now, night, lastPassNight, imaging);
            } catch (Exception ex) {
                TSLogger.Warning($"season worker: cannot determine night: {ex.Message}");
                return false;
            }
        }

        private ObserverInfo Observer() {
            IProfile profile = profileService.ActiveProfile;
            return new ObserverInfo {
                Latitude = profile.AstrometrySettings.Latitude,
                Longitude = profile.AstrometrySettings.Longitude,
                Elevation = profile.AstrometrySettings.Elevation,
            };
        }

        private void SafePass(bool full, CancellationToken token) {
            try {
                RunPass(full, token);
            } catch (Exception ex) {
                TSLogger.Warning($"season worker pass failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void RunPass(bool full, CancellationToken token) {
            IProfile profile = profileService.ActiveProfile;
            ObserverInfo observer = Observer();
            DateTime night = SeasonCalculator.NightDate(observer, DateTime.Now);

            List<Project> projects;
            using (SchedulerDatabaseContext context = new SchedulerDatabaseInteraction().GetContext()) {
                projects = context.GetActiveProjects(profile.Id.ToString());
            }

            ITargetSeasonWriter writer = WriterFactory();
            Stopwatch sw = Stopwatch.StartNew();
            int computed = 0;
            int skipped = 0;
            int noNights = 0;
            BodyNightTable bodyNights = null;

            foreach (Project project in projects ?? new List<Project>()) {
                HorizonDefinition horizon = PlanningProject.DetermineHorizon(profile, project);
                foreach (Target target in project.Targets.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Guid))) {
                    if (token.IsCancellationRequested) { return; }
                    string designation = TargetBodyTable.Current.Designation(target.Guid);

                    List<ExposureTemplate> templates = target.ExposurePlans
                        .Where(ep => ep.IsEnabled && ep.ExposureTemplate != null)
                        .Select(ep => ep.ExposureTemplate).ToList();
                    TwilightLevel level = SeasonCalculator.LevelFor(templates.Select(t => t.TwilightLevel));
                    SeasonConstraints constraints = SeasonCalculator.Constraints(project.MaximumAltitude, project.MeridianWindow,
                        templates.Select(t => SeasonCalculator.Moon(t.MoonAvoidanceEnabled, t.MoonAvoidanceSeparation, t.MoonAvoidanceWidth,
                            t.MoonRelaxScale, t.MoonRelaxMaxAltitude, t.MoonRelaxMinAltitude, t.MoonDownEnabled)));
                    BodyNightTrack track = null;
                    if (designation != null) {
                        bodyNights ??= LoadBodyNights();
                        track = bodyNights.Track(target.Guid, designation);
                        if (!track.HasNight(night)) {
                            noNights++;
                            continue;
                        }
                    }

                    string key = track == null
                        ? SeasonCalculator.Key(observer, target.Coordinates, horizon, project.MinimumTime, level, constraints)
                        : SeasonCalculator.BodyKey(observer, designation, track, horizon, project.MinimumTime, level, constraints);
                    if (!full && Store.IsCurrent(target.Guid, key, night)) {
                        skipped++;
                        continue;
                    }

                    SeasonResult season = track == null
                        ? SeasonCalculator.Compute(observer, target.Coordinates, horizon, level, project.MinimumTime, night, constraints)
                        : SeasonCalculator.ComputeBody(observer, track, horizon, level, project.MinimumTime, night, constraints);
                    Store.Put(new SeasonEntry { TargetGuid = target.Guid, Key = key, Season = season, ComputedFor = night });
                    computed++;

                    try {
                        writer.Upsert(TargetSeasonRow.From(target.Guid, season, night, DateTime.UtcNow));
                    } catch (Exception ex) {
                        TSLogger.Warning($"season worker: failed to record season for {project.Name}/{target.Name}: {ex.Message}");
                    }

                    if (token.WaitHandle.WaitOne(TargetYield)) { return; }
                }
            }

            if (full) { lastPassNight = night; }
            TSLogger.Info($"season worker: {(full ? "full" : "incremental")} pass for night {night:yyyy-MM-dd}: computed {computed}, current {skipped}, moving bodies without season nights {noNights}, {sw.ElapsedMilliseconds}ms");
        }

        private static BodyNightTable LoadBodyNights() {
            try {
                return BodyNightsFactory() ?? BodyNightTable.Empty;
            } catch (Exception ex) {
                TSLogger.Warning($"season worker: could not read {BodyNightStore.Table}, moving bodies get no season: {ex.Message}");
                return BodyNightTable.Empty;
            }
        }
    }
}
