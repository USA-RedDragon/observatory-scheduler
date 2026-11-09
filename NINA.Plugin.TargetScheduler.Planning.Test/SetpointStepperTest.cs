using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SetpointStepperTest {
        private static readonly DateTime T0 = new DateTime(2026, 11, 9, 1, 30, 0, DateTimeKind.Utc);

        /// <summary>A camera on the test's clock whose sensor follows the setpoint by <see cref="Follow"/> each minute.</summary>
        private sealed class FakeCamera : ISetpointCamera {
            public DateTime Now = T0;
            public double Sensor;
            public double Setpoint;
            public double Power = 100;
            public bool CoolerOn = true;
            public double Follow = 1.0;
            public double Air = 18.1;
            public int CoolerOffCalls;
            public List<(DateTime At, double Setpoint)> Sets { get; } = new List<(DateTime, double)>();
            public List<string> Log { get; } = new List<string>();

            public FakeCamera(double sensor, double setpoint) {
                Sensor = sensor;
                Setpoint = setpoint;
            }

            public CoolerReading Read() {
                return new CoolerReading { At = Now, Connected = true, Sensor = Sensor, Setpoint = Setpoint, Power = Power };
            }

            public Task<bool> Set(double setpoint, CancellationToken token) {
                Sets.Add((Now, setpoint));
                Setpoint = setpoint;
                CoolerOn = true;
                return Task.FromResult(true);
            }

            public Task<bool> CoolerOff() {
                CoolerOffCalls++;
                CoolerOn = false;
                return Task.FromResult(true);
            }

            public Task Delay(TimeSpan d, CancellationToken token) {
                token.ThrowIfCancellationRequested();
                Now += d;
                double goal = Math.Min(Setpoint, Air);
                double move = Follow * d.TotalMinutes;
                Sensor = Math.Abs(goal - Sensor) <= move ? goal : Sensor + Math.Sign(goal - Sensor) * move;
                Power = Math.Max(0, Math.Min(100, -36.7 + 3.82 * (Air - Sensor)));
                return Task.CompletedTask;
            }

            public SetpointStepper Stepper(Func<TimeSpan, CancellationToken, Task> delay = null) {
                return new SetpointStepper(this, delay ?? Delay, () => Now, Log.Add);
            }
        }

        private static void AtMostOneDegreePerMinute(List<(DateTime At, double Setpoint)> sets) {
            for (int i = 1; i < sets.Count; i++) {
                double change = Math.Abs(sets[i].Setpoint - sets[i - 1].Setpoint);
                change.Should().BeLessOrEqualTo(SetpointStepper.StepC + 1e-9, $"step {i} goes from {sets[i - 1].Setpoint} to {sets[i].Setpoint}");
                (sets[i].At - sets[i - 1].At).Should().BeGreaterOrEqualTo(SetpointStepper.MinInterval, $"step {i} comes {(sets[i].At - sets[i - 1].At).TotalSeconds} s after the one before");
            }
            for (int i = 0; i < sets.Count; i++) {
                for (int j = i + 1; j < sets.Count; j++) {
                    double minutes = (sets[j].At - sets[i].At).TotalMinutes;
                    Math.Abs(sets[j].Setpoint - sets[i].Setpoint).Should().BeLessOrEqualTo(Math.Max(1, minutes) + 1e-9);
                }
            }
        }

        [Test]
        public async Task TheNov8WarmUpHoldsTheSensorThenRisesAtMostOneDegreePerMinuteAndTurnsTheCoolerOff() {
            var cam = new FakeCamera(-14.0, -15);
            double target = WarmupPlan.Target(18.1);
            target.Should().Be(18);

            bool ok = await cam.Stepper().WarmAndStop(target, () => null, CancellationToken.None);

            ok.Should().BeTrue();
            cam.Sets.First().Setpoint.Should().Be(-14, "the first step holds the sensor where it is, not the target");
            cam.Sets.First().At.Should().Be(T0);
            cam.Sets.Last().Setpoint.Should().Be(18);
            AtMostOneDegreePerMinute(cam.Sets);
            cam.Sets.Should().HaveCount(33, "the hold at -14 °C and 32 steps of 1 °C to 18 °C");
            (cam.Sets.Last().At - T0).Should().Be(TimeSpan.FromMinutes(32));
            cam.CoolerOffCalls.Should().Be(1);
            cam.CoolerOn.Should().BeFalse();
            cam.Log.Should().Contain(l => l.StartsWith("holding the sensor at -14.0 °C"));
            cam.Log.Should().Contain(l => l.StartsWith("setpoint -14.0 -> -13.0 °C"));
            cam.Log.Last().Should().StartWith("cooler off at 18.0 °C");
        }

        [TestCase(-14.3, -15, -14)]
        [TestCase(-14.6, -15, -15)]
        [TestCase(-20.2, -20, -20)]
        public async Task TheFirstSetHoldsTheSensorTemperature(double sensor, double setpoint, double hold) {
            var cam = new FakeCamera(sensor, setpoint);
            await cam.Stepper().Ramp(10, 1.0, null, CancellationToken.None);
            cam.Sets.First().Setpoint.Should().Be(hold);
        }

        [Test]
        public async Task ASlowerRateStepsLessOften() {
            var cam = new FakeCamera(-10, -10);
            await cam.Stepper().Ramp(-5, 0.5, null, CancellationToken.None);
            for (int i = 1; i < cam.Sets.Count; i++) {
                (cam.Sets[i].At - cam.Sets[i - 1].At).Should().Be(TimeSpan.FromMinutes(2));
            }
            SetpointStepper.Interval(5).Should().Be(SetpointStepper.MinInterval, "a faster rate than 1 °C per minute is never used");
            SetpointStepper.Interval(0).Should().Be(SetpointStepper.MinInterval);
        }

        [Test]
        public async Task ACoolDownStepsDownTheSameWay() {
            var cam = new FakeCamera(12.4, 12);
            (await cam.Stepper().Ramp(-15, 1.0, null, CancellationToken.None)).Should().BeTrue();
            cam.Sets.First().Setpoint.Should().Be(12);
            cam.Sets.Last().Setpoint.Should().Be(-15);
            AtMostOneDegreePerMinute(cam.Sets);
        }

        [Test]
        public async Task ACancelStopsAtTheLastStepWithTheCoolerOn() {
            var cam = new FakeCamera(-14, -15);
            using var cts = new CancellationTokenSource();
            int delays = 0;
            Task Delay(TimeSpan d, CancellationToken t) {
                if (++delays == 6) {
                    cts.Cancel();
                }
                return cam.Delay(d, t);
            }

            Func<Task> run = () => cam.Stepper(Delay).WarmAndStop(18, () => null, cts.Token);

            await run.Should().ThrowAsync<OperationCanceledException>();
            cam.Sets.Last().Setpoint.Should().Be(-9, "the hold and five steps were set before the cancel");
            cam.Setpoint.Should().Be(-9);
            cam.CoolerOffCalls.Should().Be(0);
            cam.CoolerOn.Should().BeTrue();
            AtMostOneDegreePerMinute(cam.Sets);
        }

        [Test]
        public async Task SafeAgainStopsTheWarmUpAndLeavesTheCoolerHolding() {
            var cam = new FakeCamera(-14, -15);
            string stop = null;
            int delays = 0;
            Task Delay(TimeSpan d, CancellationToken t) {
                if (++delays == 4) {
                    stop = "it is safe again";
                }
                return cam.Delay(d, t);
            }

            bool ok = await cam.Stepper(Delay).WarmAndStop(18, () => stop, CancellationToken.None);

            ok.Should().BeFalse();
            cam.Sets.Last().Setpoint.Should().Be(-11);
            cam.CoolerOffCalls.Should().Be(0);
            cam.Log.Last().Should().Be("stopping at -11.0 °C, which the cooler holds: it is safe again");
        }

        [Test]
        public async Task ALaggingSensorHoldsTheStepUntilItFollows() {
            var cam = new FakeCamera(-14, -15) { Follow = 0.4 };
            await cam.Stepper().Ramp(-4, 1.0, null, CancellationToken.None);
            AtMostOneDegreePerMinute(cam.Sets);
            cam.Log.Should().Contain(l => l.StartsWith("holding -"));
            (cam.Sets.Last().At - T0).Should().BeGreaterThan(TimeSpan.FromMinutes(10), "steps wait for the sensor, so 10 °C takes longer than 10 min");
        }

        [Test]
        public async Task ASensorThatNeverFollowsEndsTheRampWithoutTurningTheCoolerOff() {
            var cam = new FakeCamera(-14, -15) { Follow = 0 };
            bool ok = await cam.Stepper().WarmAndStop(18, () => null, CancellationToken.None);
            ok.Should().BeFalse();
            cam.CoolerOffCalls.Should().Be(0);
            cam.Sets.Last().Setpoint.Should().BeLessOrEqualTo(-12);
            cam.Log.Last().Should().Contain("did not come within 1 °C");
        }

        [Test]
        public async Task AnIdleCoolerBelowTheSetpointMeansTheSensorIsAtTheAir() {
            var cam = new FakeCamera(-14, -15) { Air = 5 };
            bool ok = await cam.Stepper().WarmAndStop(18, () => null, CancellationToken.None);
            ok.Should().BeTrue();
            cam.CoolerOffCalls.Should().Be(1);
            cam.Sets.Last().Setpoint.Should().BeLessOrEqualTo(7);
            AtMostOneDegreePerMinute(cam.Sets);
        }

        [Test]
        public void TheDurationCountsTheHoldAndEveryStep() {
            SetpointStepper.Duration(-14, 18, 1.0).Should().Be(TimeSpan.FromMinutes(33));
            SetpointStepper.Duration(-14.3, -14, 1.0).Should().Be(TimeSpan.FromMinutes(1));
        }
    }
}
