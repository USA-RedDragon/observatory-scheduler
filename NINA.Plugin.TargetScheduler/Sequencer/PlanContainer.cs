using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.Interfaces;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.PubSub;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.SyncService.Sync;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Camera;
using NINA.Sequencer.SequenceItem.FilterWheel;
using NINA.Sequencer.SequenceItem.Guider;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.SequenceItem.Telescope;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    public class PlanContainer : SequentialContainer, IDeepSkyObjectContainer {
        public static readonly string INSTRUCTION_CATEGORY = "Scheduler";

        private readonly TargetSchedulerContainer parentContainer;
        private readonly IProfileService profileService;
        private readonly IList<IDateTimeProvider> dateTimeProviders;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IWindowServiceFactory windowServiceFactory;

        private readonly ProfilePreference profilePreferences;
        private readonly ITarget previousPlanTarget;
        private readonly SchedulerPlan plan;
        private readonly IProfile activeProfile;
        private SchedulerProgressVM schedulerProgress;

        private IImageSaveWatcher imageSaveWatcher;
        private bool synchronizationEnabled;
        private int syncActionTimeout;
        private int syncSolveRotateTimeout;

        private readonly object flipLock = new object();
        private DateTime? flipStart;
        private TimeSpan flipWait;
        private CancellationToken sequenceToken;
        private NewTargetStartPublisher newTargetStartPublisher;
        private TargetStartPublisher targetStartPublisher;

        public PlanContainer(
                TargetSchedulerContainer parentContainer,
                IProfileService profileService,
                IList<IDateTimeProvider> dateTimeProviders,
                ITelescopeMediator telescopeMediator,
                IRotatorMediator rotatorMediator,
                IGuiderMediator guiderMediator,
                ICameraMediator cameraMediator,
                IImagingMediator imagingMediator,
                IImageSaveMediator imageSaveMediator,
                IImageHistoryVM imageHistoryVM,
                IFilterWheelMediator filterWheelMediator,
                IDomeMediator domeMediator,
                IDomeFollower domeFollower,
                IPlateSolverFactory plateSolverFactory,
                IWindowServiceFactory windowServiceFactory,
                IMessageBroker messageBroker,
                IImageSaveWatcher imageSaveWatcher,
                ProfilePreference profilePreferences,
                bool synchronizationEnabled,
                ITarget previousPlanTarget,
                SchedulerPlan plan,
                SchedulerProgressVM schedulerProgress) {
            Name = nameof(PlanContainer);
            Description = "";
            Category = INSTRUCTION_CATEGORY;

            this.parentContainer = parentContainer;
            this.profileService = profileService;
            this.dateTimeProviders = dateTimeProviders;
            this.telescopeMediator = telescopeMediator;
            this.rotatorMediator = rotatorMediator;
            this.guiderMediator = guiderMediator;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.imageHistoryVM = imageHistoryVM;
            this.filterWheelMediator = filterWheelMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            this.imageSaveWatcher = imageSaveWatcher;
            this.profilePreferences = profilePreferences;
            this.synchronizationEnabled = synchronizationEnabled;
            this.schedulerProgress = schedulerProgress;
            this.previousPlanTarget = previousPlanTarget;
            this.plan = plan;
            this.activeProfile = profileService.ActiveProfile;

            AttachNewParent(parentContainer);

            if (synchronizationEnabled) {
                SetSyncTimeouts();
            }

            newTargetStartPublisher = new NewTargetStartPublisher(messageBroker);
            targetStartPublisher = new TargetStartPublisher(messageBroker);

            // These have no impact on the container itself but are used to assign to each added instruction
            Attempts = 1;
            ErrorBehavior = InstructionErrorBehavior.ContinueOnError;
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            return Execute(progress, token, token);
        }

        /// <summary>
        /// Runs the plan under planToken. The after-exposure and after-target-complete event containers
        /// and the image processing of a finished exposure run under sequenceToken, so a cancel of the plan
        /// alone does not cancel them.
        /// </summary>
        public Task Execute(IProgress<ApplicationStatus> progress, CancellationToken planToken, CancellationToken sequenceToken) {
            TSLogger.Debug("executing target container");
            this.sequenceToken = sequenceToken;

            try {
                telescopeMediator.BeforeMeridianFlip += BeforeMeridianFlip;
                telescopeMediator.AfterMeridianFlip += AfterMeridianFlip;
                AddEndTimeTrigger(plan.PlanTarget);
                AddInstructions(plan);
                EnsureUnparked(progress, planToken);

                targetStartPublisher.Publish(plan);

                base.Execute(progress, planToken).Wait();
            } catch (Exception ex) {
                throw;
            } finally {
                telescopeMediator.BeforeMeridianFlip -= BeforeMeridianFlip;
                telescopeMediator.AfterMeridianFlip -= AfterMeridianFlip;
                imageSaveWatcher.WaitForAllImagesSaved();
                if (planToken.IsCancellationRequested && !sequenceToken.IsCancellationRequested && !GetItemsSnapshot().Any(i => i is PlanTakeExposure t && (t.FrameCompleted || t.Status == SequenceEntityStatus.FINISHED))) {
                    TSLogger.Info("the plan was cancelled before its exposure finished, so the after each exposure instructions are not run");
                } else {
                    parentContainer.ExecuteEventContainer(parentContainer.AfterEachExposureContainer, progress, sequenceToken).Wait();
                }

                ITarget target = ReloadTarget(plan.PlanTarget);
                if (target != null && !target.Project.ExposureCompletionHelper.IsIncomplete(target)) {
                    parentContainer.ExecuteEventContainer(parentContainer.AfterTargetCompleteContainer, progress, sequenceToken).Wait();
                    TargetScheduler.EventMediator.InvokeTargetComplete(target);
                    parentContainer.targetCompletePublisher.Publish(plan);
                }

                foreach (var item in Items) {
                    item.AttachNewParent(null);
                }

                foreach (var trigger in Triggers) {
                    trigger.AttachNewParent(null);
                }

                Items.Clear();
                Triggers.Clear();
                TSLogger.Debug("done executing target container");
            }

            return Task.CompletedTask;
        }

        public override Task Interrupt() {
            TSLogger.Warning("PlanContainer: interrupt");
            return base.Interrupt();
        }

        private void AddEndTimeTrigger(ITarget planTarget) {
            TSLogger.Debug($"adding target end time trigger, run until: {Utils.FormatDateTimeFull(planTarget.EndTime)}");
            Add(new SchedulerTargetEndTimeTrigger(planTarget.EndTime, planTarget.DatabaseId));
        }

        private void AddInstructions(SchedulerPlan plan) {
            foreach (IInstruction instruction in plan.PlanInstructions) {
                if (instruction is PlanMessage) {
                    TSLogger.Debug($"exp plan msg: {((PlanMessage)instruction).msg}");
                    continue;
                }

                Add(new PlanSchedulerProgress(schedulerProgress, instruction));

                if (instruction is PlanSlew) {
                    if (profilePreferences.EnableSlewCenter) {
                        AddSlew((PlanSlew)instruction, plan.PlanTarget, plan.PlanTime);
                    }
                    continue;
                }

                if (instruction is PlanSwitchFilter) {
                    AddSwitchFilter(instruction.exposure);
                    continue;
                }

                if (instruction is PlanSetReadoutMode) {
                    AddSetReadoutMode(instruction.exposure);
                    continue;
                }

                if (instruction is Planning.Entities.PlanTakeExposure) {
                    AddTakeExposure(plan.PlanTarget, instruction.exposure);
                    continue;
                }

                if (instruction is PlanDither) {
                    AddDither();
                    continue;
                }

                if (instruction is PlanPostExposure) {
                    // currently a no-op
                    continue;
                }

                if (instruction is PlanBeforeNewTargetContainer) {
                    newTargetStartPublisher.Publish(plan);
                    AddBeforeNewTargetInstructions();
                    continue;
                }

                TSLogger.Error($"unknown instruction type: {instruction.GetType().FullName}");
                throw new Exception($"unknown instruction type: {instruction.GetType().FullName}");
            }
        }

        private void AddSlew(PlanSlew instruction, ITarget target, DateTime planTime) {
            bool isPlateSolve = instruction.center;
            Coordinates coordinates = plan.PlanCoordinates ?? target.FramedCoordinatesAt(planTime);
            if (coordinates == null && target.IsBody()) {
                TSLogger.Warning($"no slew/center added: {target.PositionUnknownReason(planTime)}");
                return;
            }
            InputCoordinates slewCoordinates = new InputCoordinates(coordinates);
            SequenceItem slewCenter;

            string with = isPlateSolve ? "with" : "without";
            TSLogger.Debug($"slew ({with} center): {Utils.FormatCoordinates(coordinates)}");

            if (isPlateSolve) {
                if (rotatorMediator.GetInfo().Connected) {
                    if (synchronizationEnabled) {
                        slewCenter = new PlanCenterAndRotate(target, Target, syncActionTimeout, syncSolveRotateTimeout, profileService, telescopeMediator, imagingMediator, rotatorMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory);
                    } else {
                        slewCenter = new CenterAndRotate(profileService, telescopeMediator, imagingMediator, rotatorMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory);
                    }

                    slewCenter.Name = nameof(CenterAndRotate);
                    (slewCenter as Center).Coordinates = slewCoordinates;
                    (slewCenter as CenterAndRotate).PositionAngle = target.FramedRotation(planTime);
                } else {
                    slewCenter = new Center(profileService, telescopeMediator, imagingMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory);
                    slewCenter.Name = nameof(Center);
                    (slewCenter as Center).Coordinates = slewCoordinates;
                }
            } else {
                slewCenter = new SlewScopeToRaDec(telescopeMediator, guiderMediator);
                slewCenter.Name = nameof(SlewScopeToRaDec);
                (slewCenter as SlewScopeToRaDec).Coordinates = slewCoordinates;
            }

            SetItemDefaults(slewCenter, null);
            Add(slewCenter);
        }

        private void AddBeforeNewTargetInstructions() {
            int? numInstructions = parentContainer.BeforeTargetContainer.Items?.Count;
            TSLogger.Debug($"adding BeforeNewTarget container with {numInstructions} instruction(s)");
            parentContainer.BeforeTargetContainer.ResetAll();
            parentContainer.BeforeTargetContainer.ResetParent(parentContainer);
            Add(parentContainer.BeforeTargetContainer);
        }

        private void AddSwitchFilter(IExposure exposure) {
            TSLogger.Debug($"adding switch filter: {exposure.FilterName}");

            SwitchFilter switchFilter = new SwitchFilter(profileService, filterWheelMediator);
            SetItemDefaults(switchFilter, nameof(SwitchFilter));

            switchFilter.Filter = Utils.LookupFilter(profileService.ActiveProfile, exposure.FilterName);
            Add(switchFilter);
        }

        private void AddSetReadoutMode(IExposure exposure) {
            short readoutMode = GetReadoutMode(exposure.ReadoutMode);
            TSLogger.Debug($"adding set readout mode: {readoutMode}");
            SetReadoutMode setReadoutMode = new SetReadoutMode(cameraMediator) { Mode = readoutMode };
            SetItemDefaults(setReadoutMode, nameof(SetReadoutMode));

            Add(setReadoutMode);
        }

        private void AddTakeExposure(ITarget target, IExposure exposure) {
            TSLogger.Info($"adding take exposure: {exposure.FilterName} {SchedulerPlan.LengthText(exposure)}");

            PlanTakeExposure takeExposure = new PlanTakeExposure(
                        parentContainer,
                        synchronizationEnabled,
                        syncActionTimeout,
                        profileService,
                        cameraMediator,
                        imagingMediator,
                        imageSaveMediator,
                        imageHistoryVM,
                        imageSaveWatcher,
                        target,
                        exposure);
            SetItemDefaults(takeExposure, nameof(TakeExposure));
            takeExposure.Guard = () => BeforeExposure(exposure);
            takeExposure.SensorGate = WaitForSensor;
            takeExposure.BeforeCapture = CoolerStep;
            CameraConnections.Watch(cameraMediator);
            takeExposure.ProcessingToken = sequenceToken;

            takeExposure.ExposureCount = GetExposureCount();
            takeExposure.ExposureTime = exposure.ExposureLength;
            takeExposure.Gain = GetGain(exposure.Gain);
            takeExposure.Offset = GetOffset(exposure.Offset);
            takeExposure.Binning = exposure.BinningMode;
            takeExposure.ROI = target.ROI;

            Add(takeExposure);
        }

        private FlipSettings ProfileFlipSettings() {
            var m = activeProfile.MeridianFlipSettings;
            return m == null ? null : new FlipSettings { MinutesAfterMeridian = m.MinutesAfterMeridian, MaxMinutesAfterMeridian = m.MaxMinutesAfterMeridian, PauseTimeBeforeMeridian = m.PauseTimeBeforeMeridian, UseSideOfPier = m.UseSideOfPier };
        }

        private Task BeforeMeridianFlip(object sender, BeforeMeridianFlipEventArgs e) {
            try {
                TelescopeInfo scope = telescopeMediator.GetInfo();
                TimeSpan wait = ImagingLimit.NinaFlipWait(scope.TimeToMeridianFlip, ProfileFlipSettings());
                lock (flipLock) {
                    flipStart = DateTime.Now;
                    flipWait = wait;
                }
            } catch (Exception ex) {
                TSLogger.Warning($"meridian flip start not recorded: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private Task AfterMeridianFlip(object sender, AfterMeridianFlipEventArgs e) {
            DateTime? start;
            TimeSpan wait;
            lock (flipLock) {
                start = flipStart;
                wait = flipWait;
                flipStart = null;
            }
            if (!start.HasValue || !e.Success) {
                return Task.CompletedTask;
            }
            TimeSpan elapsed = DateTime.Now - start.Value;
            TimeSpan? overhead = ImagingLimit.MeasuredOverhead(elapsed, wait);
            if (overhead.HasValue) {
                SequenceLimits.Flips.Record(overhead.Value, DateTime.UtcNow);
                TSLogger.Info($"meridian flip measured from NINA's flip start and end events: {elapsed.TotalSeconds:0} s in total, {wait.TotalSeconds:0} s of it waiting to pass the meridian, {overhead.Value.TotalSeconds:0} s for the slew, recenter and settle");
            }
            return Task.CompletedTask;
        }

        private async Task<string> CoolerStep(IProgress<ApplicationStatus> progress, CancellationToken token) {
            string moved = await Darks.CoolerRuntime.BeforeExposure(progress, token);
            if (moved == null) {
                return null;
            }
            foreach (ISequenceItem item in GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.CREATED) {
                    item.Skip();
                }
            }
            return $"{moved}, so the rest of this plan is skipped and the scheduler plans again with the triggers run first";
        }

        private async Task<string> WaitForSensor(IProgress<ApplicationStatus> progress, CancellationToken token) {
            SensorGate gate = new SensorGate(ReadSensor, (d, t) => Task.Delay(d, t), () => DateTime.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            SensorGateOutcome outcome;
            bool reported = false;
            try {
                outcome = await gate.WaitAsync(msg => {
                    TSLogger.Info($"sensor gate: {msg}");
                    progress?.Report(new ApplicationStatus { Status = $"Target Scheduler: {msg}" });
                    reported = true;
                }, token);
            } finally {
                if (reported) {
                    progress?.Report(new ApplicationStatus { Status = "" });
                }
            }
            switch (outcome) {
                case SensorGateOutcome.Settled:
                    return "the sensor is back at the setpoint after a wait, so the scheduler plans again with the current time";
                case SensorGateOutcome.TimedOut:
                    TSLogger.Warning($"sensor gate: no light while the sensor is off the setpoint: {SensorGate.Describe(ReadSensor())}");
                    return "the sensor is off the setpoint";
                default:
                    return null;
            }
        }

        private SensorReading ReadSensor() {
            CameraInfo info = cameraMediator.GetInfo();
            return new SensorReading(info.Connected, info.CanSetTemperature, info.CoolerOn, info.Temperature, info.TemperatureSetPoint,
                TargetSchedulerDarks.PendingRestore(), CameraConnections.ConnectedAt);
        }

        private string BeforeExposure(IExposure exposure) {
            DateTime now = DateTime.Now;
            ImagingLimit limit = parentContainer.CurrentLimit;
            if (limit == null || now.AddSeconds(exposure.ExposureLength) <= limit.End) {
                return null;
            }

            foreach (ISequenceItem item in GetItemsSnapshot()) {
                if (item.Status == SequenceEntityStatus.CREATED) {
                    item.Skip();
                }
            }
            return $"a {exposure.ExposureLength} s exposure starting now would end at {now.AddSeconds(exposure.ExposureLength):HH:mm:ss}, after the sequence ends imaging at {limit.End:HH:mm:ss} ({limit.Basis}), so the rest of this plan is skipped";
        }

        private void AddDither() {
            TSLogger.Debug("adding dither");
            Dither dither = new Dither(guiderMediator, profileService);
            Add(dither);
        }

        private void AddWait(DateTime waitForTime, ITarget target) {
            Add(new PlanWaitInstruction(guiderMediator, telescopeMediator, waitForTime));
        }

        private void EnsureUnparked(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (telescopeMediator.GetInfo().AtPark) {
                TSLogger.Debug("telescope is parked before potential target slew: unparking");
                try {
                    telescopeMediator.UnparkTelescope(progress, token).Wait();
                } catch (Exception ex) {
                    TSLogger.Error($"failed to unpark telescope: {ex.Message}");
                    throw new SequenceEntityFailedException("Failed to unpark telescope");
                }
            }
        }

        private void SetSyncTimeouts() {
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                ProfilePreference profilePreference = context.GetProfilePreference(profileService.ActiveProfile.Id.ToString());
                syncActionTimeout = profilePreference != null ? profilePreference.SyncActionTimeout : SyncManager.DEFAULT_SYNC_ACTION_TIMEOUT;
                syncSolveRotateTimeout = profilePreference != null ? profilePreference.SyncSolveRotateTimeout : SyncManager.DEFAULT_SYNC_SOLVEROTATE_TIMEOUT;
            }
        }

        private ITarget ReloadTarget(ITarget reference) {
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                try {
                    Target t = context.GetTarget(reference.Project.DatabaseId, reference.DatabaseId);
                    ForkDatabase.RefreshGoalSource(context);
                    return new PlanningTarget(reference.Project, t);
                } catch (Exception ex) {
                    TSLogger.Error($"failed to reload target for completeness check: {ex.Message}\n{ex.StackTrace}");
                    return null;
                }
            }
        }

        private void SetItemDefaults(ISequenceItem item, string name) {
            if (name != null) {
                item.Name = name;
            }

            item.Category = INSTRUCTION_CATEGORY;
            item.Description = "";
            item.ErrorBehavior = this.ErrorBehavior;
            item.Attempts = this.Attempts;
        }

        private int GetExposureCount() {
            parentContainer.TotalExposureCount++;
            return parentContainer.TotalExposureCount;
        }

        private int GetGain(int? gain) {
            return gain.HasValue ? (int)gain : cameraMediator.GetInfo().DefaultGain;
        }

        private int GetOffset(int? offset) {
            return offset.HasValue ? (int)offset : cameraMediator.GetInfo().DefaultOffset;
        }

        private short GetReadoutMode(int? readoutMode) {
            return readoutMode.HasValue ? (short)readoutMode : cameraMediator.GetInfo().ReadoutMode;
        }

        public override object Clone() {
            throw new NotImplementedException();
        }

        // IDeepSkyObjectContainer behavior, defer to parent
        public InputTarget Target { get => parentContainer.Target; set { } }

        public NighttimeData NighttimeData => parentContainer.NighttimeData;
    }
}