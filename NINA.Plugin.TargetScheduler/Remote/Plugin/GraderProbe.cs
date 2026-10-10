using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Grading;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class GraderProbe {
        private readonly IProfile profile;

        public GraderProbe(IProfile profile) {
            this.profile = profile;
        }

        public GraderReport Report(int targetId) {
            var prefs = new SchedulerPlanLoader(profile).GetProfilePreferences(new SchedulerDatabaseInteraction().GetContext());
            var graderPrefs = new ImageGraderPreferences(profile, prefs);
            var grader = new ImageGrader();
            using (var context = new SchedulerDatabaseInteraction().GetContext()) {
                var target = context.TargetSet.Include("exposureplans.exposuretemplate").Include("project").FirstOrDefault(t => t.Id == targetId);
                if (target == null) {
                    return null;
                }
                var settings = Settings(graderPrefs, target.Project?.EnableGrader ?? false);
                var report = new GraderReport {
                    GeneratedAt = DateTime.UtcNow,
                    ProjectId = target.ProjectId,
                    TargetId = target.Id,
                    TargetName = target.Name,
                    Hfr = settings,
                };
                var goals = ForkDatabase.LoadGoalProgress(context);
                foreach (var plan in target.ExposurePlans.Where(p => p.ExposureTemplate != null).OrderBy(p => p.Id)) {
                    var all = context.GetAcquiredImagesForGrading(plan);
                    var input = new GraderPlanInput {
                        PlanId = plan.Id,
                        Filter = plan.ExposureTemplate.FilterName,
                        ExposureSeconds = plan.Exposure > 0 ? plan.Exposure : plan.ExposureTemplate.DefaultExposure,
                        Acquired = all.Count,
                        GoalDriven = goals.Get(target.Guid, plan.ExposureTemplate.FilterName) != null,
                    };
                    var reference = all.FirstOrDefault(a => a.Metadata != null);
                    if (reference != null) {
                        var latest = reference.Metadata;
                        var next = reference.Metadata;
                        next.ExposureDuration = input.ExposureSeconds;
                        if (plan.ExposureTemplate.Gain >= 0) {
                            next.Gain = plan.ExposureTemplate.Gain;
                        }
                        if (plan.ExposureTemplate.Offset >= 0) {
                            next.Offset = plan.ExposureTemplate.Offset;
                        }
                        (List<AcquiredImage> population, int matching) = grader.NextImagePopulation(graderPrefs, target, profile.Id.ToString(), next, all);
                        input.ReferenceImageId = reference.Id;
                        input.ReferenceAt = reference.AcquiredDate.ToUniversalTime();
                        input.ReferenceExposure = latest.ExposureDuration;
                        input.Gain = next.Gain;
                        input.Offset = next.Offset;
                        input.Binning = next.Binning;
                        input.Roi = target.ROI;
                        input.Matching = matching;
                        input.Samples = new GraderExpert(graderPrefs, next).HFRSamples(population);
                    }
                    report.Plans.Add(GraderLimits.Build(settings, input));
                }
                return report;
            }
        }

        public static GraderHfrSettings Settings(IImageGraderPreferences p, bool projectGrading) {
            return new GraderHfrSettings {
                ProjectGrading = projectGrading,
                Enabled = p.EnableGradeHFR,
                SigmaFactor = p.HFRSigmaFactor,
                AutoAcceptLevel = p.AutoAcceptLevelHFR > 0 ? p.AutoAcceptLevelHFR : null,
                AcceptImprovement = p.AcceptImprovement,
                MaxSampleSize = p.MaxGradingSampleSize,
                DelayThresholdPercent = p.DelayGradingThreshold,
                Mode = p.IsDelayEnabled ? "delayed" : "immediate",
            };
        }
    }
}
