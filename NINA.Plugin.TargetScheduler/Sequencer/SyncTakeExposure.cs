using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.SyncService.Sync;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Utility;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    internal class SyncTakeExposure : SchedulerTakeExposure {
        private IImageSaveWatcher syncImageSaveWatcher;
        private SyncedExposure syncedExposure;
        private ITarget target;
        private IExposure exposure;
        private ExposureTemplate exposureTemplate;
        private Task imageProcessingTask;

        private static int exposureCount = 0;

        public SyncTakeExposure(
            ITarget target,
            IExposure exposure,
            ExposureTemplate exposureTemplate,
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            IImageSaveMediator imageSaveMediator,
            IImageHistoryVM imageHistoryVM,
            IImageSaveWatcher syncImageSaveWatcher,
            SyncedExposure syncedExposure,
            Action<String> UpdateDisplayTextAction) : base(profileService, cameraMediator, imagingMediator, imageSaveMediator, imageHistoryVM) {
            this.target = target;
            this.exposure = exposure;
            this.exposureTemplate = exposureTemplate;
            this.syncImageSaveWatcher = syncImageSaveWatcher;
            this.syncedExposure = syncedExposure;

            Category = PlanContainer.INSTRUCTION_CATEGORY;

            ExposureTime = GetExposureLength();
            Binning = exposureTemplate.BinningMode;
            Gain = GetGain();
            Offset = GetOffset();
            ImageType = CaptureSequence.ImageTypes.LIGHT;
            ROI = target.ROI;

            UpdateDisplayTextAction($"Exposing: {ExposureTime}s, Filter: {exposureTemplate.FilterName}, Gain: {Gain}, Offset: {Offset}, Bin: {Binning}");
            ExposureCount = exposureCount++;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
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

            Remote.Plugin.RemoteHost.Instance?.ShutterOpening(exposure, Remote.Commands.CaptureGate.SyncClientBlocked);
            IExposureData exposureData;
            try {
                exposureData = await imagingMediator.CaptureImage(capture, token, progress);
            } finally {
                Remote.Plugin.RemoteHost.Instance?.CaptureEnded();
            }

            if (IsLightSequence()) {
                imageHistoryVM.Add(exposureData.MetaData.Image.Id, ImageType);
            }

            imageProcessingTask = ProcessImageData(syncedExposure, exposureData, progress, token);
            await imageProcessingTask;

            ExposureCount++;
        }

        // Cobbled from NINA TakeExposure - private method
        private async Task ProcessImageData(SyncedExposure syncedExposure, IExposureData exposureData, IProgress<ApplicationStatus> progress, CancellationToken token) {
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

                imageData.MetaData.Target.Name = syncedExposure.TargetName;
                imageData.MetaData.Target.Coordinates = GetCoordinates(syncedExposure);
                imageData.MetaData.Target.PositionAngle = syncedExposure.TargetPositionAngle;

                var root = ItemUtility.GetRootContainer(this.Parent);
                imageData.MetaData.Sequence.Title = root != null ? root.SequenceTitle : "";

                syncImageSaveWatcher.WaitForExposure(imageData.MetaData.Image.Id, new ExposureWaitData(target, exposure, imageData.MetaData.Image.Id, syncedExposure.ExposureId, token));

                AddSchedulerKeywords(imageData, target, exposure);

                await imageSaveMediator.Enqueue(imageData, prepareTask, progress, token);
            } catch (Exception ex) {
                TSLogger.Error($"SYNC client: exception during exposure image processing: {ex.Message}\n{ex.StackTrace}");
                Logger.Error(ex);
            }
        }

        private Coordinates GetCoordinates(SyncedExposure syncedExposure) {
            return new Coordinates(AstroUtil.HMSToDegrees(syncedExposure.TargetRA), AstroUtil.DMSToDegrees(syncedExposure.TargetDec), Epoch.J2000, Coordinates.RAType.Degrees);
        }

        private double GetExposureLength() {
            return exposure.ExposureLength > 0 ? exposure.ExposureLength : exposureTemplate.defaultExposure;
        }

        private int GetGain() {
            return exposureTemplate.Gain < 0 ? cameraMediator.GetInfo().DefaultGain : exposureTemplate.Gain;
        }

        private int GetOffset() {
            return exposureTemplate.Offset < 0 ? cameraMediator.GetInfo().DefaultOffset : exposureTemplate.Offset;
        }
    }
}