using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class CoverMemoryTest {
        private static readonly DateTime T = new DateTime(2026, 10, 12, 23, 0, 0, DateTimeKind.Utc);

        private static DarkCover Off() {
            return new DarkCover { Configured = true, Device = "Flat Panel", Connected = false, State = DarkCoverState.Unknown };
        }

        [Test]
        public void ClosedThenPoweredOffLetsDarksRun() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.NeitherOpenNorClosed, false, T);
            m.Observe(true, DarkCoverState.Closed, false, T.AddSeconds(20));
            m.Observe(false, DarkCoverState.Unknown, false, T.AddSeconds(25));
            DarkCover c = m.Apply(Off());
            c.Remembered.Should().BeTrue();
            c.SeenAt.Should().Be(T.AddSeconds(20));
            DarkPlanner.CoverStopReason(c).Should().BeNull();
        }

        [Test]
        public void OpenThenPoweredOffRefuses() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.Closed, false, T);
            m.Observe(true, DarkCoverState.Open, false, T.AddMinutes(1));
            DarkPlanner.CoverStopReason(m.Apply(Off())).Should().Contain("last reported its cover as Open");
        }

        [Test]
        public void NeverSeenRefuses() {
            DarkPlanner.CoverStopReason(new CoverMemory().Apply(Off())).Should().Contain("has not been seen");
        }

        [Test]
        public void LightLastOnRefuses() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.Closed, true, T);
            DarkPlanner.CoverStopReason(m.Apply(Off())).Should().Contain("light on");
        }

        [Test]
        public void ClosingLeftUnfinishedRefuses() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.NeitherOpenNorClosed, false, T);
            DarkPlanner.CoverStopReason(m.Apply(Off())).Should().Contain("NeitherOpenNorClosed");
        }

        [Test]
        public void ConnectedUsesTheLiveStateAndRemembersIt() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.Closed, false, T);
            var live = new DarkCover { Configured = true, Connected = true, State = DarkCoverState.Open };
            DarkPlanner.CoverStopReason(m.Apply(live)).Should().Contain("Open, not Closed");
            DarkPlanner.CoverStopReason(m.Apply(Off())).Should().Contain("last reported its cover as Open");
        }

        [Test]
        public void NoFlatDeviceOrUnreadableIsLeftAlone() {
            var m = new CoverMemory();
            m.Observe(true, DarkCoverState.Closed, false, T);
            var none = new DarkCover { Configured = false };
            m.Apply(none).Remembered.Should().BeFalse();
            var bad = new DarkCover { Configured = true, ReadError = "driver threw" };
            DarkPlanner.CoverStopReason(m.Apply(bad)).Should().Contain("could not be read");
        }
    }
}
