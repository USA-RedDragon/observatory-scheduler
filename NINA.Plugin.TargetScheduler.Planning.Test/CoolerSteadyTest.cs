using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class CoolerSteadyTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 4, 9, 0, DateTimeKind.Utc);
        private const double Limit = DarkPlanner.MaxDarkPowerPercent;

        /// <summary>
        /// A simulated cooler after a move to <paramref name="setTemp"/>: the sensor closes the gap from
        /// <paramref name="startSensor"/> with a time constant, and the power falls from a pull-down peak to its
        /// steady value with another one. Readings come every 2 s, as NINA polls the camera.
        /// </summary>
        private static List<(double T, double Sensor, double Power)> Simulate(double setTemp, double startSensor, double steadyPower, double peakExtra, double sensorTau, double powerTau, int seconds) {
            var r = new List<(double, double, double)>();
            for (int t = 0; t <= seconds; t += 2) {
                double sensor = setTemp + (startSensor - setTemp) * Math.Exp(-t / sensorTau);
                double power = Math.Min(100, steadyPower + peakExtra * Math.Exp(-t / powerTau));
                r.Add((t, Math.Round(sensor, 1), Math.Round(power)));
            }
            return r;
        }

        private static (CoolerSteadyVerdict First, double At, List<CoolerSteadyState> States) Run(CoolerSteady s, double setTemp, IEnumerable<(double T, double Sensor, double Power)> readings) {
            var states = new List<CoolerSteadyState>();
            foreach (var (t, sensor, power) in readings) {
                DateTime at = T0.AddSeconds(t);
                s.Observe(at, true, sensor, setTemp, power);
                CoolerSteadyVerdict v = s.Verdict(at, setTemp, Limit);
                states.Add(v.State);
                if (v.State != CoolerSteadyState.Waiting) {
                    return (v, t, states);
                }
            }
            return (null, double.NaN, states);
        }

        [Test]
        public void APullDownPeakAbove80PercentThatSettlesBelowItPasses() {
            var readings = Simulate(-14, -15.4, 78, 16, 60, 90, 900);
            readings[0].Power.Should().Be(94);
            DarkPlanner.PowerReadingReason(-14, 9.5, readings[0].Power).Should().NotBeNull("5.8.2.233 judged this first reading and skipped the set");

            var (v, at, states) = Run(new CoolerSteady(), -14, readings);

            v.Should().NotBeNull();
            v.State.Should().Be(CoolerSteadyState.Within);
            v.Power.Should().BeInRange(78, 80);
            at.Should().BeGreaterOrEqualTo(CoolerSteady.StableFor.TotalSeconds);
            states.Should().NotContain(CoolerSteadyState.Over);
            v.Reason.Should().Contain("steady").And.Contain("-14 °C");
        }

        [Test]
        public void APowerThatSettlesAbove80PercentFailsOnlyOnceSettled() {
            var readings = Simulate(-15, -16.3, 84, 15, 60, 90, 900);
            var (v, at, _) = Run(new CoolerSteady(), -15, readings);

            v.State.Should().Be(CoolerSteadyState.Over);
            v.Power.Should().BeInRange(84, 87);
            double sensorIn = readings.First(r => Math.Abs(r.Sensor + 15) <= CoolerSteady.BandC + 1e-9).T;
            at.Should().BeGreaterOrEqualTo(sensorIn + CoolerSteady.StableFor.TotalSeconds);
            DarkPlanner.PowerReadingReason(-15, 9.5, v.Power.Value).Should().Contain("it needed a steady");
        }

        [Test]
        public void APowerStillFallingAbove80PercentIsWaitedForUntilItIsBelow() {
            var readings = Simulate(-14, -15, 76, 30, 40, 100, 1200);
            double sensorIn = readings.First(r => Math.Abs(r.Sensor + 14) <= CoolerSteady.BandC + 1e-9).T;
            var (v, at, states) = Run(new CoolerSteady(), -14, readings);

            states.Should().NotContain(CoolerSteadyState.Over);
            v.State.Should().Be(CoolerSteadyState.Within);
            v.Power.Should().BeLessOrEqualTo(Limit);
            at.Should().BeGreaterThan(sensorIn + CoolerSteady.StableFor.TotalSeconds);
        }

        [Test]
        public void APowerThatNeverStopsFallingAbove80PercentFailsAfterTenMinutesInTheBand() {
            var s = new CoolerSteady();
            CoolerSteadyVerdict v = null;
            double t = 0;
            for (; t <= 1200; t += 2) {
                s.Observe(T0.AddSeconds(t), true, -14, -14, 100 - t / 60.0 * 2.1);
                v = s.Verdict(T0.AddSeconds(t), -14, Limit);
                if (v.State != CoolerSteadyState.Waiting) {
                    break;
                }
            }
            v.State.Should().Be(CoolerSteadyState.Over);
            t.Should().Be(CoolerSteady.MaxWait.TotalSeconds);
        }

        [Test]
        public void TheRealPullDownTo15On2026Oct16IsNotJudged() {
            var telemetry = new (double Sensor, double Power)[] {
                (-16.3, 99), (-16.2, 98), (-16.0, 95), (-15.7, 93), (-15.4, 90), (-15.1, 89)
            };
            var s = new CoolerSteady();
            for (int i = 0; i < telemetry.Length; i++) {
                DateTime at = T0.AddSeconds(30 * i);
                s.Observe(at, true, telemetry[i].Sensor, -15, telemetry[i].Power);
                s.Verdict(at, -15, Limit).State.Should().Be(CoolerSteadyState.Waiting);
            }
        }

        [Test]
        public void TheRealHoldAt13On2026Oct16PassesAtItsSteadyPower() {
            var telemetry = new (double Sensor, double Power)[] {
                (-13.6, 80), (-13.2, 78), (-13.2, 77), (-13.1, 77), (-13.1, 77), (-13.1, 76), (-13.1, 76), (-13.0, 76), (-13.0, 76), (-13.0, 76), (-13.0, 76)
            };
            var s = new CoolerSteady();
            CoolerSteadyVerdict v = null;
            for (int i = 0; i < telemetry.Length; i++) {
                DateTime at = T0.AddSeconds(30 * i);
                s.Observe(at, true, telemetry[i].Sensor, -13, telemetry[i].Power);
                v = s.Verdict(at, -13, Limit);
            }
            v.State.Should().Be(CoolerSteadyState.Within);
            v.Power.Should().Be(76);
        }

        [Test]
        public void LeavingTheBandOrAGapOrASetpointChangeRestartsTheRun() {
            var s = new CoolerSteady();
            for (int t = 0; t <= 170; t += 2) {
                s.Observe(T0.AddSeconds(t), true, -14.1, -14, 70);
            }
            s.Observe(T0.AddSeconds(172), true, -13.6, -14, 70);
            s.InSince.Should().BeNull();
            for (int t = 174; t <= 400; t += 2) {
                s.Observe(T0.AddSeconds(t), true, -14.0, -14, 70);
            }
            s.InSince.Should().Be(T0.AddSeconds(174));
            s.Verdict(T0.AddSeconds(400), -14, Limit).State.Should().Be(CoolerSteadyState.Within);

            s.Observe(T0.AddSeconds(440), true, -14.0, -14, 70);
            s.InSince.Should().Be(T0.AddSeconds(440));

            s.Observe(T0.AddSeconds(442), true, -14.0, -13, 70);
            s.Setpoint.Should().Be(-13);
            s.Verdict(T0.AddSeconds(442), -14, Limit).State.Should().Be(CoolerSteadyState.Waiting);
            s.Verdict(T0.AddSeconds(442), -13, Limit).Reason.Should().Contain("not within 0.3 °C");
        }

        [Test]
        public void ADisconnectRestartsTheRun() {
            var s = new CoolerSteady();
            for (int t = 0; t <= 300; t += 2) {
                s.Observe(T0.AddSeconds(t), true, -14, -14, 70);
            }
            s.Observe(T0.AddSeconds(302), false, double.NaN, double.NaN, double.NaN);
            s.Setpoint.Should().BeNull();
            s.Verdict(T0.AddSeconds(302), -14, Limit).State.Should().Be(CoolerSteadyState.Waiting);
        }

        [Test]
        public void ACameraWithoutPowerPassesOnceTheSensorHolds() {
            var s = new CoolerSteady();
            CoolerSteadyVerdict v = null;
            for (int t = 0; t <= 200; t += 2) {
                s.Observe(T0.AddSeconds(t), true, -10, -10, double.NaN);
                v = s.Verdict(T0.AddSeconds(t), -10, Limit);
            }
            v.State.Should().Be(CoolerSteadyState.Within);
            v.Power.Should().BeNull();
        }

        [Test]
        public void TheGuardWaitsOutAPullDownAtFullPowerWhileTheSensorStillFalls() {
            var g = new CoolerGuard(-16, 9.5);
            CoolerTrip trip = null;
            for (int t = 0; t <= 600 && trip == null; t += 2) {
                double sensor = Math.Max(-16, -10 - t / 60.0);
                trip = g.Observe(T0.AddSeconds(t), sensor, 100, Math.Max(-16, -10 - t / 60.0));
            }
            trip.Should().BeNull();
        }

        [Test]
        public void TheGuardStillStopsACoolerPinnedAt100PercentThatStopsCooling() {
            var g = new CoolerGuard(-21, 9.5);
            CoolerTrip trip = null;
            int at = -1;
            for (int t = 0; t <= 900 && trip == null; t += 2) {
                double sensor = Math.Max(-16.3, -13 - t / 60.0);
                trip = g.Observe(T0.AddSeconds(t), sensor, 100, -21);
                at = t;
            }
            trip.Should().NotBeNull();
            trip.Stalled.Should().BeFalse();
            trip.Sensor.Should().BeApproximately(-16.3, CoolerGuard.FallC + 0.01);
            double stopped = (16.3 - 13) * 60;
            at.Should().BeInRange((int)(stopped + CoolerGuard.HighFor.TotalSeconds) - 10, (int)(stopped + CoolerGuard.HighFor.TotalSeconds) + 10);
        }
    }
}
