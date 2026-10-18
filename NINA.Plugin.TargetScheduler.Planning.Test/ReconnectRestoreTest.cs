using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class ReconnectRestoreTest {

        [Test]
        public void ACoolerThatCameBackOffIsRampedBackFromTheSensor() {
            (double? target, string why) = ReconnectRestore.Decide(true, -16, true, true, false, 4.0, 0);
            target.Should().Be(-16);
            why.Should().Contain("came back off").And.Contain("over 20 min");
        }

        [Test]
        public void ACoolerThatCameBackOnAtTheColdSetpointIsRampedInsteadOfJumping() {
            (double? target, string why) = ReconnectRestore.Decide(true, -20, true, true, true, 3.5, -20);
            target.Should().Be(-20);
            why.Should().Contain("on at -20 °C");
        }

        [TestCase(4.0, -16.0)]
        [TestCase(3.5, -20.0)]
        [TestCase(-5.0, -13.0)]
        public void TheRestoreCoolsAtAboutOneDegreePerMinuteFromTheSensor(double sensor, double last) {
            (double? target, _) = ReconnectRestore.Decide(true, last, true, true, false, sensor, double.NaN);
            TimeSpan d = DarkPlanner.Ramp(sensor, target.Value);
            double rate = Math.Abs(target.Value - sensor) / d.TotalMinutes;
            rate.Should().BeInRange(0.9, 2.0);

            var steps = CoolerRampRateTest.NinaSetpoints(sensor, target.Value, d);
            Math.Abs(steps.First().Setpoint - sensor).Should().BeLessOrEqualTo(1, "the first setpoint is next to the sensor, not the old cold setpoint");
            for (int i = 0; i + 4 < steps.Count; i++) {
                Math.Abs(steps[i + 4].Setpoint - steps[i].Setpoint).Should().BeLessOrEqualTo(2.0);
            }
            steps.Last().Setpoint.Should().Be(target.Value);
        }

        [Test]
        public void NothingIsDoneWhenThePluginDoesNotOwnTheCoolerOrTheCameraIsAlreadyThere() {
            ReconnectRestore.Decide(false, -16, true, true, false, 4, 0).Target.Should().BeNull();
            ReconnectRestore.Decide(true, null, true, true, false, 4, 0).Target.Should().BeNull();
            ReconnectRestore.Decide(true, -16, false, true, false, 4, 0).Target.Should().BeNull();
            ReconnectRestore.Decide(true, -16, true, false, false, 4, 0).Target.Should().BeNull();
            ReconnectRestore.Decide(true, -16, true, true, false, double.NaN, 0).Target.Should().BeNull();
            ReconnectRestore.Decide(true, -16, true, true, true, -15.4, -16).Target.Should().BeNull();
            ReconnectRestore.Decide(true, -16, true, true, true, -14.5, -16).Target.Should().Be(-16);
        }

        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);

        private static SetpointMemory Holding(double setpoint, int seconds = 600) {
            var m = new SetpointMemory();
            for (int t = 0; t <= seconds; t += 2) {
                m.Observe(T0.AddSeconds(t), true, true, true, setpoint);
            }
            return m;
        }

        [Test]
        public void TheFirstUpdateOfAReconnectWithTheCoolerOffKeepsTheOldSetpoint() {
            SetpointMemory m = Holding(-16);
            m.Observe(T0.AddSeconds(602), false, false, false, double.NaN).Should().BeFalse();
            DateTime back = T0.AddSeconds(660);
            m.Observe(back, true, true, false, 0).Should().BeTrue("NINA sends this update before it raises Connected");
            m.BeforeReconnect(back.AddMilliseconds(5)).Should().Be(-16);
        }

        [Test]
        public void AReconnectWithoutADisconnectedUpdateIsFoundByTheGapAndKeepsTheOldSetpoint() {
            SetpointMemory m = Holding(-20);
            DateTime back = T0.AddSeconds(700);
            m.Observe(back, true, true, true, -5).Should().BeTrue();
            m.Last.Should().Be(-5);
            m.BeforeReconnect(back).Should().Be(-20, "the cooler came back on at another setpoint, which must not replace the one to restore");
        }

        [Test]
        public void ACoolerTurnedOffOnPurposeIsNotRestored() {
            SetpointMemory m = Holding(-16);
            for (int t = 602; t <= 700; t += 2) {
                m.Observe(T0.AddSeconds(t), true, true, false, 20);
            }
            m.Last.Should().BeNull();
            m.Observe(T0.AddSeconds(702), false, false, false, double.NaN);
            m.Observe(T0.AddSeconds(760), true, true, false, 20).Should().BeTrue();
            m.BeforeReconnect(T0.AddSeconds(760)).Should().BeNull();
        }

        [Test]
        public void TheFirstConnectOfTheNightHasNothingToRestore() {
            var m = new SetpointMemory();
            m.Observe(T0, true, true, false, 0).Should().BeTrue();
            m.BeforeReconnect(T0).Should().BeNull();
        }
    }
}
