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
    public class ColdestSetpointTest {
        private static readonly DateTime T0 = new DateTime(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc);
        private static readonly CoolerSettingsData Defaults = new CoolerSettingsData { TargetPower = 80 };
        private static readonly CoolerFit ColdFit = new CoolerFit { Intercept = -37.4, Slope = 3.77, Basis = "test" };

        private static CoolerObservation Obs(DateTime at, double setpoint, double power, double ambient) {
            return new CoolerObservation { At = at, Connected = true, Power = power, Setpoint = setpoint, Sensor = setpoint, Ambient = ambient };
        }

        private static DarkNeed Need(double setTemp) {
            return new DarkNeed { ComboKey = "k" + setTemp, Priority = 1, Exposure = 300, Gain = 100, Offset = 50, SetTemp = setTemp, FramesNeeded = 3, SessionGapHours = 36 };
        }

        [Test]
        public void TheDefaultFloorIsMinus18() {
            new CoolerSettingsData().ColdestSetpoint.Should().Be(-18);
        }

        [Test]
        public void StartOnAColdNightIsClampedToTheFloor() {
            CoolerDecision d = CoolerController.StartSetpoint(0, ColdFit, Defaults, null, null);
            d.Target.Should().Be(-18);
            d.Reason.Should().Contain("coldest allowed setpoint");
        }

        [Test]
        public void StartOnAWarmNightIsNotChanged() {
            CoolerDecision d = CoolerController.StartSetpoint(15, ColdFit, Defaults, null, null);
            d.Target.Should().BeGreaterOrEqualTo(-18);
            d.Reason.Should().NotContain("coldest allowed");
        }

        [Test]
        public void AnOddFloorRoundsTowardWarmerOnTheGrid() {
            var s = new CoolerSettingsData { TargetPower = 80, ColdestSetpoint = -17 };
            CoolerController.StartSetpoint(0, ColdFit, s, null, null).Target.Should().Be(-16);
        }

        [Test]
        public void TheColderStepFromTheFloorHolds() {
            var c = new CoolerController();
            c.NewNight("2026-11-19");
            c.Observe(Obs(T0, -18, 50, 0));
            CoolerDecision start = c.Decide(T0, -18, Defaults, null, null);
            start.Target.Should().Be(-18);
            c.Moved(start, T0);
            DateTime t = T0;
            for (; t <= T0.AddMinutes(20); t += TimeSpan.FromSeconds(10)) {
                c.Observe(Obs(t, -18, 40, 0));
            }
            CoolerDecision d = c.Decide(t, -18, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Hold);
            d.Reason.Should().Contain("coldest allowed setpoint").And.Contain("holds at -18");
        }

        [Test]
        public void ACameraColderThanTheFloorIsRampedWarmerToTheAllowedSetpoint() {
            var c = new CoolerController();
            c.NewNight("2026-11-19");
            c.Observe(Obs(T0, -29, 80, 0));
            CoolerDecision d = c.Decide(T0, -29, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Start);
            d.Target.Should().Be(-18);
            d.From.Should().Be(-29);
            d.Reason.Should().Contain("colder than the coldest allowed").And.Contain("ramped warmer");
        }

        [Test]
        public async Task TheWarmerMoveIsSteppedAtOneDegreePerMinuteNotJumped() {
            var cam = new Fake(-29, -29);
            await new SetpointStepper(cam, cam.Delay, () => cam.Now, _ => { }).Ramp(-18, 1.0, null, CancellationToken.None);
            cam.Sets.Last().Should().Be(-18);
            cam.Sets.Count.Should().BeGreaterThan(10);
            for (int i = 1; i < cam.Sets.Count; i++) {
                Math.Abs(cam.Sets[i] - cam.Sets[i - 1]).Should().BeLessOrEqualTo(1 + 1e-9);
            }
        }

        [Test]
        public void DarkSetsColderThanTheFloorAreSkippedAndWarmerOnesKept() {
            var input = new DarkReadyInput {
                Now = T0, SunAltitudeAt = t => -30, WeatherConnected = true, Ambient = 10,
                Holds = new List<CoolerHold>(), Limits = new List<CoolerLimit>(), SaveTime = TimeSpan.FromSeconds(3),
                ColdestSetpoint = -18, Queue = new List<DarkNeed> { Need(-20), Need(-24), Need(-16) }
            };
            DarkReadyResult r = DarkReadiness.Check(input);
            r.Ready.Should().BeTrue();
            r.Need.SetTemp.Should().Be(-16);

            input.Queue = new List<DarkNeed> { Need(-20), Need(-24) };
            r = DarkReadiness.Check(input);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("colder than the coldest allowed setpoint -18");
        }

        [Test]
        public void TheFloorReasonNamesTheSetpoints() {
            DarkPlanner.FloorReason(-20, -18).Should().Contain("-20").And.Contain("-18");
            DarkPlanner.FloorReason(-18, -18).Should().BeNull();
            DarkPlanner.FloorReason(-30, null).Should().BeNull();
        }

        [TestCase(-50.0)]
        [TestCase(15.0)]
        [TestCase(double.NaN)]
        public void ProblemRejectsAFloorOutsideMinus40To10(double floor) {
            new CoolerSettingsData { ColdestSetpoint = floor }.Problem().Should().Contain("coldest setpoint");
        }

        [TestCase(-40.0)]
        [TestCase(-18.0)]
        [TestCase(10.0)]
        public void ProblemAcceptsAFloorInRange(double floor) {
            new CoolerSettingsData { ColdestSetpoint = floor }.Problem().Should().BeNull();
        }

        private sealed class Fake : ISetpointCamera {
            public DateTime Now = T0;
            public double Sensor;
            public double Setpoint;
            public List<double> Sets { get; } = new List<double>();

            public Fake(double sensor, double setpoint) {
                Sensor = sensor;
                Setpoint = setpoint;
            }

            public CoolerReading Read() {
                return new CoolerReading { At = Now, Connected = true, Sensor = Sensor, Setpoint = Setpoint, Power = 50 };
            }

            public Task<bool> Set(double setpoint, CancellationToken token) {
                Sets.Add(setpoint);
                Setpoint = setpoint;
                return Task.FromResult(true);
            }

            public Task<bool> CoolerOff() {
                return Task.FromResult(true);
            }

            public Task Delay(TimeSpan d, CancellationToken token) {
                Now += d;
                double move = d.TotalMinutes;
                Sensor = Math.Abs(Setpoint - Sensor) <= move ? Setpoint : Sensor + Math.Sign(Setpoint - Sensor) * move;
                return Task.CompletedTask;
            }
        }
    }
}
