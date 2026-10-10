using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Data.Common;
using System.Diagnostics;

namespace NINA.Plugin.TargetScheduler.Database {

    public static class ForkDatabase {

        public static void EnsureSchema(SchedulerDatabaseContext context) {
            try {
                if (ForkSchema.EnsureOnce(sql => context.Database.ExecuteSqlCommand(sql))) {
                    TSLogger.Info("fork schema ensured: ts_goal, ts_goal_progress, ts_target_season");
                }
            } catch (Exception ex) {
                TSLogger.Error($"failed to ensure fork schema: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static IGoalProgressSource LoadGoalProgress(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                DbConnection connection = context.Database.Connection;
                SqliteGoalProgressSource source = SqliteGoalProgressSource.Load(connection);
                TSLogger.Debug($"loaded goal progress snapshot: {source.Count} goal-driven filters in {sw.ElapsedMilliseconds}ms");
                return source;
            } catch (Exception ex) {
                TSLogger.Warning($"failed to load goal progress, falling back to exposure counts: {ex.Message}");
                return SqliteGoalProgressSource.Empty;
            }
        }

        public static IGoalProgressSource LoadGoalProgress() {
            using (SchedulerDatabaseContext context = new SchedulerDatabaseInteraction().GetContext()) {
                return LoadGoalProgress(context);
            }
        }

        public static ITargetSeasonWriter TargetSeasonWriter() {
            return new DbTargetSeasonWriter(() => new SchedulerDatabaseInteraction().GetContext().Database.Connection);
        }
    }
}
