using FluentAssertions;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    [NonParallelizable]
    public class EndTimeStopsTest {
        private static readonly DateTime End = new DateTime(2026, 10, 14, 2, 26, 0);
        private const int Comet = 163;

        [SetUp]
        public void SetUp() {
            EndTimeStops.Clear();
        }

        [TearDown]
        public void TearDown() {
            EndTimeStops.Clear();
        }

        [Test]
        public void NothingStoppedLetsEveryTargetContinue() {
            EndTimeStops.StoppedUntil(Comet, End.AddSeconds(-36)).Should().BeNull();
        }

        [Test]
        public void TheStoppedTargetDoesNotContinueBeforeItsEndTime() {
            EndTimeStops.Record(Comet, End);
            EndTimeStops.StoppedUntil(Comet, End.AddSeconds(-35.5)).Should().Be(End);
            EndTimeStops.StoppedUntil(Comet, End.AddSeconds(-23)).Should().Be(End);
        }

        [Test]
        public void OtherTargetsAndLaterTimesAreNotAffected() {
            EndTimeStops.Record(Comet, End);
            EndTimeStops.StoppedUntil(164, End.AddSeconds(-30)).Should().BeNull();
            EndTimeStops.StoppedUntil(Comet, End).Should().BeNull();
            EndTimeStops.StoppedUntil(Comet, End.AddHours(20)).Should().BeNull();
        }

        [Test]
        public void TheLastStopReplacesTheOneBefore() {
            EndTimeStops.Record(Comet, End);
            EndTimeStops.Record(164, End.AddHours(1));
            EndTimeStops.StoppedUntil(Comet, End.AddSeconds(-30)).Should().BeNull();
            EndTimeStops.StoppedUntil(164, End).Should().Be(End.AddHours(1));
        }
    }
}
