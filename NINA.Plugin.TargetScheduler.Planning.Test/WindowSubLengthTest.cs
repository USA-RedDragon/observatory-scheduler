using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class WindowSubLengthTest {

        private static SubLengthTable SystemRow(double seconds) {
            return new SubLengthTable(new[] { new SubLengthRow { TargetGuid = "g", Filter = "L", Gain = 100, Bucket = SubLength.Dark, Exposure = seconds } });
        }

        [Test]
        public void APlanExposureWinsOverASystemRowForWindowedTargets() {
            SubLengthChoice plan = SubLength.Choose(SystemRow(120), "g", "L", 100, 0.0, 5, 600, true);
            plan.Seconds.Should().Be(5);
            plan.Source.Should().Be(SubLength.SourcePlan);
            plan.Bucket.Should().Be(SubLength.Dark);

            SubLengthChoice noPlan = SubLength.Choose(SystemRow(120), "g", "L", 100, 0.0, -1, 600, true);
            noPlan.Seconds.Should().Be(120);
            noPlan.Source.Should().Be(SubLength.SourceSystem);

            SubLengthChoice ordinary = SubLength.Choose(SystemRow(120), "g", "L", 100, 0.0, 5, 600, false);
            ordinary.Seconds.Should().Be(120);
            ordinary.Source.Should().Be(SubLength.SourceSystem);
        }

        [Test]
        public void TheTrailCapStillAppliesToAPlanExposure() {
            SubLengthChoice plan = SubLength.Choose(SystemRow(120), "g", "L", 100, 0.0, 30, 600, true);
            BodyTrail.Cap(plan, BodyTrail.MaxSeconds(60 * 60, BodyField.Default)).Should().BeTrue();
            plan.Seconds.Should().Be(1);
            plan.Source.Should().Be(BodyTrail.SourceBodyRate);

            SubLengthChoice shortPlan = SubLength.Choose(SystemRow(120), "g", "L", 100, 0.0, 0.5, 600, true);
            BodyTrail.Cap(shortPlan, BodyTrail.MaxSeconds(60 * 60, BodyField.Default)).Should().BeFalse();
            shortPlan.Seconds.Should().Be(0.5);
        }

        [Test]
        public void WithoutAWindowTheChoiceIsUnchanged() {
            foreach (double? glare in new double?[] { null, 0.0, 0.3, 0.7, double.NaN }) {
                foreach (double planLength in new[] { -1.0, 0, 5, 300 }) {
                    foreach (SubLengthTable table in new[] { SubLengthTable.Empty, SystemRow(120) }) {
                        SubLengthChoice before = SubLength.Choose(table, "g", "L", 100, glare, planLength, 600);
                        SubLengthChoice after = SubLength.Choose(table, "g", "L", 100, glare, planLength, 600, false);
                        after.Should().BeEquivalentTo(before);
                    }
                }
            }
        }
    }
}
