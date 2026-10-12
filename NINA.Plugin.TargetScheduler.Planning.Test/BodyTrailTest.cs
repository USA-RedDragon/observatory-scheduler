using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BodyTrailTest {

        private static SubLengthTable Table(double seconds) {
            return new SubLengthTable(new[] { new SubLengthRow { TargetGuid = "g", Filter = "L", Gain = 100, Bucket = SubLength.Dark, Exposure = seconds } });
        }

        [Test]
        public void SixtyArcsecPerMinuteAtTheRigScaleAllowsOnePointNineOneSeconds() {
            BodyTrail.MaxSeconds(60 * 60, BodyField.Default).Should().BeApproximately(1.91, 1e-12);
            BodyTrail.MaxSeconds(60, BodyField.Default).Should().BeApproximately(114.6, 1e-9);
            BodyTrail.MaxSeconds(0, BodyField.Default).Should().BeNull();
            BodyTrail.MaxSeconds(null, BodyField.Default).Should().BeNull();
            BodyTrail.MaxSeconds(3600, BodyField.From(3.76, 405, null, null)).Should().BeApproximately(206.265 * 3.76 / 405, 1e-12);
            BodyTrail.MaxSeconds(60 * 60, BodyField.Default, 2).Should().BeApproximately(3.82, 1e-12);
            BodyTrail.MaxSeconds(60 * 60, BodyField.Default, 0).Should().BeApproximately(1.91, 1e-12);
        }

        [Test]
        public void CappedLengthsAreWholeSecondsAndAtLeastOneSecond() {
            SubLengthChoice binned = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 300, 600);
            BodyTrail.Cap(binned, BodyTrail.MaxSeconds(60 * 60, BodyField.Default, 2)).Should().BeTrue();
            binned.Seconds.Should().Be(3);

            SubLengthChoice fast = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 300, 600);
            BodyTrail.Cap(fast, BodyTrail.MaxSeconds(100 * 3600, BodyField.Default)).Should().BeTrue();
            fast.Seconds.Should().Be(BodyTrail.MinimumSeconds);
            fast.Source.Should().Be(BodyTrail.SourceBodyRate);

            SubLengthChoice tiny = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 0.5, 600);
            BodyTrail.Cap(tiny, 0.2).Should().BeFalse();
            tiny.Seconds.Should().Be(0.5);
        }

        [Test]
        public void EverySourceIsCapped() {
            double? max = BodyTrail.MaxSeconds(60 * 60, BodyField.Default);
            SubLengthChoice system = SubLength.Choose(Table(120), "g", "L", 100, 0.0, 300, 600);
            SubLengthChoice plan = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 300, 600);
            SubLengthChoice template = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, -1, 600);
            system.Source.Should().Be(SubLength.SourceSystem);
            plan.Source.Should().Be(SubLength.SourcePlan);
            template.Source.Should().Be(SubLength.SourceTemplate);
            foreach (SubLengthChoice c in new[] { system, plan, template }) {
                BodyTrail.Cap(c, max).Should().BeTrue();
                c.Seconds.Should().Be(1);
                c.Source.Should().Be(BodyTrail.SourceBodyRate);
                c.Bucket.Should().Be(SubLength.Dark);
            }
        }

        [Test]
        public void ASlowBodyLeavesAShorterLengthUntouchedAndNothingIsRaised() {
            double? max = BodyTrail.MaxSeconds(60, BodyField.Default);
            SubLengthChoice system = SubLength.Choose(Table(60), "g", "L", 100, 0.0, 300, 600);
            BodyTrail.Cap(system, max).Should().BeFalse();
            system.Seconds.Should().Be(60);
            system.Source.Should().Be(SubLength.SourceSystem);

            SubLengthChoice plan = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 300, 600);
            BodyTrail.Cap(plan, max).Should().BeTrue();
            plan.Seconds.Should().Be(114);

            SubLengthChoice untouched = SubLength.Choose(SubLengthTable.Empty, "g", "L", 100, 0.0, 300, 600);
            BodyTrail.Cap(untouched, null).Should().BeFalse();
            BodyTrail.Cap(untouched, double.NaN).Should().BeFalse();
            untouched.Seconds.Should().Be(300);
        }
    }
}
