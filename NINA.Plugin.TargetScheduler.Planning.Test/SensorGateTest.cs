using FluentAssertions;
using NINA.Plugin.TargetScheduler.Sequencer;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SensorGateTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 14, 1, 7, 13);

        private static SensorReading Cooling(double sensor, double setpoint = 2, double? pending = null) {
            return new SensorReading(true, true, true, sensor, setpoint, pending);
        }

        private sealed class Clock {
            public DateTime Now = T0;

            public Task Delay(TimeSpan d, CancellationToken token) {
                token.ThrowIfCancellationRequested();
                Now += d;
                return Task.CompletedTask;
            }
        }

        private static (SensorGate gate, Clock clock, List<string> log) Gate(Func<DateTime, SensorReading> sensor) {
            var clock = new Clock();
            var gate = new SensorGate(() => sensor(clock.Now), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            return (gate, clock, new List<string>());
        }

        [TestCase(2.0, true)]
        [TestCase(2.5, true)]
        [TestCase(1.5, true)]
        [TestCase(2.51, false)]
        [TestCase(1.49, false)]
        [TestCase(8.2, false)]
        [TestCase(double.NaN, false)]
        public void AtSetpointIsHalfADegree(double sensor, bool expected) {
            SensorGate.AtSetpoint(Cooling(sensor)).Should().Be(expected);
        }

        [Test]
        public void ARampStillRunningIsJudgedAgainstWhereItEnds() {
            SensorGate.AtSetpoint(Cooling(9.9, setpoint: 9, pending: 2)).Should().BeFalse();
            SensorGate.AtSetpoint(Cooling(2.3, setpoint: 3, pending: 2)).Should().BeTrue();
        }

        [Test]
        public void ACameraThatDoesNotRegulateNeverWaits() {
            SensorGate.AtSetpoint(new SensorReading(false, true, true, 25, 2, null)).Should().BeTrue();
            SensorGate.AtSetpoint(new SensorReading(true, false, false, 25, double.NaN, null)).Should().BeTrue();
            SensorGate.AtSetpoint(new SensorReading(true, true, false, 25, 2, null)).Should().BeTrue();
        }

        [Test]
        public async Task ASensorAtTheSetpointPassesAtOnce() {
            var (gate, clock, log) = Gate(_ => Cooling(2.1));
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
            clock.Now.Should().Be(T0);
            log.Should().BeEmpty();
        }

        [Test]
        public async Task TheReconnectOfTheNightWaitsUntilTheSensorHoldsTheSetpoint() {
            var points = new (int s, double t)[] {
                (0, 8.2), (29, 10.4), (78, 10.3), (107, 9.4), (137, 8.0), (166, 6.6), (196, 5.4), (225, 4.2),
                (254, 3.2), (284, 2.1), (314, 1.6), (343, 1.3), (373, 1.5), (409, 1.7), (443, 1.8), (473, 1.8),
            };
            SensorReading At(DateTime now) {
                double sec = (now - T0).TotalSeconds;
                double t = points[0].t;
                foreach (var p in points) {
                    if (p.s <= sec) {
                        t = p.t;
                    }
                }
                return Cooling(t);
            }
            var (gate, clock, log) = Gate(At);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - T0).TotalSeconds.Should().BeGreaterThanOrEqualTo(373 + 60);
            log.Should().HaveCount(2);
            log[0].Should().Contain("sensor 8.2 °C, setpoint 2.0 °C");
        }

        [Test]
        public async Task TheRestoreRampOfTheNightIsWaitedOut() {
            var (gate, clock, log) = Gate(now => {
                double sec = (now - T0).TotalSeconds;
                double sensor = Math.Max(2, 9.9 - sec / 60.0);
                return Cooling(sensor, setpoint: Math.Max(2, Math.Round(10 - sec / 60.0)), pending: sec < 480 ? 2 : null);
            });
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - T0).TotalSeconds.Should().BeGreaterThan(7.4 * 60);
        }

        [Test]
        public async Task AReadingRightAfterAReconnectMustHoldBeforeALight() {
            DateTime connected = T0.AddSeconds(-1);
            var (_, clock, log) = Gate(_ => Cooling(2.3));
            var gate = new SensorGate(() => new SensorReading(true, true, true, 2.3, 2, null, connected), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - T0).Should().BeGreaterThanOrEqualTo(SensorGate.DefaultHold);
            log[0].Should().Contain("after the camera connected");
        }

        [Test]
        public async Task AConnectDoesNotDelayACameraThatDoesNotRegulate() {
            var clock = new Clock();
            var gate = new SensorGate(() => new SensorReading(true, true, false, 25, 2, null, T0), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            (await gate.WaitAsync(null, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
        }

        [Test]
        public async Task AnOldConnectDoesNotDelayALight() {
            var clock = new Clock();
            var gate = new SensorGate(() => new SensorReading(true, true, true, 2.1, 2, null, T0.AddMinutes(-5)), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            (await gate.WaitAsync(null, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
            clock.Now.Should().Be(T0);
        }

        [Test]
        public async Task ASensorThatNeverGetsThereTimesOut() {
            var (gate, clock, log) = Gate(_ => Cooling(6));
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.TimedOut);
            (clock.Now - T0).Should().BeGreaterThanOrEqualTo(SensorGate.DefaultTimeout);
            log[^1].Should().Contain("did not hold the setpoint");
        }

        [Test]
        public async Task ABriefDipBackOutRestartsTheHold() {
            var (gate, clock, log) = Gate(now => {
                double sec = (now - T0).TotalSeconds;
                return Cooling(sec < 10 ? 5 : sec < 40 ? 2 : sec < 50 ? 1.2 : 2);
            });
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - T0).TotalSeconds.Should().BeGreaterThanOrEqualTo(50 + 60);
        }

        [Test]
        public async Task ACancelStopsTheWait() {
            var (gate, _, log) = Gate(_ => Cooling(8));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Func<Task> wait = () => gate.WaitAsync(log.Add, cts.Token);
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
    }
}
