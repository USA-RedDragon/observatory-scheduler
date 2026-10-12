using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class MoonKeepTest {

        private static MoonKeepRow Row(string filter, bool nb, double illumFrom, double illumTo, double altFrom, double altTo, double sepFrom, double sepTo,
            double rate, double high, double worth, bool usable, bool skips) {
            return new MoonKeepRow {
                Filter = filter, Narrowband = nb, IllumFrom = illumFrom, IllumTo = illumTo, AltFrom = altFrom, AltTo = altTo, SepFrom = sepFrom, SepTo = sepTo,
                Subs = 200, Kept = (int)(rate * 200), Nights = 20, Rate = rate, RateHigh = high, Worth = worth, Usable = usable, Skips = skips
            };
        }

        private static MoonKeepRow Down(string filter, bool nb) {
            return new MoonKeepRow { Filter = filter, Narrowband = nb, MoonDown = true, IllumTo = 1, AltFrom = -90, SepTo = 180, Subs = 500, Kept = 400, Nights = 50, Rate = 0.8, RateHigh = 0.85, Worth = 1, Usable = true };
        }

        private static MoonKeepTable Table() {
            return new MoonKeepTable(new[] {
                Down("", false), Down("", true), Down("Red", false),
                Row("", false, 0.9, 1, 35, 90, 45, 75, 0.02, 0.025, 0.03, true, true),
                Row("", false, 0.9, 1, 35, 90, 75, 110, 0.05, 0.08, 0.06, true, false),
                Row("", true, 0.9, 1, 35, 90, 45, 75, 0.3, 0.4, 0.37, true, false),
                Row("Red", false, 0.9, 1, 35, 90, 75, 110, 0.01, 0.02, 0.012, true, true),
                Row("Green", false, 0.9, 1, 35, 90, 45, 75, 0.2, 0.3, 0.25, false, false),
                Row("", false, 0.75, 0.9, 0, 15, 20, 45, 0.3, 0.4, 0.38, true, false),
                null
            });
        }

        private static MoonState Moon(double illum, double alt, double sep) {
            return new MoonState { Illumination = illum, Altitude = alt, Separation = sep };
        }

        [Test]
        public void ConstantsMatchTheStacker() {
            MoonKeep.GoodNightQuality.Should().Be(0.3);
            MoonKeep.FilterReason.Should().Be("bright Moon");
        }

        [TestCase("H-a", "Ha")]
        [TestCase("H-a", "H-alpha")]
        [TestCase("O-III", "OIII")]
        [TestCase("O-III", "O3")]
        [TestCase("S-II", "SII")]
        [TestCase("Red", " RED ")]
        public void FilterNamesMatchTheStackers(string stacker, string template) {
            MoonKeep.FilterKey(stacker).Should().Be(MoonKeep.FilterKey(template));
            var t = new MoonKeepTable(new[] { Row(stacker, true, 0.9, 1, 35, 90, 45, 75, 0.01, 0.02, 0.01, true, true) });
            t.Lookup(template, Moon(0.95, 50, 60)).Should().NotBeNull();
        }

        [Test]
        public void ClassFallbackFollowsTheStackersClassForAFilter() {
            var t = new MoonKeepTable(new[] {
                Row("Hb", false, 0.9, 1, 35, 90, 75, 110, 0.5, 0.6, 0.6, false, false),
                Row("", false, 0.9, 1, 35, 90, 45, 75, 0.02, 0.03, 0.02, true, true),
                Row("", true, 0.9, 1, 35, 90, 45, 75, 0.3, 0.4, 0.4, true, false)
            });
            t.Lookup("Hb", Moon(0.95, 50, 60)).Narrowband.Should().BeFalse("the stacker fitted Hb as broadband");
            t.Lookup("Ha", Moon(0.95, 50, 60)).Narrowband.Should().BeTrue();
            MoonKeep.FilterKey("  ").Should().BeNull();
        }

        [Test]
        public void EmptyTableAnswersNothing() {
            MoonKeepTable.Empty.Count.Should().Be(0);
            MoonKeepTable.Empty.Lookup("Red", Moon(1, 50, 60)).Should().BeNull();
            MoonKeepTable.Empty.Broadband(Moon(1, 50, 60)).Should().BeNull();
            MoonKeep.Worth(null).Should().BeNull();
            MoonKeep.Skips(null, null).Should().BeFalse();
            MoonKeep.Explain(null, Moon(1, 50, 60)).Should().BeNull();
        }

        [Test]
        public void FilterRowsWinOverTheClassRowWhenUsable() {
            MoonKeepTable t = Table();
            t.Count.Should().Be(9);
            t.Lookup("Red", Moon(0.95, 50, 90)).Filter.Should().Be("Red");
            t.Lookup(" red ", Moon(0.95, 50, 90)).Filter.Should().Be("Red");
            t.Lookup("Red", Moon(0.95, 50, 60)).Filter.Should().Be("");
            t.Lookup("Green", Moon(0.95, 50, 60)).Filter.Should().Be("", "the Green row has too few subs to use");
            t.Lookup("Luminance", Moon(0.95, 50, 60)).Rate.Should().Be(0.02);
            t.Lookup("H-a", Moon(0.95, 50, 60)).Narrowband.Should().BeTrue();
            t.Broadband(Moon(0.95, 50, 60)).Rate.Should().Be(0.02);
        }

        [Test]
        public void BinsAreHalfOpenUpToTheTopEdge() {
            MoonKeepTable t = Table();
            t.Lookup("Blue", Moon(0.9, 35, 45)).Should().NotBeNull();
            t.Lookup("Blue", Moon(1, 90, 75)).RateHigh.Should().Be(0.08);
            t.Lookup("Blue", Moon(1, 95, 60)).Should().NotBeNull("an altitude over 90 counts as 90");
            t.Lookup("Blue", Moon(0.89, 50, 60)).Should().BeNull();
            t.Lookup("Blue", Moon(0.95, 34.9, 60)).Should().BeNull();
            t.Lookup("Blue", Moon(0.95, 50, 19)).Should().BeNull("inside the moon floor there is no row");
            t.Lookup("Blue", Moon(0.8, 10, 30)).Rate.Should().Be(0.3);
            t.Lookup("Blue", Moon(double.NaN, 50, 60)).Should().BeNull();
        }

        [Test]
        public void MoonDownUsesTheDownRow() {
            MoonKeepTable t = Table();
            t.Lookup("Red", Moon(1, 0, 10)).MoonDown.Should().BeTrue();
            t.Lookup("Red", Moon(1, -20, 10)).Filter.Should().Be("Red");
            t.Lookup("O-III", Moon(1, -20, 10)).Narrowband.Should().BeTrue();
            MoonKeep.Skips(t.Lookup("Red", Moon(1, -20, 10)), null).Should().BeFalse();
            MoonKeep.Worth(t.Lookup("Red", Moon(1, -20, 10))).Should().Be(1);
        }

        [Test]
        public void SkipsOnlyUsableSkipRowsAndNotOnAGoodNight() {
            MoonKeepRow row = Table().Lookup("Luminance", Moon(0.95, 50, 60));
            MoonKeep.Skips(row, null).Should().BeTrue();
            MoonKeep.Skips(row, 0.25).Should().BeTrue();
            MoonKeep.Skips(row, 0.3).Should().BeFalse();
            MoonKeep.Skips(Table().Lookup("Blue", Moon(0.95, 50, 90)), null).Should().BeFalse();
            row.Usable = false;
            MoonKeep.Skips(row, null).Should().BeFalse();
        }

        [Test]
        public void ExplainsInPlainWords() {
            MoonKeepRow row = Table().Lookup("Luminance", Moon(0.95, 50, 60));
            MoonKeep.Explain(row, Moon(0.944, 50.2, 60.4)).Should().Be("Moon 94% lit, 50° up, 60° away: past broadband subs like this kept 4 of 200 (under 2.5%)");
            MoonKeep.Explain(Table().Lookup("Red", Moon(0.95, 50, 90)), Moon(0.95, 50, 90)).Should().Contain("past Red subs");
        }

        [Test]
        public void WaitsWhenEveryPlanIsLeftForTheMoonOrTwilight() {
            MoonKeep.Waits(new[] { "bright Moon", "bright Moon" }, "moon avoidance", "twilight").Should().BeTrue();
            MoonKeep.Waits(new[] { "bright Moon", "twilight" }, "moon avoidance", "twilight").Should().BeTrue();
            MoonKeep.Waits(new[] { "bright Moon", "humidity" }, "moon avoidance", "twilight").Should().BeFalse();
            MoonKeep.Waits(new[] { "bright Moon", "humidity" }, "moon avoidance", "twilight", "humidity").Should().BeTrue();
            MoonKeep.Waits(new[] { "moon avoidance", "twilight" }, "moon avoidance", "twilight").Should().BeFalse();
            MoonKeep.Waits(new List<string>(), "moon avoidance").Should().BeFalse();
            MoonKeep.Waits(null).Should().BeFalse();
        }

        [Test]
        public void ConditionMatchUsesTheWorthWhenThereIsOne() {
            var noModel = new FilterCondition { Filter = "H-a", Narrowband = true, MoonGlare = 0.8 };
            ConditionMatch.FilterScore(noModel, null).Should().BeApproximately(1 - 0.25 * 0.8, 1e-9);
            var model = new FilterCondition { Filter = "H-a", Narrowband = true, MoonGlare = 0.8, Worth = 0.1, BroadbandWorth = 0.02 };
            ConditionMatch.FilterScore(model, null).Should().BeApproximately(0.1, 1e-9);
            ConditionMatch.MoonScore(true, 0.8, null).Should().Be(ConditionMatch.MoonScore(true, 0.8));
            ConditionMatch.MoonScore(false, 0.1, 1.4).Should().Be(1);
            ConditionMatch.MoonScore(false, 0.1, double.NaN).Should().Be(ConditionMatch.MoonScore(false, 0.1));
        }

        [Test]
        public void NarrowbandIsFavoredByTheBroadbandWorth() {
            ConditionMatch.NarrowbandFavored(0.2, null, null).Should().Be(ConditionMatch.NarrowbandFavored(0.2, null));
            ConditionMatch.NarrowbandFavored(0.2, null).Should().BeFalse();
            ConditionMatch.NarrowbandFavored(0.2, null, 0.3).Should().BeTrue("broadband keeps under half its dark-sky rate");
            ConditionMatch.NarrowbandFavored(0.9, null, 0.8).Should().BeFalse("glare is high but broadband subs are still kept");
            var bb = new FilterCondition { Filter = "Red", MoonGlare = 0.9, Worth = 0.8, BroadbandWorth = 0.8 };
            var nb = new FilterCondition { Filter = "H-a", Narrowband = true, MoonGlare = 0.9, Worth = 0.9, BroadbandWorth = 0.8 };
            ConditionMatch.Favored(bb, null).Should().BeTrue();
            ConditionMatch.Favored(nb, null).Should().BeFalse();
            ConditionMatch.Score(60, 60, new[] { bb, nb }, null).Should().BeApproximately(0.8, 1e-9);
        }

        [Test]
        public void SteeringUsesTheWorth() {
            var red = new SteeringCandidate { Index = 0, Filter = "Red", Goal = null, PercentComplete = 0, MoonGlare = 0.9, Worth = 0.7, BroadbandWorth = 0.7 };
            var ha = new SteeringCandidate { Index = 1, Filter = "H-a", Narrowband = true, Goal = null, PercentComplete = 0, MoonGlare = 0.9, Worth = 0.2, BroadbandWorth = 0.7 };
            var list = new List<SteeringCandidate> { red, ha };
            FilterSteering.Evaluate(list);
            red.Value.Should().BeApproximately(0.7, 1e-9);
            ha.Value.Should().BeApproximately(0.2, 1e-9);
            FilterSteering.Favored(red, null).Should().BeTrue();
            FilterSteering.Favored(ha, null).Should().BeFalse();
            red.Worth = null;
            red.BroadbandWorth = null;
            ha.Worth = null;
            ha.BroadbandWorth = null;
            FilterSteering.Evaluate(list);
            red.Value.Should().BeApproximately(1 - 0.9, 1e-9);
            ha.Value.Should().BeApproximately(1 - 0.25 * 0.9, 1e-9);
            FilterSteering.Favored(ha, null).Should().BeTrue("without a model the glare rule decides as before");
        }
    }
}
