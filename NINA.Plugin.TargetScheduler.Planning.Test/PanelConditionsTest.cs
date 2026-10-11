using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class PanelConditionsTest {

        private static PanelExposure Sub(string filter, double glare, double seconds = 300, int day = 1) {
            return new PanelExposure { Filter = filter, Glare = glare, Seconds = seconds, At = new DateTime(2026, 10, day, 6, 0, 0, DateTimeKind.Utc) };
        }

        [TestCase(0.0, 0)]
        [TestCase(0.099, 0)]
        [TestCase(0.1, 1)]
        [TestCase(0.34, 1)]
        [TestCase(0.35, 2)]
        [TestCase(0.65, 3)]
        [TestCase(1.0, 3)]
        [TestCase(double.NaN, 0)]
        public void BucketsMoonGlare(double glare, int bucket) {
            PanelConditions.Bucket(glare).Should().Be(bucket);
        }

        [Test]
        public void HistogramSumsHoursPerBucketAndIgnoresJunk() {
            double[] h = PanelConditions.Histogram(new List<(double, double)> { (0.0, 1.5), (0.05, 0.5), (0.8, 2), (0.5, double.NaN), (0.2, -1) });
            h.Should().Equal(2.0, 0.0, 0.0, 2.0);
        }

        [Test]
        public void NoPrecedentMeansNoPreference() {
            PanelConditions.Match(null, 3).Should().Be(1);
            PanelConditions.Match(new double[] { 0.5, 0, 0, 0.4 }, 2).Should().Be(1);
            PanelConditions.Factor(new double[4], 0.9).Should().Be(1);
        }

        [Test]
        public void PrefersTheConditionsSiblingPanelsUsed() {
            double[] darkOnly = { 6, 0, 0, 0 };
            PanelConditions.Match(darkOnly, 0).Should().Be(1);
            PanelConditions.Match(darkOnly, 1).Should().Be(0.5);
            PanelConditions.Match(darkOnly, 2).Should().Be(0);
            PanelConditions.Match(darkOnly, 3).Should().Be(0);
            PanelConditions.Factor(darkOnly, 0.0).Should().Be(1);
            PanelConditions.Factor(darkOnly, 0.9).Should().Be(PanelConditions.Floor);
        }

        [Test]
        public void MixedPrecedentAcceptsEachConditionUsed() {
            double[] split = { 3, 0, 0, 3 };
            PanelConditions.Match(split, 0).Should().Be(1);
            PanelConditions.Match(split, 3).Should().Be(1);
            PanelConditions.Match(split, 1).Should().Be(0.5);
        }

        [Test]
        public void SiblingHoursCountsOnlyTheSameFilterByNormalizedName() {
            var subs = new[] { Sub("L", 0.0), Sub("l", 0.0), Sub(" L ", 0.9, 3600), Sub("Lum", 0.0, 3600), Sub("Ha", 0.9, 3600), Sub("HA", 0.5, 1800) };
            PanelConditions.SiblingHours(subs, "L", e => e.Glare.Value).Should().Equal(600.0 / 3600, 0.0, 0.0, 1.0);
            PanelConditions.SiblingHours(subs, "ha", e => e.Glare.Value).Should().Equal(0.0, 0.0, 0.5, 1.0);
            PanelConditions.SiblingHours(subs, null, e => 0).Should().BeNull();
        }

        [Test]
        public void ThreePanelsKeepLuminanceOnDarkNightsOnceTwoPanelsHaveIt() {
            var siblings = new List<PanelExposure>();
            for (int i = 0; i < 24; i++) { siblings.Add(Sub("L", 0.02)); }
            for (int i = 0; i < 12; i++) { siblings.Add(Sub("Ha", 0.8, 600)); }

            double[] l = PanelConditions.SiblingHours(siblings, "L", e => e.Glare.Value);
            double[] ha = PanelConditions.SiblingHours(siblings, "Ha", e => e.Glare.Value);
            PanelConditions.Factor(l, 0.85).Should().Be(PanelConditions.Floor);
            PanelConditions.Factor(ha, 0.85).Should().Be(1);
            PanelConditions.Factor(l, 0.03).Should().Be(1);
            PanelConditions.Factor(ha, 0.03).Should().Be(PanelConditions.Floor);
            PanelConditions.Factor(PanelConditions.SiblingHours(siblings, "OIII", e => e.Glare.Value), 0.85).Should().Be(1);
        }

        [Test]
        public void SteeringPrefersTheFilterSiblingsShotUnderTheseConditions() {
            List<SteeringCandidate> candidates = new List<SteeringCandidate> {
                new SteeringCandidate { Index = 0, Filter = "L", Goal = new GoalProgress { Progress = 0.2 }, MoonGlare = 0.05, PanelMatch = PanelConditions.Factor(new double[] { 0, 0, 0, 4 }, 0.05) },
                new SteeringCandidate { Index = 1, Filter = "R", Goal = new GoalProgress { Progress = 0.4 }, MoonGlare = 0.05, PanelMatch = PanelConditions.Factor(new double[] { 4, 0, 0, 0 }, 0.05) }
            };

            FilterSteering.Choose(candidates, -1).Should().Be(1);
            candidates[0].PanelMatch = 1;
            FilterSteering.Choose(candidates, -1).Should().Be(0);
        }

        [Test]
        public void ConditionMatchScoresAPanelLowerWhenItsFiltersDoNotMatchItsSiblings() {
            FilterCondition matched = new FilterCondition { Filter = "L", MoonGlare = 0.05, PanelMatch = 1 };
            FilterCondition unmatched = new FilterCondition { Filter = "L", MoonGlare = 0.05, PanelMatch = PanelConditions.Floor };
            double a = ConditionMatch.Score(60, 70, new[] { matched }, null);
            double b = ConditionMatch.Score(60, 70, new[] { unmatched }, null);
            b.Should().BeApproximately(a * PanelConditions.Floor, 1e-9);
            ConditionMatch.Score(60, 70, new[] { unmatched, new FilterCondition { Filter = "Ha", Narrowband = true, MoonGlare = 0.05, PanelMatch = 1 } }, null).Should().BeApproximately(b, 1e-9);
        }
    }
}
