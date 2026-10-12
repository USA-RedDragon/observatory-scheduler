using FluentAssertions;
using NINA.Plugin.TargetScheduler.Flats;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class FlatWaitFitTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 26, 5, 0, 0, DateTimeKind.Utc);

        [Test]
        public void EstimateCoversTheMeasuredDawnFlats() {
            TimeSpan e = FlatWaitFit.Estimate(new[] { 0.5, 0.5, 0.5, 0.5, 0.5 }, 30);
            e.TotalSeconds.Should().Be(60 + 5 * (30 + 30 * 4.0));
            e.TotalSeconds.Should().BeGreaterThan(665);
            FlatWaitFit.Estimate(null, 30).TotalSeconds.Should().Be(60);
            FlatWaitFit.Estimate(new[] { double.NaN }, -3).TotalSeconds.Should().Be(90);
        }

        [Test]
        public void TheLightNeedsACoverConfirmedClosed() {
            FlatWaitFit.CoverStopReason(true, true, "Closed").Should().BeNull();
            FlatWaitFit.CoverStopReason(false, true, "Closed").Should().Contain("not connected");
            FlatWaitFit.CoverStopReason(true, false, "NotPresent").Should().Contain("no cover");
            FlatWaitFit.CoverStopReason(true, true, "Unknown").Should().Contain("Unknown, not Closed");
            FlatWaitFit.CoverStopReason(true, true, "Open").Should().Contain("Open, not Closed");
        }

        [Test]
        public void FlatsRunOnlyWhenTheyEndBeforeTheWaitWithAfterWait() {
            TimeSpan est = TimeSpan.FromMinutes(14);
            FlatWaitFit.StopReason(Now, Now.AddHours(3), TimeSpan.FromMinutes(4), est).Should().BeNull();
            FlatWaitFit.StopReason(Now, Now.AddMinutes(18), TimeSpan.FromMinutes(4), est).Should().BeNull();
            FlatWaitFit.StopReason(Now, Now.AddMinutes(17), TimeSpan.FromMinutes(4), est).Should().Contain("after the wait ends");
            FlatWaitFit.StopReason(Now, Now.AddMinutes(20), null, est).Should().Contain("not timed yet");
            FlatWaitFit.StopReason(Now, Now.AddMinutes(24), null, est).Should().BeNull();
        }
    }
}
