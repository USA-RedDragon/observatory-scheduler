using NINA.Plugin.TargetScheduler.Grading;
using System;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class GraderLimits {
        public const string StateLimit = "limit";
        public const string StateProjectGradingOff = "project_grading_off";
        public const string StateHfrGradingOff = "hfr_grading_off";
        public const string StateNoImages = "no_images";
        public const string StateTooFewSamples = "too_few_samples";
        public const string StateInvalidSamples = "invalid_samples";

        public static GraderHfrLimit Build(GraderHfrSettings settings, GraderPlanInput plan) {
            var limit = new GraderHfrLimit {
                PlanId = plan.PlanId,
                Filter = plan.Filter,
                ExposureSeconds = plan.ExposureSeconds,
                Acquired = plan.Acquired,
                Matching = plan.Matching,
                Samples = plan.Samples?.Count ?? 0,
                ReferenceImageId = plan.ReferenceImageId,
                ReferenceAt = plan.ReferenceAt,
                ReferenceExposure = plan.ReferenceExposure,
                Gain = plan.Gain,
                Offset = plan.Offset,
                Binning = plan.Binning,
                Roi = plan.Roi,
            };
            if (!settings.ProjectGrading) {
                limit.State = StateProjectGradingOff;
                return limit;
            }
            if (!settings.Enabled) {
                limit.State = StateHfrGradingOff;
                return limit;
            }
            if (plan.ReferenceImageId == null) {
                limit.State = StateNoImages;
                return limit;
            }
            limit.PopulationRule = PopulationRule(settings, limit);
            if (limit.Samples < GraderMath.MinimumSamples) {
                limit.State = StateTooFewSamples;
                return limit;
            }
            double auto = settings.AutoAcceptLevel ?? 0;
            limit.ZeroRejected = !(auto > 0);
            limit.InvalidSamples = plan.Samples.Count(x => double.IsNaN(x) || double.IsInfinity(x));
            if (limit.InvalidSamples > 0) {
                limit.State = StateInvalidSamples;
                return limit;
            }
            (double mean, double sd) = GraderMath.MeanAndSampleStandardDeviation(plan.Samples.ToList());
            limit.State = StateLimit;
            limit.Mean = mean;
            limit.Sd = sd;
            limit.Lower = mean - settings.SigmaFactor * sd;
            limit.Upper = mean + settings.SigmaFactor * sd;
            limit.RejectAbove = auto > 0 ? Math.Max(limit.Upper.Value, auto) : limit.Upper;
            limit.RejectBelow = settings.AcceptImprovement || (auto > 0 && auto >= limit.Lower.Value) ? null : limit.Lower;
            return limit;
        }

        public static string PopulationRule(GraderHfrSettings settings, GraderHfrLimit limit) {
            var match = "exposure, gain, offset, binning and ROI the next sub will use";
            if (settings.Mode == "delayed") {
                return string.Create(CultureInfo.InvariantCulture,
                    $"all {limit.Samples} subs of this exposure plan with the {match}, any grading status; grading waits until the plan has {settings.DelayThresholdPercent:0.#}% of its desired subs");
            }
            return string.Create(CultureInfo.InvariantCulture,
                $"the {limit.Samples} newest of {limit.Matching} subs of this exposure plan with the {match}, any grading status (sample size {settings.MaxSampleSize} includes the new sub)");
        }
    }
}
