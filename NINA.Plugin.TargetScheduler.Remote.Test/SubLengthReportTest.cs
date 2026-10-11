using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class SubLengthReportTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

        [Test]
        public void StatusCarriesTheLengthSource() {
            var t = new ExposureTracker();
            t.PlanStarted(13);
            t.ShutterOpened(41, "H-a", 300, Now, null, false, SubLength.SourceSystem, SubLength.Dark);
            var v = t.View(Now.AddSeconds(10), null);
            v.Exposure.Seconds.Should().Be(300);
            v.Exposure.LengthSource.Should().Be("system");
            v.Exposure.LengthBucket.Should().Be("dark");
            string json = JsonSerializer.Serialize(v.Exposure);
            json.Should().Contain("\"length_source\":\"system\"").And.Contain("\"length_bucket\":\"dark\"");
        }

        private static PlannedExposure Plan(int min, double seconds, string source, string bucket) {
            return new PlannedExposure {
                Start = Now.AddMinutes(min), End = Now.AddMinutes(min).AddSeconds(seconds), TargetId = 13, TargetName = "T13", Filter = "H-a",
                ExposureSeconds = seconds, LengthSource = source, LengthBucket = bucket, NewPick = min == 0,
            };
        }

        [Test]
        public void PreviewBlocksSplitWhenTheSystemLengthChangesBucket() {
            var plans = new List<PlannedExposure> {
                Plan(0, 300, "system", "dark"),
                Plan(5, 300, "system", "dark"),
                Plan(10, 300, "system", "low"),
                Plan(15, 120, "system", "bright"),
            };
            var blocks = PreviewBuilder.Blocks(plans);
            blocks.Select(b => (b.ExposureSeconds, b.LengthSource, b.LengthBucket, b.Count)).Should().Equal(
                (300.0, "system", "dark", 2), (300.0, "system", "low", 1), (120.0, "system", "bright", 1));
        }

        [Test]
        public void PreviewBlocksMergeTemplateLengthsAcrossBuckets() {
            var plans = new List<PlannedExposure> {
                Plan(0, 600, "template", "dark"),
                Plan(10, 600, "template", "low"),
                Plan(20, 600, "plan", "low"),
            };
            var blocks = PreviewBuilder.Blocks(plans);
            blocks.Select(b => (b.ExposureSeconds, b.LengthSource, b.LengthBucket, b.Count)).Should().Equal(
                (600.0, "template", (string)null, 2), (600.0, "plan", "low", 1));
            string json = JsonSerializer.Serialize(blocks[1]);
            json.Should().Contain("\"length_source\":\"plan\"").And.Contain("\"length_bucket\":\"low\"");
        }
    }
}
