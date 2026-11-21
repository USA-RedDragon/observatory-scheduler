using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    public class SteeringExposureSelector : BaseExposureSelector, IExposureSelector {
        private readonly IExposureSelector inner;

        public IExposureSelector Inner => inner;

        /// <summary>
        /// Returns the selector under any number of Filter Steering wrappers.
        /// </summary>
        public static IExposureSelector Unwrap(IExposureSelector selector) {
            while (selector is SteeringExposureSelector steering) { selector = steering.Inner; }
            return selector;
        }

        public SteeringExposureSelector(IProject project, ITarget target, IExposureSelector inner) : base(target) {
            this.inner = inner;
            DitherManager = GetDitherManager(project, target);
        }

        public IExposure Select(DateTime atTime, IProject project, ITarget target) {
            IExposure fallback = inner.Select(atTime, project, target);
            try {
                IExposure steered = Steer(atTime, project, target, fallback, !target.IsPreview);
                if (steered == null || ReferenceEquals(steered, fallback)) { return fallback; }

                (inner as BasicExposureSelector)?.Follow(steered);
                steered.PreDither = DitherManager.DitherRequired(steered);
                TSLogger.Info($"filter steering for {project.Name}/{target.Name}: {steered.FilterName} instead of {fallback?.FilterName ?? "none"}");
                return steered;
            } catch (Exception ex) {
                TSLogger.Warning($"filter steering failed for {target?.Name}, using the default order: {ex.Message}");
                return fallback;
            }
        }

        /// <summary>
        /// The plan Filter Steering picks at <paramref name="atTime"/>, or null to keep <paramref name="current"/>. With
        /// <paramref name="commit"/> the poor-sky gate remembers its decisions; scoring and previews leave it false.
        /// </summary>
        public static IExposure Steer(DateTime atTime, IProject project, ITarget target, IExposure current, bool commit = false) {
            return Plan(atTime, project, target, current, commit).Chosen;
        }

        /// <summary>
        /// Filter Steering's pick, as <see cref="Steer"/>, and the plans the poor-sky gate holds out at <paramref name="atTime"/>.
        /// </summary>
        public static (IExposure Chosen, List<IExposure> SitOut) Plan(DateTime atTime, IProject project, ITarget target, IExposure current, bool commit = false) {
            (List<SteeringCandidate> candidates, int currentIndex, double? quality) = Candidates(atTime, project, target, current, commit);
            if (candidates == null) { return (null, new List<IExposure>()); }

            int chosen = FilterSteering.Choose(candidates, currentIndex, quality);
            List<SteeringCandidate> belowCut = candidates.Where(c => FilterSteering.SitsOut(c, quality)).ToList();
            if (belowCut.Count > 0) {
                TSLogger.Debug($"filter steering for {target.Name}: at night quality {(quality.HasValue ? quality.Value.ToString("0.00") : "unknown")} {string.Join(", ", belowCut.Select(c => $"{c.Filter} (expected {c.Goal?.ExpectedScore?.ToString("0.00") ?? "unknown"} against its cut {c.Goal?.ScoreCut?.ToString("0.00") ?? "unknown"})"))} sit out while another can pass");
            }
            List<IExposure> sitOut = FilterSteering.Applies(candidates) ? belowCut.Select(c => target.ExposurePlans[c.Index]).ToList() : new List<IExposure>();
            return (chosen < 0 ? null : target.ExposurePlans[chosen], sitOut);
        }

        /// <summary>
        /// True when Filter Steering is on for <paramref name="project"/> and the poor-sky gate holds out every open plan of
        /// <paramref name="target"/> at <paramref name="atTime"/> (<see cref="FilterSteering.HoldsAll"/>). The gate's memory is read, not moved; an error counts as not held out.
        /// </summary>
        public static bool HeldOut(DateTime atTime, IProject project, ITarget target) {
            try {
                if (target == null || !FilterSteeringRule.IsOn(project)) { return false; }
                (List<SteeringCandidate> candidates, _, double? quality) = Candidates(atTime, project, target, target.SelectedExposure, false);
                return candidates != null && FilterSteering.HoldsAll(candidates, quality);
            } catch (Exception ex) {
                TSLogger.Warning($"poor-sky gate check failed for {target?.Name}, not holding it out: {ex.Message}");
                return false;
            }
        }

        private static (List<SteeringCandidate> Candidates, int CurrentIndex, double? Quality) Candidates(DateTime atTime, IProject project, ITarget target, IExposure current, bool commit) {
            ExposureCompletionHelper helper = project.ExposureCompletionHelper;
            if (helper == null || target.ExposurePlans == null) { return (null, -1, null); }

            ObserverInfo observer = SkyConditions.CurrentObserver;
            Coordinates coordinates = target.CoordinatesAt(atTime);
            double glare = observer == null || (coordinates == null && target.IsBody()) ? 0 : SkyConditions.MoonGlare(observer, coordinates, atTime);
            MoonState? moon = SkyConditions.KeepState(observer, coordinates, atTime);
            MoonKeepTable keep = MoonKeepTable.Current;
            double? broadbandWorth = moon.HasValue ? MoonKeep.Worth(keep.Broadband(moon.Value)) : null;
            double altitude = observer == null || coordinates == null ? double.NaN : AstrometryUtils.GetAltitude(observer, coordinates, atTime);

            List<SteeringCandidate> candidates = new List<SteeringCandidate>();
            int currentIndex = -1;
            for (int i = 0; i < target.ExposurePlans.Count; i++) {
                IExposure plan = target.ExposurePlans[i];
                if (plan == null || plan.Rejected) { continue; }
                if (ReferenceEquals(plan, current)) { currentIndex = i; }
                candidates.Add(new SteeringCandidate {
                    Index = i,
                    Filter = plan.FilterName,
                    Goal = helper.GoalFor(plan),
                    PercentComplete = helper.PercentComplete(plan),
                    Narrowband = FilterKind.IsNarrowband(plan.FilterName),
                    MoonGlare = glare,
                    Worth = moon.HasValue && SkyConditions.KeepApplies(target, plan) ? MoonKeep.Worth(keep.Lookup(plan.FilterName, moon.Value)) : null,
                    BroadbandWorth = SkyConditions.KeepApplies(target, plan) ? broadbandWorth : null,
                    AltWorth = SkyConditions.AltWorth(target, plan, altitude),
                    PanelMatch = PanelBalance.ConditionFactor(target, plan.FilterName, glare, observer),
                    Key = $"{target.DatabaseId}/{MoonKeep.FilterKey(plan.FilterName)}"
                });
            }

            double? quality = SkyConditions.NightQuality(atTime);
            DateTime atUtc = atTime.ToUniversalTime();
            foreach (SteeringCandidate c in candidates) {
                c.Held = ScoreGate.Current.Decide(c, quality, atUtc, commit);
            }
            TSLogger.Debug($"filter steering conditions for {target.Name}: glare {glare:0.00}, quality {(quality.HasValue ? quality.Value.ToString("0.00") : "unknown")}, favors {(ConditionMatch.NarrowbandFavored(glare, quality, broadbandWorth) ? "narrowband" : "broadband")}{(moon.HasValue ? $", kept-rate worth {string.Join(", ", candidates.Select(c => $"{c.Filter} {(c.Worth.HasValue ? c.Worth.Value.ToString("0.00") : "none")}"))}" : "")}, sibling panel match {string.Join(", ", candidates.Select(c => $"{c.Filter} {c.PanelMatch:0.00}"))}");
            return (candidates, currentIndex, quality);
        }

        public void ExposureTaken(IExposure exposure) {
            inner.ExposureTaken(exposure);
        }

        public override void ExposureAborted(IExposure exposure) {
            inner.ExposureAborted(exposure);
        }

        public void TargetReset() {
            inner.TargetReset();
        }
    }
}
