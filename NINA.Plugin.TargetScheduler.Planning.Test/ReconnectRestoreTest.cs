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
    }
}
