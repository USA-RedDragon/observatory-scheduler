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
        [TestCase(2.49, true)]
        [TestCase(1.51, true)]
        [TestCase(2.5, false)]
        [TestCase(1.5, false)]
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
        public async Task ASensorThatHasHeldTheSetpointPassesAtOnce() {
            var clock = new Clock();
            var hold = Seeded(Cooling(2.1), TimeSpan.FromMinutes(5));
            var log = new List<string>();
            var gate = new SensorGate(() => Cooling(2.1), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, hold);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
            clock.Now.Should().Be(T0);
            log.Should().BeEmpty();
            gate.Status(Cooling(2.1)).Should().StartWith("held since 01:02:13 (300 s)");
        }

        [Test]
        public async Task AGateWithNoHistoryWaitsTheHoldTime() {
            var (gate, clock, log) = Gate(_ => Cooling(2.1));
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - T0).Should().Be(SensorGate.DefaultHold);
            log[0].Should().Contain("on the first reading");
        }

        private static SensorHold Seeded(SensorReading r, TimeSpan before) {
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            for (DateTime at = T0 - before; at < T0; at += TimeSpan.FromSeconds(2)) {
                hold.Observe(at, r);
            }
            return hold;
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
            var hold = Seeded(new SensorReading(true, true, true, 2.3, 2, null, T0.AddHours(-2)), TimeSpan.FromMinutes(10));
            hold.Observe(connected, new SensorReading(true, true, true, 2.3, 2, null, connected));
            var clock = new Clock();
            var log = new List<string>();
            var gate = new SensorGate(() => new SensorReading(true, true, true, 2.3, 2, null, connected), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, hold);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - connected).Should().BeGreaterThanOrEqualTo(SensorGate.DefaultHold);
            log[0].Should().Contain("the camera connected at 01:07:12");
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
            var reading = new SensorReading(true, true, true, 2.1, 2, null, T0.AddMinutes(-5));
            var gate = new SensorGate(() => reading, clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, Seeded(reading, TimeSpan.FromMinutes(4)));
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

        [TestCase(-3.50, false)]
        [TestCase(-4.50, false)]
        [TestCase(-3.51, true)]
        [TestCase(-4.49, true)]
        public void TheBandIsStrictlyUnderHalfADegree(double sensor, bool expected) {
            SensorGate.AtSetpoint(Cooling(sensor, setpoint: -4)).Should().Be(expected);
        }

        private static readonly DateTime Night = new DateTime(2026, 10, 14, 20, 46, 18, 130);

        private static Func<DateTime, SensorReading> AfterTheMoveOfTheNight(DateTime origin) {
            var points = new (double s, double t)[] { (0, -3.50), (12, -3.55), (25, -3.62), (40, -3.71), (70, -3.83), (100, -3.92), (140, -3.98) };
            return now => {
                double sec = (now - origin).TotalSeconds;
                double t = points[0].t;
                foreach (var p in points) {
                    if (p.s <= sec) {
                        t = p.t;
                    }
                }
                return Cooling(t, setpoint: -4);
            };
        }

        [Test]
        public async Task TheLightAfterTheCoolerMoveOfTheNightWaitsForAFullHold() {
            var clock = new Clock { Now = Night };
            var sensor = AfterTheMoveOfTheNight(Night);
            var log = new List<string>();
            var gate = new SensorGate(() => sensor(clock.Now), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            (clock.Now - Night).TotalSeconds.Should().BeGreaterThanOrEqualTo(12 + 60);
            log.Should().HaveCount(2);
            log[0].Should().Contain("waiting from 20:46:18").And.Contain("-4.0 °C");
            log[1].Should().StartWith("released at ");
        }

        [Test]
        public async Task ASecondLookRightAfterAWaitPassesAtOnce() {
            var clock = new Clock { Now = Night };
            var sensor = AfterTheMoveOfTheNight(Night);
            var gate = new SensorGate(() => sensor(clock.Now), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout);
            (await gate.WaitAsync(null, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            DateTime released = clock.Now;
            var log = new List<string>();
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
            clock.Now.Should().Be(released);
            log.Should().BeEmpty();
        }

        private static SensorHold FedWithTheRampOfTheNight(out DateTime rampEnd) {
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            DateTime start = new DateTime(2026, 10, 14, 20, 30, 0);
            rampEnd = new DateTime(2026, 10, 14, 20, 46, 15);
            DateTime rampStart = new DateTime(2026, 10, 14, 20, 38, 14);
            for (DateTime at = start; at < Night; at += TimeSpan.FromSeconds(2)) {
                double sec = (at - rampStart).TotalSeconds;
                if (at < rampStart) {
                    hold.Observe(at, Cooling(4.0, setpoint: 4));
                } else if (at < rampEnd) {
                    double step = Math.Max(-4, Math.Round(4 - sec / 60.0));
                    hold.Observe(at, Cooling(Math.Max(-3.4, 4.2 - sec / 60.0), setpoint: step, pending: -4));
                } else {
                    hold.Observe(at, Cooling(at < Night.AddSeconds(-1.1) ? -3.40 : -3.50, setpoint: -4));
                }
            }
            return hold;
        }

        [Test]
        public async Task TheRampOfTheNightThenAPlanOneSecondLaterWaitsForAFullHoldAfterTheRamp() {
            var hold = FedWithTheRampOfTheNight(out DateTime rampEnd);
            hold.InSince.Should().BeNull();
            var clock = new Clock { Now = Night };
            var sensor = AfterTheMoveOfTheNight(Night);
            var log = new List<string>();
            var gate = new SensorGate(() => sensor(clock.Now), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, hold);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            log.Should().HaveCount(2);
            log[0].Should().StartWith("waiting from 20:46:18 for the sensor to hold within 0.5 °C of -4.0 °C for 60 s");
            log[0].Should().Contain("the hold restarted at 20:46:16 on the ramp to -4.0 °C ended");
            log[1].Should().StartWith("released at 20:47:33 after 75 s: the sensor held within 0.5 °C of -4.0 °C since 20:46:33 (60 s)");
            hold.InSince.Should().Be(Night.AddSeconds(15));
        }

        [Test]
        public void InTheBandDuringARampDoesNotCountUntilTheRampEnds() {
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            for (int s = 0; s <= 120; s += 2) {
                hold.Observe(T0.AddSeconds(s), Cooling(-3.9, setpoint: -3, pending: -4));
            }
            hold.InSince.Should().BeNull();
            hold.Observe(T0.AddSeconds(122), Cooling(-3.9, setpoint: -4));
            hold.InSince.Should().Be(T0.AddSeconds(122));
            hold.LastReset.Why.Should().Be("the ramp to -4.0 °C ended");
            hold.Held(T0.AddSeconds(181), SensorGate.DefaultHold).Should().BeFalse();
        }

        [Test]
        public void ASetpointChangeWithoutARampRestartsTheHold() {
            var hold = Seeded(Cooling(-4.0, setpoint: -4), TimeSpan.FromMinutes(10));
            hold.Held(T0, SensorGate.DefaultHold).Should().BeTrue();
            hold.Observe(T0.AddSeconds(2), Cooling(-4.0, setpoint: -6));
            hold.Held(T0.AddSeconds(2), SensorGate.DefaultHold).Should().BeFalse();
            hold.LastReset.Why.Should().Be("the setpoint changed from -4.0 °C to -6.0 °C");
            hold.Observe(T0.AddSeconds(4), Cooling(-5.6, setpoint: -6));
            hold.InSince.Should().Be(T0.AddSeconds(4));
        }

        [Test]
        public void TheExactBoundaryNeverStartsTheHold() {
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            for (int s = 0; s <= 600; s += 2) {
                hold.Observe(T0.AddSeconds(s), Cooling(-3.5, setpoint: -4));
            }
            hold.InSince.Should().BeNull();
            hold.Held(T0.AddSeconds(600), SensorGate.DefaultHold).Should().BeFalse();
        }

        [Test]
        public async Task AnExcursionDuringAnExposureMakesTheNextLightWait() {
            var hold = Seeded(Cooling(2.0), TimeSpan.FromMinutes(10));
            hold.Observe(T0.AddSeconds(2), Cooling(2.7));
            hold.Observe(T0.AddSeconds(4), Cooling(2.1));
            var clock = new Clock { Now = T0.AddSeconds(30) };
            var log = new List<string>();
            var gate = new SensorGate(() => Cooling(2.1), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, hold);
            (await gate.WaitAsync(log.Add, CancellationToken.None)).Should().Be(SensorGateOutcome.Settled);
            clock.Now.Should().Be(T0.AddSeconds(65));
            log[0].Should().Contain("the sensor left the band at 2.7 °C").And.Contain("in the band since 01:07:17 (26 s)");
        }

        [Test]
        public void AGapInTheReadingsRestartsTheHold() {
            var hold = Seeded(Cooling(2.0), TimeSpan.FromMinutes(10));
            hold.Observe(T0.AddSeconds(45), Cooling(2.0));
            hold.InSince.Should().Be(T0.AddSeconds(45));
            hold.LastReset.Why.Should().Be("no reading for 47 s");
        }

        [Test]
        public void TheCoolerGoingOnRestartsTheHold() {
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            hold.Observe(T0, new SensorReading(true, true, false, 2.0, 2, null));
            hold.Observe(T0.AddSeconds(2), Cooling(2.0));
            hold.LastReset.Why.Should().Be("the cooler started regulating");
            hold.InSince.Should().Be(T0.AddSeconds(2));
        }

        [Test]
        public async Task ACameraWithoutRegulationNeverWaitsEvenWithNoHistory() {
            var clock = new Clock();
            var gate = new SensorGate(() => new SensorReading(true, false, false, 25, double.NaN, null, T0), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, SensorGate.DefaultTimeout, new SensorHold(SensorHold.DefaultMaxGap));
            (await gate.WaitAsync(null, CancellationToken.None)).Should().Be(SensorGateOutcome.Ready);
            clock.Now.Should().Be(T0);
        }

        [Test]
        public async Task ARampAcrossTheFallBackHourStillMakesTheLightWait() {
            DateTime cdt0030 = new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc);
            DateTime cst0110 = new DateTime(2026, 11, 1, 7, 10, 0, DateTimeKind.Utc);
            var hold = new SensorHold(SensorHold.DefaultMaxGap);
            for (DateTime at = cdt0030; at < cst0110; at += TimeSpan.FromSeconds(2)) {
                hold.Observe(at, Cooling(4.0, setpoint: 4));
            }
            for (DateTime at = cst0110; at < cst0110.AddMinutes(10); at += TimeSpan.FromSeconds(2)) {
                hold.Observe(at, Cooling(Math.Max(-3.4, 4 - (at - cst0110).TotalSeconds / 60.0), setpoint: 3, pending: -4));
            }
            var clock = new Clock { Now = cst0110.AddMinutes(10) };
            var gate = new SensorGate(() => Cooling(-3.4, setpoint: -4, pending: -4), clock.Delay, () => clock.Now,
                SensorGate.DefaultHold, SensorGate.DefaultPoll, TimeSpan.FromMinutes(1), hold);
            (await gate.WaitAsync(null, CancellationToken.None)).Should().Be(SensorGateOutcome.TimedOut);
        }

        [Test]
        public void LogTimesOfAUtcClockAreLocal() {
            DateTime utc = new DateTime(2026, 10, 15, 1, 46, 18, DateTimeKind.Utc);
            SensorGate.Clock(utc).Should().Be(utc.ToLocalTime().ToString("HH:mm:ss"));
            SensorGate.Clock(Night).Should().Be("20:46:18");
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
