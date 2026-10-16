using Newtonsoft.Json;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Flats;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    [ExportMetadata("Name", "Target Scheduler Flats")]
    [ExportMetadata("Description", "Flats automation for Target Scheduler. Also takes the flats requested from the web for a target's lights of one night, whatever the project's flats handling and flat history. In the Target Scheduler container's Before Wait instructions it takes flats only when they fit before the wait ends, AfterWait included, and turns the light on only under a cover the flat device confirms Closed.")]
    [ExportMetadata("Icon", "Scheduler.SchedulerSVG")]
    [ExportMetadata("Category", "Target Scheduler")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TargetSchedulerFlats : TargetSchedulerFlatsBase {

        [ImportingConstructor]
        public TargetSchedulerFlats(IProfileService profileService,
                                    ICameraMediator cameraMediator,
                                    IImagingMediator imagingMediator,
                                    IImageSaveMediator imageSaveMediator,
                                    IImageHistoryVM imageHistoryVM,
                                    IFilterWheelMediator filterWheelMediator,
                                    IRotatorMediator rotatorMediator,
                                    IFlatDeviceMediator flatDeviceMediator) :
            base(profileService,
                 cameraMediator,
                 imagingMediator,
                 imageSaveMediator,
                 imageHistoryVM,
                 filterWheelMediator,
                 rotatorMediator,
                 flatDeviceMediator) { }

        public TargetSchedulerFlats(TargetSchedulerFlats cloneMe) : this(
            cloneMe.profileService,
            cloneMe.cameraMediator,
            cloneMe.imagingMediator,
            cloneMe.imageSaveMediator,
            cloneMe.imageHistoryVM,
            cloneMe.filterWheelMediator,
            cloneMe.rotatorMediator,
            cloneMe.flatDeviceMediator) {
            CopyMetaData(cloneMe);
            AlwaysRepeatFlatSet = cloneMe.AlwaysRepeatFlatSet;
            FlatCount = cloneMe.FlatCount;
        }

        public override object Clone() {
            return new TargetSchedulerFlats(this);
        }

        private TargetSchedulerContainer FindContainer() {
            NINA.Sequencer.Container.ISequenceContainer p = Parent;
            while (p != null) {
                if (p is TargetSchedulerContainer tsc) { return tsc; }
                p = p.Parent;
            }
            return null;
        }

        private async Task LightOffAfterFailure() {
            try {
                var info = flatDeviceMediator.GetInfo();
                if (info.Connected && info.LightOn) {
                    TSLogger.Warning("TS Flats: the flat light is still on after the flats, turning it off");
                    await flatDeviceMediator.ToggleLight(false, new Progress<ApplicationStatus>(), CancellationToken.None);
                }
            } catch (Exception ex) {
                TSLogger.Error($"TS Flats: could not turn the flat light off: {ex.Message}");
            }
        }

        private string WaitStopReason(List<LightSession> neededFlats) {
            DateTime? until = FindContainer()?.CurrentWaitUntil;
            if (!until.HasValue) { return null; }
            List<double> exposures = new List<double>();
            List<FlatSpec> seen = new List<FlatSpec>();
            foreach (LightSession flat in neededFlats) {
                if (!AlwaysRepeatFlatSet && seen.Contains(flat.FlatSpec)) { continue; }
                seen.Add(flat.FlatSpec);
                exposures.Add(flatsExpert.GetTrainedFlatExposureSetting(profileService.ActiveProfile, flat.FlatSpec)?.Time ?? 0);
            }
            TimeSpan estimate = FlatWaitFit.Estimate(exposures, FlatCount);
            return FlatWaitFit.StopReason(DateTime.UtcNow, until.Value.ToUniversalTime(), DarkRuntime.State.AfterWait(), estimate);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            using IDisposable cooler = Darks.CoolerRuntime.Own("flats");
            try {
                InitSync();

                DisplayText = "Determining needed flats";
                List<LightSession> neededFlats = flatsExpert.GetNeededFlats(profileService.ActiveProfile, DateTime.Now);
                List<(FlatRequest request, List<LightSession> sessions)> requests = RequestedFlats();
                foreach ((FlatRequest _, List<LightSession> sessions) in requests) {
                    foreach (LightSession session in sessions) {
                        if (!neededFlats.Contains(session)) {
                            neededFlats.Add(session);
                        }
                    }
                }

                if (neededFlats.Count == 0) {
                    TSLogger.Info("TS Flats: no flats needed");
                    RecordRequests(requests, new List<LightSession>());
                    DisplayText = "";
                    return;
                }

                if (FindContainer()?.CurrentWaitUntil != null && Remote.Plugin.RemoteHost.Instance?.ReplanOrPauseRequested == true) {
                    TSLogger.Info("TS Flats: not taking flats in this wait: a re-plan or pause was requested");
                    return;
                }
                if (WaitStopReason(neededFlats) is string stop) {
                    TSLogger.Info($"TS Flats: not taking flats in this wait: {stop}");
                    DisplayText = $"Not taken: {stop}";
                    return;
                }

                TotalFlatSets = neededFlats.Count;
                CompletedFlatSets = 0;
                PublishFlats(neededFlats);

                LogTrainedFlatDetails();

                // Prep the flat device
                DisplayText = "Preparing flat device";
                await CloseCover(progress, token);
                if (FindContainer()?.CurrentWaitUntil != null) {
                    var info = flatDeviceMediator.GetInfo();
                    if (FlatWaitFit.CoverStopReason(info.Connected, info.SupportsOpenClose, info.CoverState.ToString()) is string coverStop) {
                        calibrationEndCode = "cover";
                        calibrationEndReason = coverStop;
                        TSLogger.Warning($"TS Flats: not taking flats in this wait: {coverStop}");
                        DisplayText = $"Not taken: {coverStop}";
                        return;
                    }
                }
                await ToggleLight(true, progress, token);

                // Get the unique set of target ids over all neededFlats
                List<int> targetIds = new List<int>();
                foreach (LightSession neededFlat in neededFlats) {
                    if (!targetIds.Contains(neededFlat.TargetId)) {
                        targetIds.Add(neededFlat.TargetId);
                    }
                }

                List<FlatSpec> allTakenFlats = new List<FlatSpec>();
                List<LightSession> completed = new List<LightSession>();

                foreach (int targetId in targetIds) {
                    List<LightSession> targetNeededFlats = neededFlats.Where(ls => ls.TargetId == targetId).ToList();
                    List<FlatSpec> targetTakenFlats = new List<FlatSpec>();

                    foreach (LightSession neededFlat in targetNeededFlats) {
                        token.ThrowIfCancellationRequested();
                        bool success = true;

                        if (flatsExpert.IsRequiredFlat(AlwaysRepeatFlatSet, neededFlat, targetTakenFlats, allTakenFlats)) {
                            success = await TakeFlatSet(neededFlat, true, progress, token);
                            if (success) {
                                targetTakenFlats.Add(neededFlat.FlatSpec);
                                allTakenFlats.Add(neededFlat.FlatSpec);
                            }
                        } else {
                            FlatSkipped(neededFlat, "already_taken", "the same flats were already taken in this run");
                            TSLogger.Info($"TS Flats: flat already taken, skipping: {neededFlat}");
                        }

                        if (success) {
                            CompletedFlatSets++;
                            SaveFlatHistory(neededFlat);
                            completed.Add(neededFlat);
                        }
                    }
                }

                RecordRequests(requests, completed);

                await ToggleLight(false, progress, token);
            } catch (Exception ex) {
                DisplayText = "";

                if (Utils.IsCancelException(ex)) {
                    calibrationEndCode = "canceled";
                    calibrationEndReason = "the sequence stopped the instruction";
                    TSLogger.Warning("TS Flats: sequence was canceled/interrupted");
                    Status = SequenceEntityStatus.CREATED;
                    token.ThrowIfCancellationRequested();
                } else {
                    calibrationEndCode = "error";
                    calibrationEndReason = ex.Message;
                    TSLogger.Error($"Exception taking flats: {ex.Message}:\n{ex.StackTrace}");
                }

                if (ex is SequenceEntityFailedException) {
                    throw;
                }

                throw new SequenceEntityFailedException($"exception taking flats: {ex.Message}", ex);
            } finally {
                FlatsEnded();
                await LightOffAfterFailure();
                DisplayText = "";
                TotalFlatSets = 0;
                CompletedFlatSets = 0;
                Iterations = 0;
                CompletedIterations = 0;
            }

            return;
        }

        private List<(FlatRequest request, List<LightSession> sessions)> RequestedFlats() {
            var due = new List<(FlatRequest, List<LightSession>)>();
            try {
                DateTime now = DateTime.UtcNow;
                IList<FlatRequest> requests = DarkRuntime.FlatRequestStore.LoadRequests();
                List<FlatRequest> pending = FlatRequestPlanner.Due(requests, DarkRuntime.FlatRequestStore.LoadTaken(now - FlatRequestPlanner.LedgerWindow), now);
                TSLogger.Info($"TS Flats: {requests.Count} flat requests from the web, {pending.Count} due");
                foreach (FlatRequest request in pending) {
                    List<LightSession> sessions = flatsExpert.GetRequestedLightSessions(profileService.ActiveProfile, request);
                    TSLogger.Info($"TS Flats: {request}: {sessions.Count} light sessions to flat");
                    due.Add((request, sessions));
                }
            } catch (Exception ex) {
                TSLogger.Error($"TS Flats: could not read the flat requests: {ex.Message}");
            }
            return due;
        }

        private void RecordRequests(List<(FlatRequest request, List<LightSession> sessions)> requests, List<LightSession> completed) {
            foreach ((FlatRequest request, List<LightSession> sessions) in requests) {
                int sets = sessions.Count(completed.Contains);
                string note = sessions.Count == 0 ? FlatRequestPlanner.NoLightsNote(request) : FlatRequestPlanner.FailedNote(sessions.Count - sets, sessions.Count);
                try {
                    DarkRuntime.FlatRequestStore.RecordTaken(request, DateTime.UtcNow, sets, note);
                    TSLogger.Info($"TS Flats: {request}: {sets} of {sessions.Count} sets taken{(note == null ? "" : $"; {note}")}");
                } catch (Exception ex) {
                    TSLogger.Error($"TS Flats: could not record {request}: {ex.Message}");
                }
            }
        }
    }
}