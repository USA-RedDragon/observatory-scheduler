using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.SyncService.Sync;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Extend TakeExposure so we can establish the association between an image id and the scheduler
    /// exposure plan that initiated the exposure.  We also have a reference to the InstructionWrapper
    /// that contains this so we can handle parent relationships.  Works in conjunction with the provided
    /// IImageSaveWatcher.
    ///
    /// We also handle ROI < 1 by using parts of TakeSubframeExposure here rather than a separate instruction.
    ///
    /// If synchronization is enabled, we handle getting exposures to registered clients and then waiting for
    /// them to complete.
    ///
    /// This is far from ideal.  If the core TakeExposure instruction changes, we'd be doing something different
    /// until this code was updated.  Ideally, NINA would provide a way to track some metadata or id all the way
    /// through the image pipeline to the save operation.
    /// </summary>
    public class PlanTakeExposure : SchedulerTakeExposure {
        private bool synchronizationEnabled;
        private int syncExposureTimeout;
        private IImageSaveWatcher imageSaveWatcher;
        private IDeepSkyObjectContainer dsoContainer;
        private ITarget target;
        private IExposure exposure;
        private Task imageProcessingTask;

        public PlanTakeExposure(
            IDeepSkyObjectContainer dsoContainer,
            bool synchronizationEnabled,
            int syncExposureTimeout,
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IImageSaveMediator imageSaveMediator,
            IImageHistoryVM imageHistoryVM,
            IImageSaveWatcher imageSaveWatcher,
            ITarget target,
            IExposure exposure) : base(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
            this.dsoContainer = dsoContainer;
            this.synchronizationEnabled = synchronizationEnabled;
            this.syncExposureTimeout = syncExposureTimeout;
            this.imageSaveWatcher = imageSaveWatcher;
            this.target = target;
            this.exposure = exposure;
        }

        public Func<string> Guard { get; set; }

        /// <summary>
        /// Waits, before a light, until the sensor is at the setpoint. It returns null to take the
        /// light, or the reason to skip it so the scheduler plans again.
        /// </summary>
        public Func<IProgress<ApplicationStatus>, CancellationToken, Task<string>> SensorGate { get; set; }

        /// <summary>Runs before a light once the guard passes; a non-null result skips the exposure.</summary>
        public Func<IProgress<ApplicationStatus>, CancellationToken, Task<string>> BeforeCapture { get; set; }

        public CancellationToken? ProcessingToken { get; set; }

        public bool FrameCompleted { get; private set; }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (SensorGate != null && IsLightSequence()) {
                string notReady = await SensorGate(progress, token);
                if (notReady != null) {
                    TSLogger.Info($"not starting the exposure: {notReady}");
                    return;
                }
            }

            string skip = Guard?.Invoke();
            if (skip != null) {
                TSLogger.Info($"not starting the exposure: {skip}");
                return;
            }

            if (BeforeCapture != null && IsLightSequence()) {
                skip = await BeforeCapture(progress, token);
                if (skip != null) {
                    TSLogger.Info($"not starting the exposure: {skip}");
                    return;
                }
            }

            var capture = new CaptureSequence() {
                ExposureTime = ExposureTime,
                Binning = Binning,
                Gain = Gain,
                Offset = Offset,
                ImageType = ImageType,
                ProgressExposureCount = ExposureCount,
                TotalExposureCount = ExposureCount + 1,
            };

            ObservableRectangle rect = GetObservableRectangle();
            if (rect != null) {
                capture.EnableSubSample = true;
                capture.SubSambleRectangle = rect;
            }

            var inputTarget = RetrieveTarget(dsoContainer);
            string exposureId = "";
            target.ExposureSelector.ExposureTaken(exposure);

            var host = Remote.Plugin.RemoteHost.Instance;
            CaptureKey opened = null;
            bool shutterOpened = false;
            IExposureData exposureData = null;
            bool aborted = false;
            bool cancelled = false;
            try {
                if (synchronizationEnabled) {
                    exposureId = Guid.NewGuid().ToString();
                    progress?.Report(new ApplicationStatus() { Status = "Target Scheduler: waiting for sync clients to accept exposure" });
                    await TrySendExposureToClients(exposureId, inputTarget, token);
                    progress?.Report(new ApplicationStatus() { Status = "" });
                }

                opened = host?.ShutterOpening(exposure, null, synchronizationEnabled);
                shutterOpened = true;
                if (host == null) {
                    exposureData = await imagingMediator.CaptureImage(capture, token, progress);
                } else {
                    var outcome = await host.Control.Capture.Run(t => imagingMediator.CaptureImage(capture, t, progress), opened, token);
                    aborted = outcome.Aborted;
                    exposureData = outcome.Value;
                    if (aborted) {
                        TSLogger.Info($"exposure aborted from the web ({outcome.Reason}), the partial frame is discarded");
                        host.Control.RequestReplan(outcome.Reason);
                    }
                }
            } catch (Exception ex) when (Utils.IsCancelException(ex)) {
                cancelled = true;
                TSLogger.Info("exposure cancelled before the frame was saved, the partial frame is not counted");
                throw;
            } finally {
                if (aborted || cancelled) {
                    RevertExposureTaken();
                }
                if (shutterOpened) {
                    host?.CaptureEnded(aborted || cancelled);
                }
            }

            if (aborted) {
                if (synchronizationEnabled) {
                    TSLogger.Info("abort: waiting for sync clients to finish their own exposures");
                    progress?.Report(new ApplicationStatus() { Status = "Target Scheduler: exposure aborted, waiting for sync clients to complete their exposures" });
                    await SyncServer.Instance.WaitForClientExposureCompletion(exposureId, token);
                    progress?.Report(new ApplicationStatus() { Status = "" });
                }
                return;
            }

            if (IsLightSequence()) {
                imageHistoryVM.Add(exposureData.MetaData.Image.Id, ImageType);
            }

            imageProcessingTask = ProcessImageData(inputTarget, exposureData, progress, ProcessingToken ?? token);
            await imageProcessingTask;
            FrameCompleted = true;

            // If any sync clients accepted this exposure, we have to wait for them to finish before continuing
            if (synchronizationEnabled) {
                progress?.Report(new ApplicationStatus() { Status = "Target Scheduler: waiting for sync clients to complete exposure" });
                await SyncServer.Instance.WaitForClientExposureCompletion(exposureId, token);
                progress?.Report(new ApplicationStatus() { Status = "" });
            }

            ExposureCount++;
        }

        private void RevertExposureTaken() {
            try {
                target.ExposureSelector.ExposureAborted(exposure);
            } catch (Exception ex) {
                TSLogger.Error($"could not undo the filter cadence and dither count for the aborted exposure: {ex.Message}");
            }
        }

        // Cobbled from NINA TakeExposure - private method
        private async Task ProcessImageData(InputTarget inputTarget, IExposureData exposureData, IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                var imageParams = new PrepareImageParameters(null, false);
                if (IsLightSequence()) {
                    imageParams = new PrepareImageParameters(true, true);
                }

                var imageData = await exposureData.ToImageData(progress, token);

                var prepareTask = imagingMediator.PrepareImage(imageData, imageParams, token);

                if (IsLightSequence()) {
                    imageHistoryVM.PopulateStatistics(imageData.MetaData.Image.Id, await imageData.Statistics);
                }

                if (inputTarget != null) {
                    imageData.MetaData.Target.Name = inputTarget.DeepSkyObject.NameAsAscii;
                    imageData.MetaData.Target.Coordinates = inputTarget.InputCoordinates.Coordinates;
                    imageData.MetaData.Target.PositionAngle = inputTarget.PositionAngle;
                }

                var root = ItemUtility.GetRootContainer(this.Parent);
                imageData.MetaData.Sequence.Title = root != null ? root.SequenceTitle : "";

                imageSaveWatcher.WaitForExposure(imageData.MetaData.Image.Id, new ExposureWaitData(target, exposure, imageData.MetaData.Image.Id, token));

                AddSchedulerKeywords(imageData, target, exposure);

                await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);
            } catch (Exception ex) {
                TSLogger.Error($"exception during exposure image processing: {ex.Message}\n{ex.StackTrace}");
                Logger.Error(ex);
            }
        }

        private async Task TrySendExposureToClients(string exposureId, InputTarget inputTarget, CancellationToken token) {
            await SyncServer.Instance.SyncExposure(exposureId, inputTarget, target.DatabaseId, exposure.DatabaseId, exposure.ExposureLength, syncExposureTimeout, token);
        }

        private InputTarget RetrieveTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as IDeepSkyObjectContainer;
                if (container != null) {
                    return container.Target;
                } else {
                    return RetrieveTarget(parent.Parent);
                }
            } else {
                return null;
            }
        }
    }
}