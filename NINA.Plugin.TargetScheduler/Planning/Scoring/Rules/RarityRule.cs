using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class RarityRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Rarity";
        public const double DEFAULT_WEIGHT = .2 * WEIGHT_SCALE;

        public static Func<DateTime> Today { get; set; } = () => DateTime.Now.Date;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            try {
                SeasonEntry entry = Lookup(scoringEngine, potentialTarget);
                return entry == null ? 0 : GoalScoring.RarityScore(entry.Season);
            } catch (Exception ex) {
                TSLogger.Warning($"rarity rule failed for {potentialTarget?.Name}, scoring 0: {ex.Message}");
                return 0;
            }
        }

        public static SeasonEntry Lookup(IScoringEngine scoringEngine, ITarget target) {
            if (string.IsNullOrWhiteSpace(target?.Guid)) { return null; }

            SeasonStore store = SeasonWorker.Store;
            if (!store.Seeded) {
                SeasonWorker.SeedStore();
            }

            SeasonEntry entry = store.Find(target.Guid, Key(scoringEngine, target), Today());
            if (entry == null) {
                TSLogger.Debug($"rarity: no season yet for {target.Name}, scoring 0");
                SeasonWorker.Instance?.RequestPass($"no season for {target.Name}");
            }

            return entry;
        }

        public static string Key(IScoringEngine scoringEngine, ITarget target) {
            ObserverInfo observerInfo = new ObserverInfo {
                Latitude = scoringEngine.ActiveProfile.AstrometrySettings.Latitude,
                Longitude = scoringEngine.ActiveProfile.AstrometrySettings.Longitude,
                Elevation = scoringEngine.ActiveProfile.AstrometrySettings.Elevation,
            };

            IProject project = target.Project;
            HorizonDefinition horizon = project.HorizonDefinition ?? new HorizonDefinition(project.MinimumAltitude);
            TwilightLevel level = SeasonCalculator.LevelFor((target.AllExposurePlans ?? target.ExposurePlans)?.Where(p => p.IsEnabled).Select(p => p.TwilightLevel));
            return SeasonCalculator.Key(observerInfo, target.Coordinates, horizon, project.MinimumTime, level);
        }
    }
}
