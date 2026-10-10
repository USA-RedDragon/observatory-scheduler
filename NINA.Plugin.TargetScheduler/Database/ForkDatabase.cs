using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
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
                    ensured = true;
                    TSLogger.Info("fork goal schema ensured");
                    RefreshGoalSource(context);
                } catch (Exception ex) {
                    TSLogger.Error($"failed to ensure fork goal schema: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        public static IGoalProgressSource LoadGoalProgress(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                SqliteGoalProgressSource source = new SqliteGoalProgressSource(new SqlGoalRowProvider(context.Database.Connection));
                TSLogger.Debug($"loaded goal progress snapshot: {source.Count} goal-driven filters in {sw.ElapsedMilliseconds}ms");
                return source;
            } catch (Exception ex) {
                TSLogger.Warning($"failed to load goal progress, falling back to exposure counts: {ex.Message}");
                return SqliteGoalProgressSource.Empty;
            }
        }

        public static IGoalProgressSource RefreshGoalSource(SchedulerDatabaseContext context) {
            IGoalProgressSource source = LoadGoalProgress(context);
            MosaicCompletion.GoalSource = source;
            return source;
        }

        public static ITargetSeasonWriter TargetSeasonWriter() {
            return new SqlTargetSeasonWriter(() => new SchedulerDatabaseInteraction().GetContext().Database.Connection);
        }
    }
}
