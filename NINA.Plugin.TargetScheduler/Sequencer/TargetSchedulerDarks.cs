using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Equipment.MyCamera;
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
    [ExportMetadata("Description", "Takes the darks the stacker's dark backlog asks for, at each one's set temperature, while the cover is already closed and the sun is below the light-safe altitude. It uses only the camera.")]
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

        [ImportingConstructor]
        public TargetSchedulerDarks(IProfileService profileService,
                                    ICameraMediator cameraMediator,
                                    IImagingMediator imagingMediator,
                                    IImageSaveMediator imageSaveMediator,
                                    IImageHistoryVM imageHistoryVM,
                                    ISafetyMonitorMediator safetyMonitorMediator,
                                    IWeatherDataMediator weatherDataMediator) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.weatherDataMediator = weatherDataMediator;
        }

        public TargetSchedulerDarks(TargetSchedulerDarks cloneMe) : this(
            cloneMe.profileService,
            cloneMe.cameraMediator,
            cloneMe.imagingMediator,
            cloneMe.imageSaveMediator,
            cloneMe.imageHistoryVM,
            cloneMe.safetyMonitorMediator,
            cloneMe.weatherDataMediator) {
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
            var w = new DarkWindow { Placement = Placement, Now = DateTime.UtcNow, StopBy = stopBy, SunAltitudeAt = SunAltitude() };
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
            if (!cam.Connected || !cam.CanSetTemperature) {
                TSLogger.Warning("TS Darks: camera not connected or cannot set its temperature, skipping");
                return;
            }

            TSLogger.Info($"TS Darks: light-safe sun limit {DarkPlanner.SunLimitDegrees:0.0}°: {DarkPlanner.SunLimitBasis}");
            string stop = DarkPlanner.StopReason(Window());
            if (stop != null) {
                TSLogger.Info($"TS Darks: not taking darks: {stop}");
                return;
            }

            List<DarkNeed> queue;
            List<DarkTaken> taken;
            try {
                IList<DarkNeed> needs = DarkRuntime.Store.LoadNeeds();
                taken = DarkRuntime.Store.LoadTaken(DateTime.UtcNow.AddDays(-7)).ToList();
                queue = DarkPlanner.Queue(needs, taken, DateTime.UtcNow);
                TSLogger.Info($"TS Darks: {needs.Count} combos in the stacker's backlog, {queue.Count} still need darks");
            } catch (Exception ex) {
                TSLogger.Error($"TS Darks: could not read the dark backlog: {ex.Message}");
                return;
            }

            if (queue.Count == 0) {
                return;
            }

            double imagingSetpoint = cam.TemperatureSetPoint;
            double? imagingPower = DarkPlanner.AtTemperature(cam.Temperature, imagingSetpoint) ? cam.CoolerPower : (double?)null;
            short normalReadout = cam.ReadoutModeForNormalImages;
            double currentSetpoint = imagingSetpoint;
            int done = 0;
            TSLogger.Info($"TS Darks: placement {Placement}, imaging setpoint {imagingSetpoint} °C, sensor {cam.Temperature} °C, cooler {cam.CoolerPower}%");

            using var watch = new TemperatureWatch(cameraMediator);
            try {
                for (int ci = 0; ci < queue.Count; ci++) {
                    DarkNeed need = queue[ci];
                    while (true) {
                        token.ThrowIfCancellationRequested();
                        Remote.Plugin.RemoteHost.Instance?.AtPlanBoundary();

                        DarkWindow w = Window();
                        stop = DarkPlanner.StopReason(w);
                        if (stop != null) {
                            TSLogger.Info($"TS Darks: stopping after {done} darks: {stop}");
                            return;
                        }

                        int remaining = DarkPlanner.Remaining(need, taken, DateTime.UtcNow);
                        if (remaining == 0) {
                            break;
                        }

                        string skip = DarkPlanner.LimitReason(DarkRuntime.State.Limits(), need.SetTemp, Ambient(), imagingSetpoint, imagingPower);
                        CameraInfo info = cameraMediator.GetInfo();
                        TimeSpan overhead = TimeSpan.FromSeconds(Math.Max(0, info.LastDownloadTime));
                        skip ??= DarkPlanner.FitReason(w, need, currentSetpoint, imagingSetpoint, DarkRuntime.State.Rates(), overhead);
                        if (skip != null) {
                            TSLogger.Info($"TS Darks: skipping {need}: {skip}");
                            break;
                        }

                        DisplayText = $"{need} ({ci + 1}/{queue.Count})";
                        ProgressText = $"{need.FramesNeeded - remaining}/{need.FramesNeeded}";

                        DateTime? deadline = Deadline(w, need, imagingSetpoint);
                        string reach = await Reach(need.SetTemp, watch, deadline, progress, token, yieldToken);
                        currentSetpoint = cameraMediator.GetInfo().TemperatureSetPoint;
                        if (reach != null) {
                            if (!token.IsCancellationRequested && !yieldToken.IsCancellationRequested && (deadline == null || DateTime.UtcNow < deadline.Value)) {
                                DarkRuntime.State.AddLimit(Limit(need.SetTemp, imagingSetpoint, imagingPower, reach));
                            }
                            TSLogger.Info($"TS Darks: skipping {need}: {reach}");
                            break;
                        }

                        stop = DarkPlanner.StopReason(Window());
                        if (stop != null) {
                            TSLogger.Info($"TS Darks: stopping after {done} darks: {stop}");
                            return;
                        }

                        string sunSkip = DarkPlanner.SunFitReason(Window(), TimeSpan.Zero, need.Exposure, overhead);
                        if (sunSkip != null) {
                            TSLogger.Info($"TS Darks: skipping {need}: {sunSkip}");
                            break;
                        }

                        (double deviation, DateTime takenAt, string path) = await TakeDark(need, watch, info.ReadoutModes?.ToList(), progress, token);
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
                TSLogger.Warning($"TS Darks: canceled after {done} darks");
                throw;
            } finally {
                DisplayText = "Restoring the imaging setpoint";
                try {
                    if (cameraMediator.GetInfo().ReadoutModeForNormalImages != normalReadout) {
                        cameraMediator.SetReadoutModeForNormalImages(normalReadout);
                    }
                    await Restore(imagingSetpoint, watch, progress, token);
                } catch (Exception ex) {
                    TSLogger.Error($"TS Darks: restoring the imaging setpoint {imagingSetpoint} °C failed: {ex.Message}");
                }
                DisplayText = "";
                ProgressText = "...";
            }
        }

        private CoolerLimit Limit(double setTemp, double imagingSetpoint, double? imagingPower, string reason) {
            return new CoolerLimit { SetTemp = setTemp, Ambient = Ambient(), ImagingPower = imagingPower, ImagingSetpoint = imagingSetpoint, Reason = reason };
        }

        private DateTime? Deadline(DarkWindow w, DarkNeed need, double imagingSetpoint) {
            if (w.Placement != DarkPlacement.BeforeWait || !w.WaitUntil.HasValue) {
                return null;
            }
            TimeSpan back = DarkPlanner.MoveTime(need.SetTemp, imagingSetpoint, DarkRuntime.State.Rates()) ?? TimeSpan.Zero;
            return w.WaitUntil.Value - (w.AfterWait ?? TimeSpan.Zero) - back - TimeSpan.FromSeconds(need.Exposure);
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
                    return "no time left before the wait ends";
                }
                cts.CancelAfter(left);
            }

            double from = info.Temperature;
            DateTime start = DateTime.UtcNow;
            DisplayText = $"Setting the camera to {target} °C";
            try {
                bool ok = await cameraMediator.CoolCamera(target, TimeSpan.Zero, progress, cts.Token);
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
                    : $"{target} °C was not reached before the wait ends";
            }

            DarkRuntime.State.RecordMove(from, target, DateTime.UtcNow - start, DateTime.UtcNow);
            return null;
        }

        private async Task<string> WaitAtTemperature(double target, TemperatureWatch watch, CancellationToken token) {
            CameraInfo info = cameraMediator.GetInfo();
            double best = Math.Abs(info.Temperature - target);
            DateTime lastProgress = DateTime.UtcNow;
            while (!DarkPlanner.AtTemperature(info.Temperature, target)) {
                info = await watch.Next(token);
                if (info == null || !info.Connected) {
                    return "the camera disconnected";
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
                _ = cameraMediator.CoolCamera(imagingSetpoint, TimeSpan.Zero, progress, CancellationToken.None);
                return;
            }

            double from = info.Temperature;
            DateTime start = DateTime.UtcNow;
            bool ok = await cameraMediator.CoolCamera(imagingSetpoint, TimeSpan.Zero, progress, token);
            string held = ok ? await WaitAtTemperature(imagingSetpoint, watch, token) : "NINA could not reach it";
            if (held != null) {
                TSLogger.Warning($"TS Darks: the setpoint is back at {imagingSetpoint} °C but the sensor is not there yet: {held}");
                return;
            }
            DarkRuntime.State.RecordMove(from, imagingSetpoint, DateTime.UtcNow - start, DateTime.UtcNow);
        }

        private async Task<(double deviation, DateTime takenAt, string path)> TakeDark(DarkNeed need, TemperatureWatch watch, IList<string> readoutModes, IProgress<ApplicationStatus> progress, CancellationToken token) {
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
            watch.BeginHold(need.SetTemp, before.Temperature);
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

            double deviation = watch.EndHold(cameraMediator.GetInfo().Temperature);
            TSLogger.Info($"TS Darks: dark saved to {path ?? "(not reported)"}, largest sensor deviation {deviation:0.0} °C");
            return (deviation, takenAt, path);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TargetSchedulerDarks)}, Placement: {Placement}";
        }
    }
}
