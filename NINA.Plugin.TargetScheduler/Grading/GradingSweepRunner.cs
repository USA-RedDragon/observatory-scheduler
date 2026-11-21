using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Grading {

    /// <summary>
    /// Grades the pending backlog of goal-driven exposure plans once, shortly after the plugin starts, so a plan
    /// with 20 or more subs does not wait for its filter to take another sub.
    /// </summary>
    public static class GradingSweepRunner {
        public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(45);

        private static readonly CancellationTokenSource cancel = new CancellationTokenSource();

        public static void Start(IProfileService profileService) {
            Task.Run(async () => {
                try {
                    await Task.Delay(StartDelay, cancel.Token);
                    await Sweep(profileService, cancel.Token);
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    TSLogger.Error($"grading sweep failed: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        public static void Stop() {
            try { cancel.Cancel(); } catch { }
        }

        private static async Task Sweep(IProfileService profileService, CancellationToken token) {
            IProfile profile = profileService.ActiveProfile;
            ProfilePreference profilePreference = new SchedulerPlanLoader(profile).GetProfilePreferences();
            ImageGraderPreferences graderPreferences = new ImageGraderPreferences(profile, profilePreference);
            if (!graderPreferences.IsDelayEnabled) {
                TSLogger.Info("grading sweep skipped: delayed grading is off");
                return;
            }

            int planCount = 0;
            List<SweepPlan> plans = new List<SweepPlan>();
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                IGoalProgressSource goals = ForkDatabase.LoadGoalProgress(context);
                foreach (Project project in context.GetAllProjects(profile.Id.ToString())) {
                    if (!project.EnableGrader) { continue; }
                    foreach (Target target in project.Targets ?? new List<Target>()) {
                        foreach (ExposurePlan exposurePlan in target.ExposurePlans ?? new List<ExposurePlan>()) {
                            token.ThrowIfCancellationRequested();
                            string filter = exposurePlan.ExposureTemplate?.FilterName;
                            if (string.IsNullOrWhiteSpace(target.Guid) || string.IsNullOrWhiteSpace(filter)) { continue; }
                            if (goals.Get(target.Guid, filter) == null) { continue; }

                            planCount++;
                            plans.Add(new SweepPlan {
                                TargetId = target.Id,
                                ExposurePlanId = exposurePlan.Id,
                                HasGoal = true,
                                GraderEnabled = true,
                                TargetRoi = target.ROI,
                                Images = context.GetAcquiredImagesForGrading(exposurePlan).Select(a => new SweepImage {
                                    Id = a.Id,
                                    ExposurePlanId = a.ExposureId,
                                    ProfileId = a.ProfileId,
                                    Exposure = a.Metadata.ExposureDuration,
                                    Gain = a.Metadata.Gain,
                                    Offset = a.Metadata.Offset,
                                    Binning = a.Metadata.Binning,
                                    Roi = a.Metadata.ROI,
                                    Pending = a.GradingStatus == GradingStatus.Pending
                                }).ToList()
                            });
                        }
                    }
                }
            }

            List<SweepPick> picks = GradingSweep.Select(plans, true);
            ImageGradingController controller = ImageGradingController.Instance;
            foreach (SweepPick pick in picks) {
                GradingWorkData workData = new GradingWorkData(pick.TargetId, pick.ExposurePlanId, pick.AcquiredImageId, -1, graderPreferences);
                await controller.Enqueue(workData, token);
            }

            TSLogger.Info($"grading sweep: goal plans checked={planCount}, groups enqueued={picks.Count}, pending images covered={picks.Sum(p => p.PendingCount)}");
        }
    }
}
