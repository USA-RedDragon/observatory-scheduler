using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class AltKeepTest {

        private static AltKeepRow Row(string filter, bool nb, double from, double to, double worth, bool usable = true, bool skips = false) {
            return new AltKeepRow { Filter = filter, Narrowband = nb, AltFrom = from, AltTo = to, Subs = 200, Kept = (int)(worth * 160), Nights = 20, Rate = worth * 0.8, RateHigh = worth * 0.9, Worth = worth, Usable = usable, Skips = skips };
        }

        private static AltKeepTable Table() {
            return new AltKeepTable(new[] {
                Row("", false, 15, 20, 0.33), Row("", false, 20, 25, 0.66), Row("", false, 40, 90, 1),
                Row("", true, 15, 20, 0.37), Row("H-a", true, 15, 20, 0.2), Row("Red", false, 20, 25, 0.5, usable: false),
                Row("Green", false, 15, 20, 0.02, skips: true), null
            });
        }

        [Test]
        public void LooksUpTheFiltersOwnRowThenItsClass() {
            var t = Table();
            t.Count.Should().Be(7);
            t.Lookup("Ha", 17).Worth.Should().Be(0.2);
            t.Lookup("OIII", 17).Worth.Should().Be(0.37);
            t.Lookup("Red", 22).Worth.Should().Be(0.66);
            t.Lookup("Luminance", 90).Worth.Should().Be(1);
            t.Lookup("Luminance", 30).Should().BeNull();
            t.Lookup("Luminance", double.NaN).Should().BeNull();
            AltKeepTable.Empty.Lookup("Luminance", 17).Should().BeNull();
        }

        [Test]
        public void SkipsOnlyAUsableRowThatSaysSo() {
            var t = Table();
            AltKeep.Skips(t.Lookup("Green", 17)).Should().BeTrue();
            AltKeep.Skips(t.Lookup("Luminance", 17)).Should().BeFalse();
            AltKeep.Skips(null).Should().BeFalse();
            AltKeep.Explain(t.Lookup("Green", 17), 17.4).Should().Contain("Green").And.Contain("15-20°");
        }

        [Test]
        public void TheAltitudeWorthMultipliesTheFilterScore() {
            var high = new FilterCondition { Filter = "L", MoonGlare = 0 };
            var low = new FilterCondition { Filter = "L", MoonGlare = 0, AltWorth = 0.33 };
            ConditionMatch.FilterScore(high, null).Should().Be(1);
            ConditionMatch.FilterScore(low, null).Should().BeApproximately(0.33, 1e-9);
            var both = new FilterCondition { Filter = "L", MoonGlare = 0, Worth = 0.5, AltWorth = 0.5 };
            ConditionMatch.FilterScore(both, null).Should().BeApproximately(0.25, 1e-9);
        }

        [Test]
        public void SteeringPrefersTheFilterThatKeepsMoreAtThisAltitude() {
            var candidates = new List<SteeringCandidate> {
                new SteeringCandidate { Index = 0, Filter = "Blue", PercentComplete = 10, AltWorth = 0.24 },
                new SteeringCandidate { Index = 1, Filter = "Red", PercentComplete = 10, AltWorth = 0.37 },
            };
            FilterSteering.Evaluate(candidates);
            candidates[1].Value.Should().BeGreaterThan(candidates[0].Value);
            candidates[0].AltWorth = null;
            FilterSteering.Evaluate(candidates);
            candidates[0].Value.Should().BeGreaterThan(candidates[1].Value);
        }

        [Test]
        public void NoWorthWeighsOne() {
            AltKeep.Weight(null).Should().Be(1);
            AltKeep.Weight(double.NaN).Should().Be(1);
            AltKeep.Weight(1.4).Should().Be(1);
            AltKeep.Worth(null).Should().BeNull();
        }

        [Test]
        public void ATargetSkippedLowInTheSkyWaitsLikeABrightMoon() {
            AltKeep.Waits(new[] { AltKeep.FilterReason, AltKeep.FilterReason }).Should().BeTrue();
            AltKeep.Waits(new[] { AltKeep.FilterReason, "complete" }, "complete").Should().BeTrue();
            AltKeep.Waits(new[] { AltKeep.FilterReason, "humidity" }).Should().BeFalse();
            AltKeep.Waits(new[] { "complete" }, "complete").Should().BeFalse();
            AltKeep.Waits(null).Should().BeFalse();
            MoonKeep.Waits(new[] { MoonKeep.FilterReason, AltKeep.FilterReason }, AltKeep.FilterReason).Should().BeTrue();
        }

        [Test]
        public void OnlyManagedExposuresOutsideEventWindowsAreWeighed() {
            AltKeep.Applies(true, false).Should().BeTrue();
            AltKeep.Applies(false, false).Should().BeFalse();
            AltKeep.Applies(true, true).Should().BeFalse();
        }
    }
}
