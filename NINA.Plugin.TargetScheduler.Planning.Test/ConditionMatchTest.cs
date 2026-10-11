using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class ConditionMatchTest {

        [TestCase("Ha", true)]
        [TestCase("H-alpha", true)]
        [TestCase("H-a", true)]
        [TestCase("HA 3nm", true)]
        [TestCase("OIII", true)]
        [TestCase("O-III", true)]
        [TestCase("SII", true)]
        [TestCase("S-II", true)]
        [TestCase("Hb", true)]
        [TestCase("L-eXtreme", true)]
        [TestCase("Dual Band", true)]
        [TestCase("7nm", true)]
        [TestCase("L", false)]
        [TestCase("Lum", false)]
        [TestCase("Luminance", false)]
        [TestCase("R", false)]
        [TestCase("Red", false)]
        [TestCase("G", false)]
        [TestCase("B", false)]
        [TestCase("H", false)]
        [TestCase("UV/IR Cut", false)]
        [TestCase("L-Pro", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ClassifiesFilters(string filter, bool narrowband) {
            FilterKind.IsNarrowband(filter).Should().Be(narrowband);
        }

        [Test]
        public void IlluminationFollowsTheMoonAge() {
            ConditionMatch.MoonIllumination(0).Should().BeApproximately(0, 1e-9);
            ConditionMatch.MoonIllumination(ConditionMatch.LunarCycleDays / 2).Should().BeApproximately(1, 1e-9);
            ConditionMatch.MoonIllumination(ConditionMatch.LunarCycleDays / 4).Should().BeApproximately(0.5, 1e-9);
        }

        [TestCase(-10, 1, 20, 0)]
        [TestCase(30, 1, 20, 1)]
        [TestCase(30, 1, 30, 1)]
        [TestCase(30, 1, 90, 0.5)]
        [TestCase(30, 1, 150, 0)]
        [TestCase(30, 0.5, 90, 0.25)]
        [TestCase(0, 1, 30, 0.5)]
        [TestCase(30, 0, 10, 0)]
        public void GlareNeedsTheMoonUpBrightAndNear(double moonAlt, double illumination, double separation, double expected) {
            ConditionMatch.MoonGlare(moonAlt, illumination, separation).Should().BeApproximately(expected, 1e-9);
        }

        [Test]
        public void PeakAltitudeIsCulminationCappedByTheProjectMaximum() {
            ConditionMatch.PeakAltitude(40, 20, 0).Should().Be(70);
            ConditionMatch.PeakAltitude(40, 60, 0).Should().Be(70);
            ConditionMatch.PeakAltitude(40, 40, 0).Should().Be(90);
            ConditionMatch.PeakAltitude(40, 40, 75).Should().Be(75);
        }

        [Test]
        public void AltitudeScoresAgainstTheTargetsOwnPeak() {
            ConditionMatch.AltitudeScore(70, 70).Should().BeApproximately(1, 1e-9);
            ConditionMatch.AltitudeScore(30, 90).Should().BeApproximately(0.5, 1e-9);
            ConditionMatch.AltitudeScore(-5, 70).Should().Be(0);
            ConditionMatch.AltitudeScore(30, 0).Should().Be(0);
        }

        [Test]
        public void NarrowbandToleratesMoonAndPoorSky() {
            ConditionMatch.MoonScore(false, 0.8).Should().BeApproximately(0.2, 1e-9);
            ConditionMatch.MoonScore(true, 0.8).Should().BeApproximately(0.8, 1e-9);
            ConditionMatch.SkyScore(false, 0.4).Should().BeApproximately(0.4, 1e-9);
            ConditionMatch.SkyScore(true, 0.4).Should().BeApproximately(0.7, 1e-9);
            ConditionMatch.SkyScore(false, null).Should().Be(1);
            ConditionMatch.SkyScore(true, null).Should().Be(1);
        }

        [Test]
        public void ScoreTakesTheBestFilterAtTheCurrentAltitude() {
            var broad = new FilterCondition { Filter = "L", Narrowband = false, MoonGlare = 0.9 };
            var narrow = new FilterCondition { Filter = "Ha", Narrowband = true, MoonGlare = 0.9 };

            double broadOnly = ConditionMatch.Score(70, 70, new List<FilterCondition> { broad }, null);
            double withNarrow = ConditionMatch.Score(70, 70, new List<FilterCondition> { broad, narrow }, null);
            broadOnly.Should().BeApproximately(0.1, 1e-9);
            withNarrow.Should().BeApproximately(0.775, 1e-9);

            ConditionMatch.Score(30, 90, new List<FilterCondition> { narrow }, null).Should().BeApproximately(0.3875, 1e-9);
            ConditionMatch.Score(70, 70, new List<FilterCondition>(), 1).Should().Be(0);
        }

        [Test]
        public void GoodSkiesFavourBroadbandTargetsLessThanPoorSkiesHurtThem() {
            var broad = new List<FilterCondition> { new FilterCondition { Filter = "R" } };
            var narrow = new List<FilterCondition> { new FilterCondition { Filter = "Ha", Narrowband = true } };

            ConditionMatch.Score(60, 60, broad, 1).Should().BeApproximately(1, 1e-9);
            ConditionMatch.Score(60, 60, narrow, 1).Should().BeApproximately(1, 1e-9);
            ConditionMatch.Score(60, 60, broad, 0.3).Should().BeLessThan(ConditionMatch.Score(60, 60, narrow, 0.3));
        }

        [Test]
        public void AMixedTargetIsCreditedForTheKindFilterSteeringShoots() {
            var mixed = new List<FilterCondition> {
                new FilterCondition { Filter = "R" },
                new FilterCondition { Filter = "Ha", Narrowband = true }
            };

            ConditionMatch.Score(60, 60, mixed, 0.8).Should().BeApproximately(0.8, 1e-9);
            ConditionMatch.Score(60, 60, mixed, 0.3).Should().BeApproximately(0.65, 1e-9);
            ConditionMatch.Score(60, 60, mixed, null).Should().BeApproximately(1, 1e-9);

            var narrowOnly = new List<FilterCondition> { new FilterCondition { Filter = "Ha", Narrowband = true } };
            ConditionMatch.Score(60, 60, narrowOnly, 0.8).Should().BeApproximately(0.9, 1e-9);
        }
    }
}
