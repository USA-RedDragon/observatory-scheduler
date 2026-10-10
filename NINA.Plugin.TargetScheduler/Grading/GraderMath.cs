using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Grading {

    public static class GraderMath {
        public const int MinimumSamples = 3;

        public static (double, double) MeanAndSampleStandardDeviation(IList<double> samples) {
            if (samples == null || samples.Count < MinimumSamples) {
                throw new Exception("must have >= 3 samples");
            }

            double mean = samples.Average();
            double sum = samples.Sum(d => Math.Pow(d - mean, 2));
            double stddev = Math.Sqrt(sum / (samples.Count - 1));
            return (mean, stddev);
        }

        public static bool ImprovementAccepted(double mean, double newSample, bool acceptImprovement, bool positiveImprovement) {
            if (!acceptImprovement) {
                return false;
            }
            return positiveImprovement ? newSample > mean : newSample < mean;
        }

        public static bool WithinSigma(double mean, double stddev, double newSample, double sigmaFactor) {
            return Math.Abs(mean - newSample) <= stddev * sigmaFactor;
        }
    }
}
