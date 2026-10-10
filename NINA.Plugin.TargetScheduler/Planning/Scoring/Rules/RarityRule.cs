using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Planning.Scoring.Rules {

    public class RarityRule : ScoringRule, IOptInScoringRule {
        public const string RULE_NAME = "Rarity";
        public const double DEFAULT_WEIGHT = .2 * WEIGHT_SCALE;
        private const int MAX_CACHE_ENTRIES = 5000;

        private static readonly ConcurrentDictionary<string, SeasonResult> cache = new ConcurrentDictionary<string, SeasonResult>();
        private static ITargetSeasonWriter seasonWriter;

        public static ITargetSeasonWriter SeasonWriter {
            get { return seasonWriter ??= ForkDatabase.TargetSeasonWriter(); }
            set { seasonWriter = value; }
        }

        public static bool WriteInBackground { get; set; } = true;

        public override string Name { get { return RULE_NAME; } }
        public override double DefaultWeight { get { return DEFAULT_WEIGHT; } }

        public override double Score(IScoringEngine scoringEngine, ITarget potentialTarget) {
            try {
                return GoalScoring.RarityScore(GetSeason(scoringEngine, potentialTarget));
            } catch (Exception ex) {
                TSLogger.Warning($"rarity rule failed for {potentialTarget?.Name}, scoring 0: {ex.Message}");
                return 0;
            }
        }

        public static SeasonResult GetSeason(IScoringEngine scoringEngine, ITarget target) {
            ObserverInfo observerInfo = new ObserverInfo {
                Latitude = scoringEngine.ActiveProfile.AstrometrySettings.Latitude,
                Longitude = scoringEngine.ActiveProfile.AstrometrySettings.Longitude,
                Elevation = scoringEngine.ActiveProfile.AstrometrySettings.Elevation,
            };

            IProject project = target.Project;
            HorizonDefinition horizon = project.HorizonDefinition ?? new HorizonDefinition(project.MinimumAltitude);
            TwilightLevel level = TwilightLevelFor(target);
            DateTime nightDate = TwilightCircumstances.AdjustTwilightCircumstances(observerInfo, scoringEngine.AtTime).OnDate.Date;

            string key = string.Join("|",
                target.DatabaseId.ToString(CultureInfo.InvariantCulture),
                target.Coordinates.RA.ToString("0.00000", CultureInfo.InvariantCulture),
                target.Coordinates.Dec.ToString("0.00000", CultureInfo.InvariantCulture),
                horizon.GetCacheKey(),
                project.MinimumTime.ToString(CultureInfo.InvariantCulture),
                level.ToString(),
                nightDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                observerInfo.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                observerInfo.Longitude.ToString("0.0000", CultureInfo.InvariantCulture));

            if (cache.TryGetValue(key, out SeasonResult cached)) {
                return cached;
            }

            Stopwatch sw = Stopwatch.StartNew();
            SeasonVisibility visibility = new SeasonVisibility(observerInfo, target.Coordinates, horizon, level, nightDate);
            SeasonResult season = SeasonCounter.Count(visibility, project.MinimumTime);
            sw.Stop();

            if (cache.Count >= MAX_CACHE_ENTRIES) { cache.Clear(); }
            if (!cache.TryAdd(key, season)) {
                return cache.TryGetValue(key, out cached) ? cached : season;
            }

            TSLogger.Debug($"rarity season for {project.Name}/{target.Name} on {nightDate:yyyy-MM-dd}: nights left {season.NightsLeft}, out of season {season.OutOfSeason}, scanned {season.NightsScanned} nights, {visibility.Samples} samples in {sw.ElapsedMilliseconds}ms");

            if (!target.IsPreview && !string.IsNullOrWhiteSpace(target.DatabaseGuid)) {
                Record(TargetSeasonRow.From(target.DatabaseGuid, season, nightDate, DateTime.UtcNow));
            }

            return season;
        }

        public static TwilightLevel TwilightLevelFor(ITarget target) {
            List<IExposure> plans = target.ExposurePlans?.Count > 0 ? target.ExposurePlans : target.AllExposurePlans;
            if (plans == null || plans.Count == 0) { return TwilightLevel.Astronomical; }
            return plans.Max(p => p.TwilightLevel);
        }

        public static void ClearCache() {
            cache.Clear();
        }

        private static void Record(TargetSeasonRow row) {
            ITargetSeasonWriter writer;
            try {
                writer = SeasonWriter;
            } catch (Exception ex) {
                TSLogger.Warning($"no target season writer: {ex.Message}");
                return;
            }

            void write() {
                try {
                    writer.Upsert(row);
                } catch (Exception ex) {
                    TSLogger.Warning($"failed to record target season for {row.TargetGuid}: {ex.Message}");
                }
            }

            if (WriteInBackground) {
                Task.Run(write);
            } else {
                write();
            }
        }
    }
}
