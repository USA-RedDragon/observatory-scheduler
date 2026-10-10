using FluentAssertions;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Exposures {

    [TestFixture]
    public class MosaicCompletionTest {

        [Test]
        public void testPanelProgressIsWeakestPlan() {
            MosaicCompletion.PanelProgress(new List<double> { 0.9, 0.4, 0.7 }).Should().BeApproximately(0.4, 0.00001);
            MosaicCompletion.PanelProgress(new List<double> { 1.5, 0.8 }).Should().BeApproximately(0.8, 0.00001);
            MosaicCompletion.PanelProgress(new List<double> { 1.5, 2 }).Should().BeApproximately(1, 0.00001);
            MosaicCompletion.PanelProgress(new List<double>()).Should().Be(0);
            MosaicCompletion.PanelProgress((IEnumerable<double>)null).Should().Be(0);
        }

        [Test]
        public void testMosaicProgressIsWeakestPanel() {
            MosaicCompletion.MosaicProgress(new List<double> { 0.5, 0.2, 0.9 }).Should().BeApproximately(0.2, 0.00001);
            MosaicCompletion.MosaicProgress(new List<double> { -1, 0.5 }).Should().Be(0);
            MosaicCompletion.MosaicProgress(new List<double>()).Should().Be(0);
        }

        [Test]
        public void testIsPastGoal() {
            MosaicCompletion.IsPastGoal(1).Should().BeTrue();
            MosaicCompletion.IsPastGoal(1.3).Should().BeTrue();
            MosaicCompletion.IsPastGoal(0.999).Should().BeFalse();
            MosaicCompletion.IsPastGoal(double.NaN).Should().BeFalse();
        }

        [Test]
        public void testWeakestPanelScoresItsDeficit() {
            MosaicCompletion.DeficitScore(0.2, 0.2).Should().BeApproximately(0.8, 0.00001);
            MosaicCompletion.DeficitScore(0, 0).Should().BeApproximately(1, 0.00001);
        }

        [Test]
        public void testWeakestFirstOrdering() {
            double pmin = 0.2;
            double weakest = MosaicCompletion.DeficitScore(0.2, pmin);
            double middle = MosaicCompletion.DeficitScore(0.5, pmin);
            double strongest = MosaicCompletion.DeficitScore(0.9, pmin);

            weakest.Should().BeGreaterThan(middle);
            middle.Should().BeGreaterThan(strongest);
            middle.Should().BeApproximately(0.25 / 0.8, 0.00001);
            strongest.Should().BeApproximately(0.01 / 0.8, 0.00001);
        }

        [Test]
        public void testPastGoalScoresZero() {
            MosaicCompletion.DeficitScore(1, 0.3).Should().Be(0);
            MosaicCompletion.DeficitScore(1.7, 0.3).Should().Be(0);
            MosaicCompletion.DeficitScore(1, 1).Should().Be(0);
        }

        [Test]
        public void testScoresStayInRange() {
            double[] values = { -0.5, 0, 0.1, 0.5, 0.99, 1, 2, double.NaN };
            foreach (double p in values) {
                foreach (double pmin in values) {
                    double score = MosaicCompletion.DeficitScore(p, pmin);
                    score.Should().BeInRange(0, 1);
                    double.IsNaN(score).Should().BeFalse();
                }
            }
        }

        [Test]
        public void testMosaicMinimumAbovePanelIsCapped() {
            MosaicCompletion.DeficitScore(0.3, 0.6).Should().BeApproximately(0.7, 0.00001);
        }
    }
}
