using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SubLengthTest {

        private static SubLengthTable Table(params SubLengthRow[] rows) {
            return new SubLengthTable(rows);
        }

        private static SubLengthRow Row(string guid, string filter, int gain, string bucket, double exposure) {
            return new SubLengthRow { TargetGuid = guid, Filter = filter, Gain = gain, Bucket = bucket, Exposure = exposure };
        }

        [TestCase(0.0, "dark")]
        [TestCase(0.0499999, "dark")]
        [TestCase(0.05, "low")]
        [TestCase(0.2499999, "low")]
        [TestCase(0.25, "mid")]
        [TestCase(0.4999999, "mid")]
        [TestCase(0.5, "bright")]
        [TestCase(1.0, "bright")]
        [TestCase(-0.1, "dark")]
        public void BucketEdgesMatchTheStacker(double glare, string bucket) {
            SubLength.Bucket(glare).Should().Be(bucket);
        }

        [Test]
        public void NoBucketWithoutGlare() {
            SubLength.Bucket(double.NaN).Should().BeNull();
            SubLengthChoice c = SubLength.Choose(Table(Row("g", "Ha", 100, "dark", 120)), "g", "Ha", 100, null, -1, 300);
            c.Bucket.Should().BeNull();
            c.Source.Should().Be(SubLength.SourceTemplate);
            c.Seconds.Should().Be(300);
        }

        [Test]
        public void BucketFollowsConditionMatchGlare() {
            double down = ConditionMatch.MoonGlare(-10, ConditionMatch.MoonIllumination(14.8), 20);
            SubLength.Bucket(down).Should().Be(SubLength.Dark);
            double full = ConditionMatch.MoonGlare(40, ConditionMatch.MoonIllumination(14.8), 20);
            SubLength.Bucket(full).Should().Be(SubLength.Bright);
        }

        [Test]
        public void SystemRowWins() {
            SubLengthChoice c = SubLength.Choose(Table(Row("g", "Ha", 100, "mid", 600)), "g", "Ha", 100, 0.3, 180, 300);
            c.Seconds.Should().Be(600);
            c.Source.Should().Be(SubLength.SourceSystem);
            c.Bucket.Should().Be(SubLength.Mid);
            c.Glare.Should().Be(0.3);
        }

        [Test]
        public void FallsBackToPlanThenTemplate() {
            SubLengthTable table = Table(Row("g", "Ha", 100, "dark", 0));
            SubLengthChoice plan = SubLength.Choose(table, "g", "Ha", 100, 0.01, 180, 300);
            plan.Source.Should().Be(SubLength.SourcePlan);
            plan.Seconds.Should().Be(180);
            plan.Bucket.Should().Be(SubLength.Dark);

            SubLengthChoice template = SubLength.Choose(table, "g", "Ha", 100, 0.01, -1, 300);
            template.Source.Should().Be(SubLength.SourceTemplate);
            template.Seconds.Should().Be(300);

            SubLength.Choose(table, "g", "Ha", 100, 0.01, 0, 300).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(null, "g", "Ha", 100, 0.01, -1, 300).Seconds.Should().Be(300);
        }

        [Test]
        public void LooksUpByGuidFilterGainAndBucket() {
            SubLengthTable table = Table(
                Row("guid-a", "Ha", 100, "dark", 600),
                Row("guid-a", "Ha", 0, "dark", 120),
                Row("guid-a", "OIII", 100, "dark", 900),
                Row("guid-a", "Ha", 100, "bright", 300),
                Row("guid-b", "Ha", 100, "dark", 60));

            SubLength.Choose(table, "guid-a", "Ha", 100, 0.0, -1, 30).Seconds.Should().Be(600);
            SubLength.Choose(table, "GUID-A", "Ha", 100, 0.0, -1, 30).Seconds.Should().Be(600);
            SubLength.Choose(table, "guid-a", "Ha", 0, 0.0, -1, 30).Seconds.Should().Be(120);
            SubLength.Choose(table, "guid-a", "OIII", 100, 0.0, -1, 30).Seconds.Should().Be(900);
            SubLength.Choose(table, "guid-a", "Ha", 100, 0.7, -1, 30).Seconds.Should().Be(300);
            SubLength.Choose(table, "guid-b", "Ha", 100, 0.0, -1, 30).Seconds.Should().Be(60);
            SubLength.Choose(table, "guid-a", "Ha", 139, 0.0, -1, 30).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "guid-a", "ha", 100, 0.0, -1, 30).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "guid-a", "Ha", 100, 0.1, -1, 30).Source.Should().Be(SubLength.SourceTemplate);
            SubLength.Choose(table, "guid-c", "Ha", 100, 0.0, -1, 30).Source.Should().Be(SubLength.SourceTemplate);
        }

        [Test]
        public void CameraDefaultGainMatchesAnyNegativeSentinel() {
            SubLengthTable table = Table(Row("g", "Ha", -1, "dark", 240));
            SubLength.Choose(table, "g", "Ha", -1, 0.0, -1, 30).Seconds.Should().Be(240);
            SubLength.Choose(table, "g", "Ha", -2, 0.0, -1, 30).Seconds.Should().Be(240);
            SubLength.Choose(table, "g", "Ha", 0, 0.0, -1, 30).Source.Should().Be(SubLength.SourceTemplate);
        }

        [Test]
        public void IgnoresInvalidRows() {
            SubLengthTable table = new SubLengthTable(new[] { null, Row(null, "Ha", 100, "dark", 600), Row("g", null, 100, "dark", 600), Row("g", "Ha", 100, null, 600), Row("g", "Ha", 100, "dark", double.NaN) });
            table.Count.Should().Be(1);
            SubLength.Choose(table, "g", "Ha", 100, 0.0, -1, 300).Source.Should().Be(SubLength.SourceTemplate);
        }

        [Test]
        public void CurrentNeverNull() {
            SubLengthTable.Current = null;
            SubLengthTable.Current.Should().BeSameAs(SubLengthTable.Empty);
            SubLengthTable.Current = Table(Row("g", "Ha", 100, "dark", 600));
            SubLengthTable.Current.Count.Should().Be(1);
            SubLengthTable.Current = SubLengthTable.Empty;
        }
    }
}
