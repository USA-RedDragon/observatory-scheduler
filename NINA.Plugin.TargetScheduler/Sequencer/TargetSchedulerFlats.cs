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
    [ExportMetadata("Description", "Flats automation for Target Scheduler. Also takes the flats requested from the web for a target's lights of one night, whatever the project's flats handling and flat history.")]
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

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
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

                TotalFlatSets = neededFlats.Count;
                CompletedFlatSets = 0;

                LogTrainedFlatDetails();

                // Prep the flat device
                DisplayText = "Preparing flat device";
                await CloseCover(progress, token);
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
                    TSLogger.Warning("TS Flats: sequence was canceled/interrupted");
                    Status = SequenceEntityStatus.CREATED;
                    token.ThrowIfCancellationRequested();
                } else {
                    TSLogger.Error($"Exception taking flats: {ex.Message}:\n{ex.StackTrace}");
                }

                if (ex is SequenceEntityFailedException) {
                    throw;
                }

                throw new SequenceEntityFailedException($"exception taking flats: {ex.Message}", ex);
            } finally {
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