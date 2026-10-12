using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
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
                    Flats.FlatRequestStore.Ensure(context.Database.Connection);
                    SubLengthStore.Ensure(context.Database.Connection);
                    BodySchema.TryEnsure(context.Database.Connection, m => TSLogger.Warning(m));
                    try {
                        TargetWindowStore.Ensure(context.Database.Connection);
                    } catch (Exception ex) {
                        TSLogger.Warning($"failed to create {TargetWindowStore.Table}, planning without event windows: {ex.Message}");
                    }
                    try {
                        BodyNightStore.Ensure(context.Database.Connection);
                    } catch (Exception ex) {
                        TSLogger.Warning($"failed to create {BodyNightStore.Table}, moving bodies get no season: {ex.Message}");
                    }
                    try {
                        MoonKeepStore.Ensure(context.Database.Connection);
                    } catch (Exception ex) {
                        TSLogger.Warning($"failed to create {MoonKeepStore.Table}, bright-Moon exposures keep the moon glare rule: {ex.Message}");
                    }
                    try {
                        AltKeepStore.Ensure(context.Database.Connection);
                    } catch (Exception ex) {
                        TSLogger.Warning($"failed to create {AltKeepStore.Table}, exposures are not weighed by the target's altitude: {ex.Message}");
                    }
                    try {
                        TemplateRoleStore.Ensure(context.Database.Connection);
                    } catch (Exception ex) {
                        TSLogger.Warning($"failed to create {TemplateRoleStore.RoleTable} and {TemplateRoleStore.MigrationTable}, no template is managed: {ex.Message}");
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
            return Load(context).Item1;
        }

        public static IGoalProgressSource RefreshGoalSource(SchedulerDatabaseContext context) {
            (SqliteGoalProgressSource source, PlannerSignals signals) = Load(context);
            PlannerSignals.Current = signals;
            MosaicCompletion.GoalSource = source.Count == 0 ? null : source;
            RefreshSubLengths(context);
            RefreshManagedTemplates(context);
            RefreshMoonKeep(context);
            RefreshAltKeep(context);
            RefreshBodies(context);
            RefreshPanelHistory(context);
            return MosaicCompletion.GoalSource;
        }

        public static void RefreshManagedTemplates(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                ManagedTemplateTable.Current = TemplateRoleStore.Load(context.Database.Connection);
                TSLogger.Debug($"loaded template roles: {ManagedTemplateTable.Current.ManagedCount} managed of {ManagedTemplateTable.Current.Count} in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                ManagedTemplateTable.Current = ManagedTemplateTable.Empty;
                TSLogger.Warning($"failed to load {TemplateRoleStore.RoleTable}, no template is managed: {ex.Message}");
            }
        }

        public static void RefreshMoonKeep(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                MoonKeepTable.Current = MoonKeepStore.Load(context.Database.Connection);
                TSLogger.Debug($"loaded moon kept rates: {MoonKeepTable.Current.Count} rows in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                MoonKeepTable.Current = MoonKeepTable.Empty;
                TSLogger.Warning($"failed to load {MoonKeepStore.Table}, bright-Moon exposures keep the moon glare rule: {ex.Message}");
            }
        }

        public static void RefreshAltKeep(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                AltKeepTable.Current = AltKeepStore.Load(context.Database.Connection);
                TSLogger.Debug($"loaded altitude kept rates: {AltKeepTable.Current.Count} rows in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                AltKeepTable.Current = AltKeepTable.Empty;
                TSLogger.Warning($"failed to load {AltKeepStore.Table}, exposures are not weighed by the target's altitude: {ex.Message}");
            }
        }

        public static ManagedTemplateTable TemplateRoles(SchedulerDatabaseContext context) {
            try {
                return TemplateRoleStore.Load(context.Database.Connection);
            } catch (Exception ex) {
                TSLogger.Warning($"failed to load {TemplateRoleStore.RoleTable}, showing every template: {ex.Message}");
                return ManagedTemplateTable.Empty;
            }
        }

        public static void RefreshSubLengths(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                SubLengthTable.Current = SubLengthStore.Load(context.Database.Connection);
                TSLogger.Debug($"loaded sub length recommendations: {SubLengthTable.Current.Count} rows in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                SubLengthTable.Current = SubLengthTable.Empty;
                TSLogger.Warning($"failed to load {SubLengthStore.Table}, using the plan or template exposure lengths: {ex.Message}");
            }
        }

        public static void RefreshBodies(SchedulerDatabaseContext context) {
            Stopwatch sw = Stopwatch.StartNew();
            BodySnapshot snapshot = BodySchema.Load(context.Database.Connection, BodySnapshot.Current, m => TSLogger.Warning(m));
            BodySnapshot.Current = snapshot;
            TSLogger.Debug($"loaded moving bodies: {snapshot.Targets.Count} targets, {snapshot.Positions.Count} positions in {sw.ElapsedMilliseconds}ms");
            try {
                SeasonWorker.Instance?.NoticeBodyNights(BodyNightStore.Version(context.Database.Connection));
            } catch (Exception ex) {
                TSLogger.Debug($"could not read the {BodyNightStore.Table} version: {ex.Message}");
            }
            RefreshTargetWindows(context);
        }

        public static void RefreshTargetWindows(SchedulerDatabaseContext context) {
            try {
                Stopwatch sw = Stopwatch.StartNew();
                TargetWindowTable.Current = TargetWindowStore.Load(context.Database.Connection);
                TSLogger.Debug($"loaded event windows: {TargetWindowTable.Current.Count} targets in {sw.ElapsedMilliseconds}ms");
            } catch (Exception ex) {
                TargetWindowTable.Current = TargetWindowTable.Empty;
                TSLogger.Warning($"failed to load {TargetWindowStore.Table}, planning without event windows: {ex.Message}");
            }
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

        public static string BodyNightsVersion() {
            using (SchedulerDatabaseContext context = new SchedulerDatabaseInteraction().GetContext()) {
                return BodyNightStore.Version(context.Database.Connection);
            }
        }

        public static BodyNightTable BodyNights() {
            using (SchedulerDatabaseContext context = new SchedulerDatabaseInteraction().GetContext()) {
                return BodyNightStore.Load(context.Database.Connection);
            }
        }

        public static ITargetSeasonWriter TargetSeasonWriter() {
            return new SqlTargetSeasonWriter(() => new SchedulerDatabaseInteraction().GetContext().Database.Connection);
        }
    }
}
