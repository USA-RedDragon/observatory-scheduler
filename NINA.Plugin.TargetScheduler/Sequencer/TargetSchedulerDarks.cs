using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFlatDevice;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.Validations;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    [ExportMetadata("Name", "Target Scheduler Darks")]
    [ExportMetadata("Description", "Takes the bias frames the stacker's bias backlog asks for, at the current setpoint and the camera's minimum exposure, and then the darks the stacker's dark backlog asks for, at each one's set temperature, while the cover is already closed and the sun is below the light-safe altitude. It uses only the camera, and when the profile has a flat device it reads the cover state without changing it and takes nothing unless the cover reports Closed with the light off, or, with the device powered off, was last reported so while connected.")]
    [ExportMetadata("Icon", "Scheduler.SchedulerSVG")]
    [ExportMetadata("Category", "Target Scheduler")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TargetSchedulerDarks : SequenceItem, IValidatable {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly IFlatDeviceMediator flatDeviceMediator;

        [ImportingConstructor]
        public TargetSchedulerDarks(IProfileService profileService,
                                    ICameraMediator cameraMediator,
                                    IImagingMediator imagingMediator,
                                    IImageSaveMediator imageSaveMediator,
                                    IImageHistoryVM imageHistoryVM,
                                    ISafetyMonitorMediator safetyMonitorMediator,
                                    IWeatherDataMediator weatherDataMediator,
                                    IFlatDeviceMediator flatDeviceMediator) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.weatherDataMediator = weatherDataMediator;
            this.flatDeviceMediator = flatDeviceMediator;
            CoverWatch.Ensure(flatDeviceMediator);
        }

        public TargetSchedulerDarks(TargetSchedulerDarks cloneMe) : this(
            cloneMe.profileService,
            cloneMe.cameraMediator,
            cloneMe.imagingMediator,
            cloneMe.imageSaveMediator,
            cloneMe.imageHistoryVM,
            cloneMe.safetyMonitorMediator,
            cloneMe.weatherDataMediator,
            cloneMe.flatDeviceMediator) {
            CopyMetaData(cloneMe);
            Placement = cloneMe.Placement;
            StopAfterMinutes = cloneMe.StopAfterMinutes;
        }

        public override object Clone() {
            return new TargetSchedulerDarks(this);
        }

        private DarkPlacement placement = DarkPlacement.BeforeWait;

        [JsonProperty]
        public DarkPlacement Placement {
            get => placement;
            set {
                placement = value;
                RaisePropertyChanged(nameof(Placement));
                Validate();
            }
        }

        private int stopAfterMinutes;

        [JsonProperty]
        public int StopAfterMinutes {
            get => stopAfterMinutes;
            set {
                stopAfterMinutes = Math.Max(0, value);
                RaisePropertyChanged(nameof(StopAfterMinutes));
            }
        }

        private DateTime? stopBy;
        private int biasDone;
        private string coverStop;
        private CancellationTokenSource yieldCts;

        public IList<DarkPlacement> Placements => Enum.GetValues(typeof(DarkPlacement)).Cast<DarkPlacement>().ToList();

        private string displayText = "";

        public string DisplayText {
            get => displayText;
            set {
                displayText = value;
                RaisePropertyChanged(nameof(DisplayText));
            }
        }

        private string progressText = "...";

        public string ProgressText {
            get => progressText;
            set {
                progressText = value;
                RaisePropertyChanged(nameof(ProgressText));
            }
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public bool Validate() {
            var i = new List<string>();
            CameraInfo info = cameraMediator.GetInfo();
            if (!info.Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            } else if (!info.CanSetTemperature) {
                i.Add("Camera cannot set its temperature, so darks cannot be matched to the lights' set temperature");
            }

            if (SunAltitude() == null) {
                i.Add("The profile has no observer location, so the sun's altitude is not known and no darks are taken");
            }

            if (Placement == DarkPlacement.BeforeWait && FindContainer() == null) {
                i.Add("Placement 'Before Wait' needs this instruction inside the Target Scheduler container's Before Wait instructions");
            }

            Issues = i;
            return i.Count == 0;
        }

        private TargetSchedulerContainer FindContainer() {
            ISequenceContainer p = Parent;
            while (p != null) {
                if (p is TargetSchedulerContainer tsc) {
                    return tsc;
                }
                p = p.Parent;
            }
            return null;
        }

        private DarkWindow Window() {
            var w = new DarkWindow { Placement = Placement, Now = DateTime.UtcNow, StopBy = stopBy, SunAltitudeAt = SunAltitude(), Cover = ReadCover() };
            if (Placement == DarkPlacement.BeforeWait) {
                DateTime? until = FindContainer()?.CurrentWaitUntil;
                w.WaitUntil = until?.ToUniversalTime();
                w.AfterWait = DarkRuntime.State.AfterWait();
                w.ReplanOrPause = Remote.Plugin.RemoteHost.Instance?.ReplanOrPauseRequested == true;
            }
            if (Placement == DarkPlacement.WhileUnsafe) {
                var s = safetyMonitorMediator.GetInfo();
                w.Safe = s.Connected ? s.IsSafe : (bool?)null;
            }
            return w;
        }

        public DarkCover ReadCover() {
            string id = profileService.ActiveProfile?.FlatDeviceSettings?.Id;
            if (string.IsNullOrWhiteSpace(id) || id == "No_Device") {
                return new DarkCover { Configured = false };
            }
            var c = new DarkCover { Configured = true, Device = id, State = DarkCoverState.Unknown };
            try {
                FlatDeviceInfo info = flatDeviceMediator.GetInfo();
                if (info == null) {
                    c.ReadError = "no device info";
                    return c;
                }
                c.Connected = info.Connected;
                c.LightOn = info.LightOn;
                if (!string.IsNullOrWhiteSpace(info.Name)) {
                    c.Device = info.Name;
                }
                c.State = Enum.TryParse(info.CoverState.ToString(), out DarkCoverState state) ? state : DarkCoverState.Unknown;
                return DarkRuntime.Cover.Apply(c);
            } catch (Exception ex) {
                c.ReadError = ex.Message;
            }
            return c;
        }

        private void LogStop(DarkWindow w, string message) {
            string cover = DarkPlanner.CoverStopReason(w.Cover);
            if (cover != null) {
                coverStop = cover;
                TSLogger.Warning(message);
            } else {
                TSLogger.Info(message);
            }
        }

        private Func<DateTime, double?> SunAltitude() {
            var a = profileService.ActiveProfile?.AstrometrySettings;
            if (a == null || (a.Latitude == 0 && a.Longitude == 0)) {
                return null;
            }
            var observer = new ObserverInfo { Latitude = a.Latitude, Longitude = a.Longitude, Elevation = a.Elevation };
            return t => AstroUtil.GetSunAltitude(DateTime.SpecifyKind(t, DateTimeKind.Utc), observer);
        }

        private double? Ambient() {
            var w = weatherDataMediator.GetInfo();
            return w.Connected && !double.IsNaN(w.Temperature) ? w.Temperature : (double?)null;
        }

        private void OnSafeChanged(object sender, IsSafeEventArgs e) {
            if (e.IsSafe) {
                try {
                    yieldCts?.Cancel();
                } catch (ObjectDisposedException) {
                    return;
                }
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            stopBy = StopAfterMinutes > 0 ? DateTime.UtcNow.AddMinutes(StopAfterMinutes) : (DateTime?)null;
            coverStop = null;
            DisplayText = "";
            using var yieldSource = new CancellationTokenSource();
            yieldCts = yieldSource;
            if (Placement == DarkPlacement.WhileUnsafe) {
                safetyMonitorMediator.IsSafeChanged += OnSafeChanged;
            }
            try {
                await Run(progress, token, yieldSource.Token);
            } finally {
                safetyMonitorMediator.IsSafeChanged -= OnSafeChanged;
                yieldCts = null;
            }
        }

        private async Task Run(IProgress<ApplicationStatus> progress, CancellationToken token, CancellationToken yieldToken) {
            CameraInfo cam = cameraMediator.GetInfo();
            if (!cam.Connected) {
                TSLogger.Warning("TS Darks: camera not connected, skipping");
                return;
            }

            TSLogger.Info($"TS Darks: light-safe sun limit {DarkPlanner.SunLimitDegrees:0.0}°: {DarkPlanner.SunLimitBasis}");
            DarkWindow first = Window();
            string stop = DarkPlanner.StopReason(first);
            if (stop != null) {
                LogStop(first, $"TS Darks: not taking darks or bias frames: {stop}");
                if (coverStop != null) {
                    DisplayText = $"Not taken: {coverStop}";
                }
                return;
            }

            List<BiasNeed> biasQueue = new List<BiasNeed>();
            List<BiasTaken> biasTaken = new List<BiasTaken>();
            try {
                IList<BiasNeed> biasNeeds = DarkRuntime.BiasStore.LoadNeeds();
                biasTaken = DarkRuntime.BiasStore.LoadTaken(DateTime.UtcNow.AddDays(-7)).ToList();
                biasQueue = BiasPlanner.Queue(biasNeeds, biasTaken, DateTime.UtcNow);
                TSLogger.Info($"TS Bias: {biasNeeds.Count} combos in the stacker's bias backlog, {biasQueue.Count} still need bias frames");
            } catch (Exception ex) {
                TSLogger.Error($"TS Bias: could not read the bias backlog: {ex.Message}");
                biasQueue = new List<BiasNeed>();
            }

            List<DarkNeed> queue = new List<DarkNeed>();
            List<DarkTaken> taken = new List<DarkTaken>();
            if (!cam.CanSetTemperature) {
                TSLogger.Warning("TS Darks: the camera cannot set its temperature, so no darks are taken");
            } else {
                try {
                    IList<DarkNeed> needs = DarkRuntime.Store.LoadNeeds();
                    taken = DarkRuntime.Store.LoadTaken(DateTime.UtcNow.AddDays(-7)).ToList();
                    queue = DarkPlanner.Queue(needs, taken, DateTime.UtcNow);
                    if (Placement == DarkPlacement.WhileUnsafe) {
                        queue = DarkPlanner.WarmFirst(queue);
                    }
                    TSLogger.Info($"TS Darks: {needs.Count} combos in the stacker's backlog, {queue.Count} still need darks");
                } catch (Exception ex) {
                    TSLogger.Error($"TS Darks: could not read the dark backlog: {ex.Message}");
                    queue = new List<DarkNeed>();
                }
            }

            if (queue.Count == 0 && biasQueue.Count == 0) {
                return;
            }

            double imagingSetpoint = cam.TemperatureSetPoint;
            double? imagingPower = DarkPlanner.AtTemperature(cam.Temperature, imagingSetpoint) ? cam.CoolerPower : (double?)null;
            short normalReadout = cam.ReadoutModeForNormalImages;
            double currentSetpoint = imagingSetpoint;
            int done = 0;
            biasDone = 0;
            TSLogger.Info($"TS Darks: placement {Placement}, imaging setpoint {imagingSetpoint} °C, sensor {cam.Temperature} °C, cooler {cam.CoolerPower}%");
            if (queue.Count > 0) {
                RecordImagingHold(cam, imagingPower);
                queue = Order(queue, imagingSetpoint);
            }

            using var watch = new TemperatureWatch(cameraMediator);
            try {
                if (biasQueue.Count > 0 && await TakeBiasQueue(biasQueue, biasTaken, progress, token)) {
                    return;
                }

                if (queue.Count == 0) {
                    return;
                }

                for (int ci = 0; ci < queue.Count; ci++) {
                    DarkNeed need = queue[ci];
                    while (true) {
                        token.ThrowIfCancellationRequested();
                        Remote.Plugin.RemoteHost.Instance?.AtPlanBoundary();

                        DarkWindow w = Window();
                        stop = DarkPlanner.StopReason(w);
                        if (stop != null) {
                            LogStop(w, $"TS Darks: stopping after {done} darks: {stop}");
                            return;
                        }

                        int remaining = DarkPlanner.Remaining(need, taken, DateTime.UtcNow);
                        if (remaining == 0) {
                            break;
                        }

                        double? ambient = Ambient();
                        IList<CoolerLimit> limits = DarkRuntime.State.Limits(ambient.HasValue);
                        string skip = DarkPlanner.AboveAmbientReason(need.SetTemp, ambient)
                            ?? DarkPlanner.CapacityReason(DarkPlanner.Capacity(DarkRuntime.State.Holds(), limits), need.SetTemp, ambient)
                            ?? DarkPlanner.LimitReason(limits, need.SetTemp, ambient, imagingSetpoint, imagingPower);
                        CameraInfo info = cameraMediator.GetInfo();
                        TimeSpan overhead = TimeSpan.FromSeconds(Math.Max(0, info.LastDownloadTime));
                        skip ??= DarkPlanner.FitReason(w, need, currentSetpoint, imagingSetpoint, DarkRuntime.State.Rates(), overhead);
                        if (skip != null) {
                            TSLogger.Info($"TS Darks: skipping {need}: {skip}");
                            break;
                        }

                        DisplayText = $"{need} ({ci + 1}/{queue.Count})";
                        ProgressText = $"{need.FramesNeeded - remaining}/{need.FramesNeeded}";

                        DateTime? deadline = Deadline(w, need, imagingSetpoint, overhead);
                        string reach = await Reach(need.SetTemp, watch, deadline, progress, token, yieldToken);
                        CameraInfo reached = cameraMediator.GetInfo();
                        currentSetpoint = reached.TemperatureSetPoint;
                        if (reach != null) {
                            if (reach != DarkPlanner.CameraDisconnected && !token.IsCancellationRequested && !yieldToken.IsCancellationRequested && (deadline == null || DateTime.UtcNow < deadline.Value)) {
                                DarkRuntime.State.AddLimit(Limit(need.SetTemp, imagingSetpoint, imagingPower, reach, reached.Connected && !double.IsNaN(reached.Temperature) ? reached.Temperature : (double?)null));
                            }
                            TSLogger.Info($"TS Darks: skipping {need}: {reach}");
                            break;
                        }

                        DarkWindow beforeDark = Window();
                        stop = DarkPlanner.StopReason(beforeDark);
                        if (stop != null) {
                            LogStop(beforeDark, $"TS Darks: stopping after {done} darks: {stop}");
                            return;
                        }

                        string sunSkip = DarkPlanner.SunFitReason(Window(), TimeSpan.Zero, need.Exposure, overhead);
                        if (sunSkip != null) {
                            TSLogger.Info($"TS Darks: skipping {need}: {sunSkip}");
                            break;
                        }

                        (double deviation, double? peakPower, DateTime takenAt, string path) = await TakeDark(need, watch, info.ReadoutModes?.ToList(), progress, token);
                        if (deviation <= DarkPlanner.AtTargetToleranceC) {
                            RecordHold(need, peakPower);
                        }
                        if (deviation > DarkPlanner.AtTargetToleranceC) {
                            string why = $"the sensor drifted {deviation:0.0} °C from {need.SetTemp} °C during the dark";
                            DarkRuntime.State.AddLimit(Limit(need.SetTemp, imagingSetpoint, imagingPower, why));
                            TSLogger.Warning($"TS Darks: {why}; the stacker will reject it and this set temperature is skipped for now");
                        }

                        DarkRuntime.Store.RecordTaken(need, takenAt, cameraMediator.GetInfo().Temperature, deviation, path);
                        taken.Add(new DarkTaken { ComboKey = need.ComboKey, TakenAt = takenAt });
                        done++;
                        if (deviation > DarkPlanner.AtTargetToleranceC) {
                            break;
                        }
                    }
                }

                TSLogger.Info($"TS Darks: done, {done} darks taken");
            } catch (Exception ex) when (Utils.IsCancelException(ex)) {
                TSLogger.Warning($"TS Darks: canceled after {biasDone} bias frames and {done} darks");
                throw;
            } finally {
                DisplayText = queue.Count > 0 ? "Restoring the imaging setpoint" : "Restoring the readout mode";
                try {
                    if (cameraMediator.GetInfo().ReadoutModeForNormalImages != normalReadout) {
                        cameraMediator.SetReadoutModeForNormalImages(normalReadout);
                    }
                    if (queue.Count > 0) {
                        await Restore(imagingSetpoint, watch, progress, token);
                    }
                } catch (Exception ex) {
                    TSLogger.Error($"TS Darks: restoring the imaging setpoint {imagingSetpoint} °C failed: {ex.Message}");
                }
                DisplayText = coverStop != null ? $"Stopped: {coverStop}" : "";
                ProgressText = "...";
            }
        }

        private CoolerLimit Limit(double setTemp, double imagingSetpoint, double? imagingPower, string reason, double? reached = null) {
            var l = new CoolerLimit { At = DateTime.UtcNow, SetTemp = setTemp, Ambient = Ambient(), ImagingPower = imagingPower, ImagingSetpoint = imagingSetpoint, Reached = reached, Reason = reason };
            if (l.Ambient.HasValue) {
                UseLearning(l.Ambient.Value);
            }
            TSLogger.Info(l.Ambient.HasValue
                ? $"TS Darks: recorded a cooler failure at {setTemp} °C and {l.Ambient.Value:0.#} °C ambient{(reached.HasValue ? $", sensor at {reached.Value:0.0} °C" : "")}: {reason}"
                : $"TS Darks: cooler failure at {setTemp} °C kept for this run only, the ambient temperature is not known: {reason}");
            return l;
        }

        private static void UseLearning(double ambient) {
            DateTime now = DateTime.UtcNow;
            string night = DarkReadiness.NightOf(now.ToLocalTime());
            if (DarkRuntime.State.LearningUsed(night) == null) {
                DarkRuntime.State.RecordLearning(night, now, ambient);
            }
        }

        private List<DarkNeed> Order(List<DarkNeed> queue, double imagingSetpoint) {
            if (Placement == DarkPlacement.WhileUnsafe) {
                return queue;
            }
            List<DarkNeed> ordered = DarkPlanner.TonightFirst(queue, imagingSetpoint);
            int tonight = ordered.Count(n => DarkPlanner.Tonight(n, imagingSetpoint));
            TSLogger.Info($"TS Darks: {tonight} combos at or above {imagingSetpoint - DarkPlanner.AtTargetToleranceC} °C ({DarkPlanner.AtTargetToleranceC} °C below the imaging setpoint, which the sequence sets from the ambient temperature) come first, then {ordered.Count - tonight} colder ones, each group in the stacker's priority order");
            return ordered;
        }

        private void RecordImagingHold(CameraInfo cam, double? imagingPower) {
            double? ambient = Ambient();
            if (!imagingPower.HasValue || double.IsNaN(imagingPower.Value) || !ambient.HasValue) {
                return;
            }
            DarkRuntime.State.RecordHold(new CoolerHold { At = DateTime.UtcNow, Ambient = ambient.Value, SetTemp = cam.TemperatureSetPoint, PeakPower = imagingPower });
            TSLogger.Info($"TS Darks: recorded the imaging setpoint {cam.TemperatureSetPoint} °C as a hold at {ambient.Value:0.#} °C ambient, cooler {imagingPower.Value:0}%");
        }

        private void RecordHold(DarkNeed need, double? peakPower) {
            double? ambient = Ambient();
            if (!ambient.HasValue) {
                TSLogger.Info($"TS Darks: the cooler held {need.SetTemp} °C, not recorded because the ambient temperature is not known");
                return;
            }
            DarkRuntime.State.RecordHold(new CoolerHold { At = DateTime.UtcNow, Ambient = ambient.Value, SetTemp = need.SetTemp, PeakPower = peakPower });
            UseLearning(ambient.Value);
            TSLogger.Info($"TS Darks: recorded a hold at {need.SetTemp} °C and {ambient.Value:0.#} °C ambient, peak cooler power {(peakPower.HasValue ? $"{peakPower.Value:0}%" : "not reported")}");
        }

        private DateTime? Deadline(DarkWindow w, DarkNeed need, double imagingSetpoint, TimeSpan overhead) {
            DateTime? deadline = null;
            if (w.Placement == DarkPlacement.BeforeWait && w.WaitUntil.HasValue) {
                TimeSpan back = DarkPlanner.MoveTime(need.SetTemp, imagingSetpoint, DarkRuntime.State.Rates()) ?? TimeSpan.Zero;
                deadline = w.WaitUntil.Value - (w.AfterWait ?? TimeSpan.Zero) - back - TimeSpan.FromSeconds(need.Exposure);
            }
            DateTime? sun = DarkPlanner.LatestStart(w, need.Exposure, overhead, TimeSpan.FromHours(18));
            if (sun.HasValue && (!deadline.HasValue || sun.Value < deadline.Value)) {
                deadline = sun;
            }
            return deadline;
        }

        private async Task<string> Reach(double target, TemperatureWatch watch, DateTime? deadline, IProgress<ApplicationStatus> progress, CancellationToken token, CancellationToken yieldToken) {
            CameraInfo info = cameraMediator.GetInfo();
            if (DarkPlanner.SameSetpoint(info.TemperatureSetPoint, target) && DarkPlanner.AtTemperature(info.Temperature, target)) {
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, yieldToken);
            if (deadline.HasValue) {
                TimeSpan left = deadline.Value - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) {
                    return $"no time left before {deadline.Value.ToLocalTime():HH:mm:ss}, the latest start for the dark";
                }
                cts.CancelAfter(left);
            }

            double from = info.Temperature;
            DateTime start = DateTime.UtcNow;
            DisplayText = $"Setting the camera to {target} °C";
            try {
                bool ok = await RampTo(target, progress, cts.Token);
                if (!ok) {
                    return $"NINA could not reach {target} °C: the cooler stopped making progress at full power";
                }
                string held = await WaitAtTemperature(target, watch, cts.Token);
                if (held != null) {
                    return held;
                }
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                return yieldToken.IsCancellationRequested
                    ? $"imaging can resume, so the move to {target} °C was stopped"
                    : $"{target} °C was not reached by {deadline?.ToLocalTime():HH:mm:ss}, the latest start for the dark";
            }

            DarkRuntime.State.RecordMove(from, target, DateTime.UtcNow - start, DateTime.UtcNow);
            return null;
        }

        private async Task<bool> RampTo(double target, IProgress<ApplicationStatus> progress, CancellationToken token) {
            CameraInfo info = cameraMediator.GetInfo();
            double from = double.IsNaN(info.Temperature) ? info.TemperatureSetPoint : info.Temperature;
            TimeSpan ramp = DarkPlanner.Ramp(from, target);
            TSLogger.Info($"TS Darks: ramping the setpoint from {info.TemperatureSetPoint} °C (sensor {info.Temperature} °C) to {target} °C over {ramp.TotalMinutes:0.##} min");
            bool ok = await cameraMediator.CoolCamera(target, ramp, progress, token);
            if (ok && !DarkPlanner.SameSetpoint(cameraMediator.GetInfo().TemperatureSetPoint, target)) {
                ok = await cameraMediator.CoolCamera(target, TimeSpan.Zero, progress, token);
            }
            return ok;
        }

        private async Task<string> WaitAtTemperature(double target, TemperatureWatch watch, CancellationToken token) {
            CameraInfo info = cameraMediator.GetInfo();
            double best = Math.Abs(info.Temperature - target);
            DateTime lastProgress = DateTime.UtcNow;
            while (!DarkPlanner.AtTemperature(info.Temperature, target)) {
                info = await watch.Next(token);
                if (info == null || !info.Connected) {
                    return DarkPlanner.CameraDisconnected;
                }
                double gap = Math.Abs(info.Temperature - target);
                if (gap <= best - 0.5) {
                    best = gap;
                    lastProgress = DateTime.UtcNow;
                } else if (DateTime.UtcNow - lastProgress > TimeSpan.FromMinutes(2)) {
                    return $"the sensor stayed {gap:0.0} °C from {target} °C for 2 minutes at {info.CoolerPower:0}% cooler power";
                }
            }
            return null;
        }

        private async Task Restore(double imagingSetpoint, TemperatureWatch watch, IProgress<ApplicationStatus> progress, CancellationToken token) {
            CameraInfo info = cameraMediator.GetInfo();
            if (!info.Connected) {
                return;
            }
            if (DarkPlanner.SameSetpoint(info.TemperatureSetPoint, imagingSetpoint) && DarkPlanner.AtTemperature(info.Temperature, imagingSetpoint)) {
                return;
            }

            TSLogger.Info($"TS Darks: restoring the imaging setpoint {imagingSetpoint} °C from {info.TemperatureSetPoint} °C");
            if (token.IsCancellationRequested) {
                _ = RampTo(imagingSetpoint, progress, CancellationToken.None);
                return;
            }

            double from = info.Temperature;
            DateTime start = DateTime.UtcNow;
            bool ok = await RampTo(imagingSetpoint, progress, token);
            string held = ok ? await WaitAtTemperature(imagingSetpoint, watch, token) : "NINA could not reach it";
            if (held != null) {
                TSLogger.Warning($"TS Darks: the setpoint is back at {imagingSetpoint} °C but the sensor is not there yet: {held}");
                return;
            }
            DarkRuntime.State.RecordMove(from, imagingSetpoint, DateTime.UtcNow - start, DateTime.UtcNow);
        }

        private async Task<(double deviation, double? peakPower, DateTime takenAt, string path)> TakeDark(DarkNeed need, TemperatureWatch watch, IList<string> readoutModes, IProgress<ApplicationStatus> progress, CancellationToken token) {
            short? readout = DarkPlanner.ReadoutIndex(need.ReadoutMode, readoutModes);
            if (readout.HasValue && cameraMediator.GetInfo().ReadoutModeForNormalImages != readout.Value) {
                cameraMediator.SetReadoutModeForNormalImages(readout.Value);
            }

            var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnSaved(object sender, ImageSavedEventArgs args) {
                if (args?.MetaData?.Image?.ImageType == CaptureSequence.ImageTypes.DARK) {
                    saved.TrySetResult(args.PathToImage?.LocalPath);
                }
            }

            short bin = (short)Math.Max(1, Math.Round(need.Binning));
            var take = new TakeExposure(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
                ImageType = CaptureSequence.ImageTypes.DARK,
                ExposureTime = need.Exposure,
                Gain = (int)Math.Round(need.Gain),
                Offset = (int)Math.Round(need.Offset),
                Binning = new BinningMode(bin, bin)
            };

            CameraInfo before = cameraMediator.GetInfo();
            watch.BeginHold(need.SetTemp, before.Temperature, before.CoolerPower);
            DateTime takenAt = DateTime.UtcNow;
            DisplayText = $"Dark {need}";
            TSLogger.Info($"TS Darks: taking a dark: {need}, sensor {before.Temperature} °C, cooler {before.CoolerPower}%");
            imageSaveMediator.ImageSaved += OnSaved;
            string path = null;
            DateTime exposed;
            try {
                await take.Execute(progress, token);
                exposed = DateTime.UtcNow;
                using var saveWait = CancellationTokenSource.CreateLinkedTokenSource(token);
                saveWait.CancelAfter(TimeSpan.FromSeconds(Math.Max(need.Exposure, 60)));
                using (saveWait.Token.Register(() => saved.TrySetCanceled())) {
                    try {
                        path = await saved.Task;
                        DarkRuntime.State.RecordSave(DateTime.UtcNow - exposed, DateTime.UtcNow);
                    } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                        TSLogger.Warning("TS Darks: the dark was not reported saved; NINA's log has the save error if there was one");
                    }
                }
            } finally {
                imageSaveMediator.ImageSaved -= OnSaved;
            }

            CameraInfo after = cameraMediator.GetInfo();
            double deviation = watch.EndHold(after.Temperature, after.CoolerPower);
            double? peakPower = watch.PeakPower;
            TSLogger.Info($"TS Darks: dark saved to {path ?? "(not reported)"}, largest sensor deviation {deviation:0.0} °C");
            return (deviation, peakPower, takenAt, path);
        }

        private async Task<bool> TakeBiasQueue(List<BiasNeed> queue, List<BiasTaken> taken, IProgress<ApplicationStatus> progress, CancellationToken token) {
            for (int ci = 0; ci < queue.Count; ci++) {
                BiasNeed need = queue[ci];
                while (true) {
                    token.ThrowIfCancellationRequested();
                    Remote.Plugin.RemoteHost.Instance?.AtPlanBoundary();

                    DarkWindow w = Window();
                    string stop = DarkPlanner.StopReason(w);
                    if (stop != null) {
                        LogStop(w, $"TS Bias: stopping after {biasDone} bias frames: {stop}");
                        return true;
                    }

                    int remaining = BiasPlanner.Remaining(need, taken, DateTime.UtcNow);
                    if (remaining == 0) {
                        break;
                    }

                    CameraInfo info = cameraMediator.GetInfo();
                    double exposure = BiasPlanner.Exposure(info.ExposureMin);
                    TimeSpan overhead = TimeSpan.FromSeconds(Math.Max(0, info.LastDownloadTime));
                    string skip = BiasPlanner.FitReason(w, exposure, overhead);
                    if (skip != null) {
                        TSLogger.Info($"TS Bias: skipping {need}: {skip}");
                        break;
                    }

                    DisplayText = $"{need} ({ci + 1}/{queue.Count})";
                    ProgressText = $"{need.FramesNeeded - remaining}/{need.FramesNeeded}";

                    (DateTime takenAt, double? sensorTemp, string path) = await TakeBias(need, exposure, info.ReadoutModes?.ToList(), progress, token);
                    DarkRuntime.BiasStore.RecordTaken(need, takenAt, exposure, sensorTemp, path);
                    taken.Add(new BiasTaken { ComboKey = need.ComboKey, TakenAt = takenAt });
                    biasDone++;
                }
            }

            TSLogger.Info($"TS Bias: done, {biasDone} bias frames taken");
            return false;
        }

        private async Task<(DateTime takenAt, double? sensorTemp, string path)> TakeBias(BiasNeed need, double exposure, IList<string> readoutModes, IProgress<ApplicationStatus> progress, CancellationToken token) {
            short? readout = DarkPlanner.ReadoutIndex(need.ReadoutMode, readoutModes);
            if (readout.HasValue && cameraMediator.GetInfo().ReadoutModeForNormalImages != readout.Value) {
                cameraMediator.SetReadoutModeForNormalImages(readout.Value);
            }

            var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnSaved(object sender, ImageSavedEventArgs args) {
                if (args?.MetaData?.Image?.ImageType == CaptureSequence.ImageTypes.BIAS) {
                    saved.TrySetResult(args.PathToImage?.LocalPath);
                }
            }

            short bin = (short)Math.Max(1, Math.Round(need.Binning));
            var take = new TakeExposure(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
                ImageType = CaptureSequence.ImageTypes.BIAS,
                ExposureTime = exposure,
                Gain = (int)Math.Round(need.Gain),
                Offset = (int)Math.Round(need.Offset),
                Binning = new BinningMode(bin, bin)
            };

            CameraInfo before = cameraMediator.GetInfo();
            DateTime takenAt = DateTime.UtcNow;
            DisplayText = $"Bias {need}";
            TSLogger.Info($"TS Bias: taking a bias frame: {need}, {exposure} s, sensor {before.Temperature} °C");
            imageSaveMediator.ImageSaved += OnSaved;
            string path = null;
            DateTime exposed;
            try {
                await take.Execute(progress, token);
                exposed = DateTime.UtcNow;
                using var saveWait = CancellationTokenSource.CreateLinkedTokenSource(token);
                saveWait.CancelAfter(TimeSpan.FromSeconds(60));
                using (saveWait.Token.Register(() => saved.TrySetCanceled())) {
                    try {
                        path = await saved.Task;
                        DarkRuntime.State.RecordSave(DateTime.UtcNow - exposed, DateTime.UtcNow);
                    } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                        TSLogger.Warning("TS Bias: the bias frame was not reported saved; NINA's log has the save error if there was one");
                    }
                }
            } finally {
                imageSaveMediator.ImageSaved -= OnSaved;
            }

            double temp = cameraMediator.GetInfo().Temperature;
            TSLogger.Info($"TS Bias: bias frame saved to {path ?? "(not reported)"}");
            return (takenAt, double.IsNaN(temp) ? null : temp, path);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TargetSchedulerDarks)}, Placement: {Placement}";
        }
    }
}
