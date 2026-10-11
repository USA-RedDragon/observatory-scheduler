using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    [ExportMetadata("Name", "Target Scheduler Darks Ready")]
    [ExportMetadata("Description", "Succeeds when darks can be taken now without the camera: the dark backlog is not empty, the sun is below the light-safe altitude and a needed dark fits before dawn, the weather device reports the ambient temperature, and the cooler is known (or tried once per night) to reach a needed set temperature at that ambient. Also succeeds when no darks can be taken but the bias backlog is not empty and the sun is below the light-safe altitude. Fails with the reason otherwise. Use it as the condition of an If Fails.")]
    [ExportMetadata("Icon", "Scheduler.SchedulerSVG")]
    [ExportMetadata("Category", "Target Scheduler")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TargetSchedulerDarksReady : SequenceItem {
        private readonly IProfileService profileService;
        private readonly IWeatherDataMediator weatherDataMediator;

        [ImportingConstructor]
        public TargetSchedulerDarksReady(IProfileService profileService, IWeatherDataMediator weatherDataMediator) {
            this.profileService = profileService;
            this.weatherDataMediator = weatherDataMediator;
        }

        public TargetSchedulerDarksReady(TargetSchedulerDarksReady cloneMe) : this(cloneMe.profileService, cloneMe.weatherDataMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new TargetSchedulerDarksReady(this);
        }

        private string displayText = "";

        public string DisplayText {
            get => displayText;
            set {
                displayText = value;
                RaisePropertyChanged(nameof(DisplayText));
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

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            DateTime now = DateTime.UtcNow;
            string night = DarkReadiness.NightOf(now.ToLocalTime());
            List<DarkNeed> queue;
            try {
                IList<DarkNeed> needs = DarkRuntime.Store.LoadNeeds();
                queue = DarkPlanner.Queue(needs, DarkRuntime.Store.LoadTaken(now.AddDays(-7)), now);
            } catch (Exception ex) {
                throw Fail($"could not read the dark backlog: {ex.Message}");
            }

            List<BiasNeed> biasQueue;
            try {
                biasQueue = BiasPlanner.Queue(DarkRuntime.BiasStore.LoadNeeds(), DarkRuntime.BiasStore.LoadTaken(now.AddDays(-7)), now);
            } catch (Exception ex) {
                TSLogger.Warning($"TS Darks Ready: could not read the bias backlog, so it counts as empty: {ex.Message}");
                biasQueue = new List<BiasNeed>();
            }

            var weather = weatherDataMediator.GetInfo();
            DarkStateData state = DarkRuntime.State.Snapshot();
            var input = new DarkReadyInput {
                Now = now,
                SunAltitudeAt = SunAltitude(),
                Queue = queue,
                BiasQueue = biasQueue,
                WeatherConnected = weather.Connected,
                Ambient = weather.Connected && !double.IsNaN(weather.Temperature) ? weather.Temperature : null,
                Holds = DarkRuntime.State.Holds(),
                Limits = DarkRuntime.State.Limits(),
                SaveTime = DarkRuntime.State.SaveTime(),
                CoolingSecondsPerC = state.CoolingSecondsPerC,
                LearningUsedAt = DarkRuntime.State.LearningUsed(night)
            };

            DarkReadyResult r = DarkReadiness.Check(input);
            if (!r.Ready) {
                throw Fail(r.Reason);
            }

            if (r.Need == null) {
                DisplayText = $"Ready: bias, {r.Bias.Count} combos";
                TSLogger.Info($"TS Darks Ready: ready for bias frames, {r.Bias.Count} combos need bias: {r.Basis}");
                return Task.CompletedTask;
            }

            string counted = input.CoolingSecondsPerC.HasValue ? "the measured cooling time, exposure and save time" : $"{DarkPlanner.UnmeasuredMoveHorizon.TotalMinutes:0} min for the unmeasured cooling, exposure and save time";
            DisplayText = $"Ready: {r.Need}";
            TSLogger.Info($"TS Darks Ready: ready, {queue.Count} combos need darks, ambient {input.Ambient.Value:0.#} °C; {r.Need}: {r.Basis}; the sun check counted {counted}");
            return Task.CompletedTask;
        }

        private SequenceEntityFailedException Fail(string reason) {
            DisplayText = $"Not ready: {reason}";
            TSLogger.Info($"TS Darks Ready: not ready: {reason}");
            return new SequenceEntityFailedException($"Darks not ready: {reason}");
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(TargetSchedulerDarksReady)}";
        }
    }
}
