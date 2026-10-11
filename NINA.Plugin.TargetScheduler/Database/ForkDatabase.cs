using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NINA.Plugin.TargetScheduler.Database {

    public static class ForkDatabase {
        private static readonly object ensureLock = new object();
        private static bool ensured;

        public static void EnsureSchema(SchedulerDatabaseContext context) {
            lock (ensureLock) {
                if (ensured) { return; }
                try {
                    foreach (string sql in GoalSchema.All) {
                        context.Database.ExecuteSqlCommand(sql);
                    }
                    GoalSchema.EnsureColumns(context.Database.Connection);
                    Darks.DarkStore.Ensure(context.Database.Connection);
                    Darks.BiasStore.Ensure(context.Database.Connection);
                    ensured = true;
                    TSLogger.Info("fork goal schema ensured");
                    RefreshGoalSource(context);
                } catch (Exception ex) {
                    TSLogger.Error($"failed to ensure fork goal schema: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public static IGoalProgressSource LoadGoalProgress(SchedulerDatabaseContext context) {
            return Load(context).Item1;
        }

        public static IGoalProgressSource RefreshGoalSource(SchedulerDatabaseContext context) {
            (SqliteGoalProgressSource source, PlannerSignals signals) = Load(context);
            PlannerSignals.Current = signals;
            MosaicCompletion.GoalSource = source.Count == 0 ? null : source;
            RefreshPanelHistory(context);
            return MosaicCompletion.GoalSource;
        }

        public static void RefreshPanelHistory(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                PanelHistory.Current = new SqlPanelHistoryReader(context.Database.Connection).Load();
                TSLogger.Debug($"loaded mosaic panel history: {PanelHistory.Current.Count} subs in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                TSLogger.Warning($"failed to load mosaic panel history, panel matching and even shares see no history: {ex.Message}");
            }
        }

        private static (SqliteGoalProgressSource, PlannerSignals) Load(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                SqlGoalRowProvider provider = new SqlGoalRowProvider(context.Database.Connection);
                IList<GoalRow> goals = provider.LoadGoals();
                IList<GoalProgressRow> progress = provider.LoadProgress();
                SqliteGoalProgressSource source = new SqliteGoalProgressSource(goals, progress);
                PlannerSignals signals = new PlannerSignals(progress);
                TSLogger.Debug($"loaded goal progress snapshot: {source.Count} goal-driven filters, {signals.BoostCount} season boosts, night quality {(signals.Quality.HasValue ? signals.Quality.Value.ToString("0.00") + " at " + signals.QualityAt : "none")} in {sw.ElapsedMilliseconds}ms");
                return (source, signals);
            } catch (Exception ex) {
                TSLogger.Warning($"failed to load goal progress, falling back to exposure counts: {ex.Message}");
                return (SqliteGoalProgressSource.Empty, PlannerSignals.Empty);
            }
        }

        public static ITargetSeasonReader TargetSeasonReader() {
            return new SqlTargetSeasonReader(() => new SchedulerDatabaseInteraction().GetContext().Database.Connection);
        }

        public static ITargetSeasonWriter TargetSeasonWriter() {
            return new SqlTargetSeasonWriter(() => new SchedulerDatabaseInteraction().GetContext().Database.Connection);
        }
    }
}
