using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class CoolerControlTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 15, 2, 0, 0, DateTimeKind.Utc);
        private static readonly CoolerSettingsData Defaults = new CoolerSettingsData();

        private static CoolerController Started(double setpoint, double ambient = 15) {
            var c = new CoolerController();
            c.NewNight("2026-10-14");
            c.Observe(Obs(T0, setpoint, 50, ambient));
            CoolerDecision start = c.Decide(T0, setpoint, Defaults, null, null);
            start.Action.Should().Be(CoolerAction.Start);
            start.Target = setpoint;
            c.Moved(start, T0);
            return c;
        }

        private static CoolerObservation Obs(DateTime at, double setpoint, double power, double ambient = 15, double? sensor = null, string paused = null) {
            return new CoolerObservation { At = at, Connected = true, Power = power, Setpoint = setpoint, Sensor = sensor ?? setpoint, Ambient = ambient, Paused = paused };
        }

        private static DateTime Feed(CoolerController c, DateTime from, TimeSpan span, double setpoint, Func<DateTime, double> power, double ambient = 15) {
            DateTime t = from;
            for (; t <= from + span; t += TimeSpan.FromSeconds(10)) {
                c.Observe(Obs(t, setpoint, power(t), ambient));
            }
            return t - TimeSpan.FromSeconds(10);
        }

        [TestCase(-9.0, -8.0)]
        [TestCase(-9.1, -10.0)]
        [TestCase(-6.9, -6.0)]
        [TestCase(-11.14, -12.0)]
        [TestCase(0.9, 0.0)]
        [TestCase(1.0, 2.0)]
        public void GridSnapsToEvenDegreesAndTiesGoWarmer(double raw, double snapped) {
            CoolerGrid.Snap(raw).Should().Be(snapped);
            CoolerGrid.OnGrid(snapped).Should().BeTrue();
        }

        [Test]
        public void GridNeverGoesAboveAmbient() {
            CoolerGrid.BelowAmbient(2, 1.5).Should().Be(0);
            CoolerGrid.BelowAmbient(-10, 1.5).Should().Be(-10);
        }

        [TestCase(20.0, -12.0)]
        [TestCase(15.0, -16.0)]
        [TestCase(25.3, -6.0)]
        public void StartPicksTheGridSetpointNearestTheTargetPowerDrop(double ambient, double expected) {
            CoolerDecision d = CoolerController.StartSetpoint(ambient, CoolerFit.Prior(), Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Start);
            d.Target.Should().Be(expected);
            d.Predicted.Should().BeLessOrEqualTo(85);
            d.Reason.Should().Contain("80% power is 31.1 °C below");
        }

        [Test]
        public void StartStaysWarmerThanALearnedHoldAbove85Percent() {
            var hot = new CoolerHold { At = T0.AddDays(-2), Ambient = 16, SetTemp = -14, PeakPower = 90 };
            CoolerDecision d = CoolerController.StartSetpoint(15, CoolerFit.Prior(), Defaults, new[] { hot }, null);
            d.Target.Should().Be(-14);
            d.Reason.Should().Contain("needed 90%");
        }

        [Test]
        public void StartStaysWarmerThanALearnedFailure() {
            var limit = new CoolerLimit { At = T0.AddDays(-1), SetTemp = -16, Ambient = 15, Reason = "stalled" };
            CoolerDecision d = CoolerController.StartSetpoint(15, CoolerFit.Prior(), Defaults, null, new[] { limit });
            d.Target.Should().Be(-14);
        }

        [Test]
        public void StartMovesWarmerWhenTheFitPredictsAboveTheLimit() {
            var fit = new CoolerFit { Intercept = -30, Slope = 3.77, Basis = "test" };
            var s = new CoolerSettingsData { TargetPower = 84, LowPower = 70, HighPower = 88, MaxPredictedPower = 85 };
            CoolerDecision d = CoolerController.StartSetpoint(15, fit, s, null, null);
            fit.Power(15, d.Target.Value).Should().BeLessOrEqualTo(85);
        }

        [Test]
        public void RefitBelowSixtyPercentNeedsTenSamples() {
            var few = Enumerable.Range(0, 5).Select(i => new CoolerSample { At = T0.AddMinutes(i), Power = 55, Setpoint = -10, Ambient = 15 });
            CoolerFit f = CoolerFit.Refit(few);
            f.Intercept.Should().Be(CoolerFit.PriorIntercept);
            f.Slope.Should().Be(CoolerFit.PriorSlope);
        }

        [Test]
        public void OneSampleAtSixtyPercentOrMoreReplacesThePrior() {
            var few = new List<CoolerSample> {
                new CoolerSample { At = T0, Power = 55, Setpoint = -10, Ambient = 15 },
                new CoolerSample { At = T0.AddMinutes(1), Power = 70, Setpoint = -16, Ambient = 15 }
            };
            CoolerFit f = CoolerFit.Refit(few);
            f.Samples.Should().Be(1);
            f.Slope.Should().Be(CoolerFit.PriorSlope);
            f.Power(15, -16).Should().BeApproximately(70, 1e-9);
            f.Margin.Should().Be(0);
            f.Basis.Should().Contain("60% cooler power or more tonight");
        }

        [Test]
        public void ACapacityPointFromDarksSetsTheEightyPercentLimitFromTonight() {
            CoolerFit.ColdestSetpoint(10, CoolerFit.Prior()).Value.Should().BeLessThan(-21);
            var night = Enumerable.Range(0, 40).Select(i => new CoolerSample { At = T0.AddMinutes(i), Power = 60 + (i % 5), Setpoint = -10 - (i % 5) * 0.5, Ambient = 10 }).ToList();
            night.Add(new CoolerSample { At = T0.AddHours(1), Power = 80, Setpoint = -16.2, Sensor = -16.2, Ambient = 10, Capacity = true });
            CoolerFit f = CoolerFit.Refit(night);
            f.Samples.Should().Be(1);
            f.Margin.Should().Be(1);
            f.Basis.Should().Contain("measured during darks tonight");
            CoolerFit.ColdestSetpoint(10, f).Value.Should().BeApproximately(-15.2, 1e-9);
            DarkPlanner.PowerLimitReason(-21, 10, CoolerFit.ColdestSetpoint(10, f)).Should().StartWith("the cooler can't hold -21 °C at 10 °C air");
            DarkPlanner.PowerLimitReason(-15, 10, CoolerFit.ColdestSetpoint(10, f)).Should().BeNull();
        }

        [Test]
        public void TheControllerKeepsCapacityPointsForTheNight() {
            var c = new CoolerController();
            c.NewNight("2026-10-16");
            c.AddCapacity(new CoolerSample { At = T0, Power = 80, Setpoint = -16.2, Sensor = -16.2, Ambient = 10 });
            CoolerFit.ColdestSetpoint(10, c.Fit()).Value.Should().BeApproximately(-15.2, 1e-9);
            c.NewNight("2026-10-17");
            c.Fit().Samples.Should().Be(0);
        }

        private static CoolerTrip Feed(CoolerGuard g, DateTime start, int seconds, Func<int, (double sensor, double power, double setpoint)> at) {
            for (int t = 0; t <= seconds; t += 2) {
                (double sensor, double power, double setpoint) = at(t);
                CoolerTrip trip = g.Observe(start.AddSeconds(t), sensor, power, setpoint);
                if (trip != null) {
                    return trip;
                }
            }
            return null;
        }

        [Test]
        public void TwoMinutesAtEightyPercentShortOfTheSetStopsTheCoolDown() {
            var g = new CoolerGuard(-21, 10);
            CoolerTrip trip = Feed(g, T0, 600, t => (t < 60 ? -14 - t / 30.0 : -16.2, t < 60 ? 70 : (t == 60 ? 80 : 100), -17));
            trip.Should().NotBeNull();
            trip.Stalled.Should().BeFalse();
            (trip.At - T0).Should().Be(TimeSpan.FromSeconds(180));
            trip.Sensor.Should().Be(-16.2);
            trip.Power.Should().Be(80);
            trip.Floor.Should().BeApproximately(-15.2, 1e-9);
            trip.Reason.Should().Be("the cooler can't hold -21 °C at 10 °C air (at 80% power it reached about -16.2 °C)");
            trip.ReasonFor(-18).Should().Be("the cooler can't hold -18 °C at 10 °C air (at 80% power it reached about -16.2 °C)");
            CoolerSample s = trip.Sample();
            s.Capacity.Should().BeTrue();
            s.Setpoint.Should().Be(-16.2);
            s.Power.Should().Be(80);
            s.Ambient.Should().Be(10);
        }

        [Test]
        public void ABriefPowerPeakOrAHoldWithinHalfADegreeDoesNotStop() {
            var g = new CoolerGuard(-21, 10);
            Feed(g, T0, 600, t => (-17, (t / 60) % 2 == 0 ? 90 : 70, -17)).Should().BeNull();
            g = new CoolerGuard(-21, 10);
            Feed(g, T0, 600, t => (-20.6, 100, -21)).Should().BeNull();
        }

        [Test]
        public void ASensorThatStopsFallingForThreeMinutesStopsTheCoolDown() {
            var g = new CoolerGuard(-21, 10);
            CoolerTrip trip = Feed(g, T0, 600, t => (t < 60 ? -14 - t / 30.0 : -16.2, 75, -21));
            trip.Should().NotBeNull();
            trip.Stalled.Should().BeTrue();
            (trip.At - T0).Should().Be(TimeSpan.FromSeconds(240));
            trip.Reason.Should().Be("the cooler can't hold -21 °C at 10 °C air (the sensor stopped cooling at about -16.2 °C at 75% power)");
            trip.Floor.Should().BeApproximately(-15.2, 1e-9);
            trip.Sample().Power.Should().Be(75);
        }

        [Test]
        public void ASensorTrackingASlowRampIsNotAStall() {
            var g = new CoolerGuard(-21, 10);
            Feed(g, T0, 600, t => (-10.2, 50, -10)).Should().BeNull();
        }

        [Test]
        public void ATripWithoutAmbientOrBelowSixtyPercentKeepsTheFitUnchanged() {
            var trip = new CoolerTrip { At = T0, SetTemp = -21, Ambient = null, Sensor = -16, Power = 85 };
            trip.Sample().Should().BeNull();
            trip.Reason.Should().Be("the cooler can't hold -21 °C (at 80% power it reached about -16 °C)");
            new CoolerTrip { At = T0, SetTemp = -21, Ambient = 10, Sensor = -16, Power = 40, Stalled = true }.Sample().Should().BeNull();
        }

        [Test]
        public void RefitMovesOnlyTheOffsetAtOneDrop() {
            var at = Enumerable.Range(0, 20).Select(i => new CoolerSample { At = T0.AddMinutes(i), Power = 90, Setpoint = -16, Ambient = 15 });
            CoolerFit f = CoolerFit.Refit(at);
            f.Slope.Should().Be(CoolerFit.PriorSlope);
            f.Power(15, -16).Should().BeApproximately(90, 1e-6);
        }

        [Test]
        public void RefitFitsBothTermsOverAWideDrop() {
            var s = new List<CoolerSample>();
            for (int i = 0; i < 40; i++) {
                double ambient = 18 - i * 0.15;
                double setpoint = i < 20 ? -12 : -14;
                s.Add(new CoolerSample { At = T0.AddMinutes(i), Setpoint = setpoint, Ambient = ambient, Power = -30 + 3.5 * (ambient - setpoint) });
            }
            CoolerFit f = CoolerFit.Refit(s);
            f.Slope.Should().BeApproximately(3.5, 1e-6);
            f.Intercept.Should().BeApproximately(-30, 1e-6);
        }

        [Test]
        public void RefitRejectsAnImplausibleSlope() {
            var s = new List<CoolerSample>();
            for (int i = 0; i < 40; i++) {
                double ambient = 18 - i * 0.1;
                s.Add(new CoolerSample { At = T0.AddMinutes(i), Setpoint = -12, Ambient = ambient, Power = 10 * (ambient + 12) - 250 });
            }
            CoolerFit.Refit(s).Slope.Should().Be(CoolerFit.PriorSlope);
        }

        [Test]
        public void FirstExposureOfTheNightStarts() {
            var c = new CoolerController();
            c.NewNight("2026-10-14");
            c.Observe(Obs(T0, -10, 50, 15));
            CoolerDecision d = c.Decide(T0, -10, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Start);
            d.Target.Should().Be(-16);
            d.From.Should().Be(-10);
            d.Moves.Should().BeTrue();
        }

        [Test]
        public void StartAtTheCurrentSetpointDoesNotMove() {
            var c = new CoolerController();
            c.NewNight("2026-10-14");
            c.Observe(Obs(T0, -16, 80, 15));
            c.Decide(T0, -16, Defaults, null, null).Moves.Should().BeFalse();
        }

        [Test]
        public void NoAmbientHolds() {
            var c = new CoolerController();
            c.NewNight("2026-10-14");
            c.Decide(T0, -16, Defaults, null, null).Action.Should().Be(CoolerAction.Hold);
        }

        [Test]
        public void HoldsUntilFiveSettledMinutes() {
            CoolerController c = Started(-16);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(6), -16, t => 95);
            CoolerDecision d = c.Decide(end, -16, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Hold);
            d.Reason.Should().Contain("settled minutes");
        }

        [Test]
        public void HighPowerStepsWarmer() {
            CoolerController c = Started(-16);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(8), -16, t => 92);
            CoolerDecision d = c.Decide(end, -16, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Warmer);
            d.Target.Should().Be(-14);
            d.Power.Should().Be(92);
            d.Reason.Should().Contain("first settled 5-minute power").And.Contain("above 88%");
        }

        [Test]
        public void ASingleSpikeDoesNotStepWarmer() {
            CoolerController c = Started(-16);
            DateTime spike = T0.AddMinutes(6);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(8), -16, t => Math.Abs((t - spike).TotalSeconds) < 30 ? 100 : 82);
            c.Decide(end, -16, Defaults, null, null).Action.Should().Be(CoolerAction.None);
        }

        [Test]
        public void StepsAreAtLeastTwentyMinutesApart() {
            CoolerController c = Started(-16);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(8), -16, t => 92);
            CoolerDecision warmer = c.Decide(end, -16, Defaults, null, null);
            c.Moved(warmer, end);

            DateTime end2 = Feed(c, end, TimeSpan.FromMinutes(10), -14, t => 90);
            CoolerDecision held = c.Decide(end2, -14, Defaults, null, null);
            held.Action.Should().Be(CoolerAction.Hold);
            held.Reason.Should().Contain("at least 20 min apart");

            DateTime end3 = Feed(c, end2 + TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(11), -14, t => 90);
            CoolerDecision next = c.Decide(end3, -14, Defaults, null, null);
            next.Action.Should().Be(CoolerAction.Warmer);
            next.Target.Should().Be(-12);
        }

        [Test]
        public void LowPowerNeedsFifteenMinutesBeforeStepColder() {
            CoolerController c = Started(-12, ambient: 20);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(12), -12, t => 60, 20);
            c.Decide(end, -12, Defaults, null, null).Action.Should().Be(CoolerAction.None);

            DateTime end2 = Feed(c, end + TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(6), -12, t => 60, 20);
            CoolerDecision d = c.Decide(end2, -12, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Colder);
            d.Target.Should().Be(-14);
            d.Predicted.Should().BeLessOrEqualTo(85);
            d.Reason.Should().Contain("below 70%");
        }

        [Test]
        public void HysteresisAFiveMinuteMedianAbove70WithinFifteenMinutesBlocksColder() {
            CoolerController c = Started(-12, ambient: 20);
            DateTime bump = T0.AddMinutes(12);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(20), -12, t => t > bump && t < bump.AddMinutes(4) ? 75 : 60, 20);
            c.Decide(end, -12, Defaults, null, null).Action.Should().Be(CoolerAction.None);
        }

        [Test]
        public void BetweenTheThresholdsNothingChanges() {
            CoolerController c = Started(-16);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(30), -16, t => 79);
            CoolerDecision d = c.Decide(end, -16, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.None);
            d.Reason.Should().Contain("within 70-88%");
        }

        [Test]
        public void NoColderStepWhenTheRefitPredictsAbove85() {
            CoolerController c = Started(-12, ambient: 20);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(20), -12, t => 69, 20);
            CoolerDecision d = c.Decide(end, -12, new CoolerSettingsData { TargetPower = 72, LowPower = 70, HighPower = 88, MaxPredictedPower = 75 }, null, null);
            d.Action.Should().Be(CoolerAction.Hold);
            d.Predicted.Should().BeApproximately(69 + 2 * CoolerFit.PriorSlope, 0.01);
            d.Reason.Should().Contain("is predicted at");
        }

        [Test]
        public void NoColderStepIntoALearnedLimit() {
            CoolerController c = Started(-12, ambient: 20);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(20), -12, t => 60, 20);
            var limit = new CoolerLimit { At = T0.AddDays(-1), SetTemp = -14, Ambient = 20, Reason = "stalled" };
            CoolerDecision d = c.Decide(end, -12, Defaults, null, new[] { limit });
            d.Action.Should().Be(CoolerAction.Hold);
            d.Reason.Should().Contain("could not hold -14");
        }

        [Test]
        public void HoldsLearnedWithTheFlapClosedDoNotBlockAColderStep() {
            CoolerController c = Started(-12, ambient: 20);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(20), -12, t => 60, 20);
            var hot = new CoolerHold { At = T0.AddDays(-1), Ambient = 20, SetTemp = -14, PeakPower = 92 };
            c.Decide(end, -12, Defaults, new[] { hot }, null).Action.Should().Be(CoolerAction.Colder);
        }

        [Test]
        public void StartIgnoresHoldsOlderThanThirtyDays() {
            var old = new CoolerHold { At = T0.AddDays(-40), Ambient = 16, SetTemp = -14, PeakPower = 90 };
            CoolerController.StartSetpoint(15, CoolerFit.Prior(), Defaults, new[] { old }, null, T0).Target.Should().Be(-16);
            CoolerController.StartSetpoint(15, CoolerFit.Prior(), Defaults, new[] { old }, null, T0.AddDays(-20)).Target.Should().Be(-14);
        }

        [Test]
        public void AFailedColderMoveStopsColderStepsForTheNight() {
            CoolerController c = Started(-12, ambient: 20);
            c.ColderFailed("stalled");
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(20), -12, t => 60, 20);
            CoolerDecision d = c.Decide(end, -12, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Hold);
            d.Reason.Should().Contain("colder steps are off tonight");

            c.NewNight("2026-10-15");
            c.ColderBlocked.Should().BeNull();
            c.Setpoint.Should().BeNull();
        }

        [Test]
        public void PausedReadingsHoldAndRestartTheSettleWindow() {
            CoolerController c = Started(-16);
            DateTime end = Feed(c, T0, TimeSpan.FromMinutes(8), -16, t => 92);
            c.Observe(Obs(end.AddSeconds(10), -16, 99, paused: "the safety monitor reports unsafe"));
            CoolerDecision paused = c.Decide(end.AddSeconds(10), -16, Defaults, null, null);
            paused.Action.Should().Be(CoolerAction.Hold);
            paused.Reason.Should().Contain("paused");

            c.Observe(Obs(end.AddSeconds(20), -16, 92));
            CoolerDecision resumed = c.Decide(end.AddSeconds(20), -16, Defaults, null, null);
            resumed.Action.Should().Be(CoolerAction.Hold);
            resumed.Reason.Should().Contain("settled minutes");
        }

        [Test]
        public void ReadingsColderThanTheSetpointDoNotCount() {
            CoolerController c = Started(-16);
            DateTime t = T0;
            for (; t <= T0.AddMinutes(10); t += TimeSpan.FromSeconds(10)) {
                c.Observe(Obs(t, -16, 40, sensor: -16.8));
            }
            c.Run.Should().BeEmpty();
            c.Decide(t, -16, Defaults, null, null).Action.Should().Be(CoolerAction.Hold);
        }

        [Test]
        public void ASensorDriftingWarmCountsAsFullPowerAndStepsWarmer() {
            CoolerController c = Started(-16);
            DateTime t = T0;
            for (; t <= T0.AddMinutes(8); t += TimeSpan.FromSeconds(10)) {
                c.Observe(Obs(t, -16, 97, sensor: -15.0));
            }
            t -= TimeSpan.FromSeconds(10);
            CoolerDecision d = c.Decide(t, -16, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Warmer);
            d.Power.Should().Be(CoolerController.SaturatedPower);
            c.NightSamples.Should().BeEmpty();
        }

        [Test]
        public void ASetpointChangedOutsideTheControllerIsChosenAgain() {
            CoolerController c = Started(-16);
            Feed(c, T0, TimeSpan.FromMinutes(8), -16, t => 80);
            c.Observe(Obs(T0.AddMinutes(9), -10, 50, 28));
            CoolerDecision d = c.Decide(T0.AddMinutes(9), -10, Defaults, null, null);
            d.Action.Should().Be(CoolerAction.Start);
            d.From.Should().Be(-10);
            d.Reason.Should().Contain("outside the controller");
        }

        [Test]
        public void TheAmbientIsATenMinuteMedian() {
            var c = new CoolerController();
            c.NewNight("2026-10-14");
            for (int i = 0; i <= 10; i++) {
                c.Observe(Obs(T0.AddMinutes(i), -16, 80, i == 10 ? 30 : 15));
            }
            c.Ambient(T0.AddMinutes(10)).Should().Be(15);
        }

        private sealed class FakeDevice : ICoolerDevice {
            public DateTime Now = T0;
            public double Sensor = -16;
            public double Setpoint = -16;
            public double Power = 90;
            public double? Stall;
            public readonly List<double> Ramps = new List<double>();
            public TimeSpan RampTook;
            public bool RefuseRamp;

            public CoolerReading Read() => new CoolerReading { At = Now, Connected = true, Sensor = Sensor, Setpoint = Setpoint, Power = Power };

            public Task<bool> Ramp(double target, CancellationToken token) {
                token.ThrowIfCancellationRequested();
                Ramps.Add(target);
                if (RefuseRamp) {
                    return Task.FromResult(false);
                }
                RampTook = DarkPlanner.Ramp(Sensor, target);
                Now += RampTook;
                Setpoint = target;
                Sensor = target + (target < Sensor ? 1.5 : -1.5);
                return Task.FromResult(true);
            }

            public Task<CoolerReading> Next(CancellationToken token) {
                token.ThrowIfCancellationRequested();
                Now += TimeSpan.FromSeconds(10);
                double floor = Stall ?? Setpoint;
                double step = Math.Sign(Setpoint - Sensor) * 0.1;
                if (Math.Abs(Setpoint - Sensor) > 1e-9 && !(Stall.HasValue && Math.Abs(Sensor - floor) < 1e-9)) {
                    Sensor = Math.Abs(Setpoint - Sensor) < 0.1 ? Setpoint : Sensor + step;
                    if (Stall.HasValue && Sensor < Stall.Value) {
                        Sensor = Stall.Value;
                    }
                }
                return Task.FromResult(Read());
            }
        }

        [Test]
        public async Task AMoveRampsAtOneDegreePerMinuteAndWaitsForHalfADegree() {
            var dev = new FakeDevice();
            CoolerMoveResult r = await CoolerMove.To(dev, -14, CancellationToken.None);
            r.Reached.Should().BeTrue();
            dev.Ramps.Should().Equal(-14);
            r.Ramp.Should().Be(TimeSpan.FromMinutes(2));
            Math.Abs(dev.Sensor - -14).Should().BeLessOrEqualTo(CoolerController.ReadyToleranceC);
            r.Took.Should().BeGreaterThan(r.Ramp);
        }

        [Test]
        public async Task AColderMoveThatStallsFails() {
            var dev = new FakeDevice { Stall = -16.8 };
            CoolerMoveResult r = await CoolerMove.To(dev, -18, CancellationToken.None);
            r.Reached.Should().BeFalse();
            r.Reason.Should().Contain("stayed");
        }

        [Test]
        public async Task ACanceledMovePropagatesTheCancel() {
            var dev = new FakeDevice();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Func<Task> act = () => CoolerMove.To(dev, -14, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
            dev.Ramps.Should().BeEmpty();
        }

        [Test]
        public async Task SettleWaitsForHalfADegreeAfterARampThatRanOn() {
            var dev = new FakeDevice { Sensor = -14.2, Setpoint = -16 };
            CoolerMoveResult r = await CoolerMove.Settle(dev, -16, dev.Now + CoolerMove.SettleLimit, CancellationToken.None);
            r.Reached.Should().BeTrue();
            dev.Sensor.Should().BeLessOrEqualTo(-15.5);
            dev.Ramps.Should().BeEmpty();
        }

        [Test]
        public async Task SettleFailsAtTheDeadline() {
            var dev = new FakeDevice { Sensor = -10, Setpoint = -16 };
            CoolerMoveResult r = await CoolerMove.Settle(dev, -16, dev.Now + TimeSpan.FromMinutes(1), CancellationToken.None);
            r.Reached.Should().BeFalse();
            r.Reason.Should().Contain("still");
        }

        [Test]
        public async Task ARampThatNinaRefusesFails() {
            var dev = new FakeDevice { RefuseRamp = true };
            CoolerMoveResult r = await CoolerMove.To(dev, -18, CancellationToken.None);
            r.Reached.Should().BeFalse();
            r.Reason.Should().Contain("could not ramp");
        }

        [Test]
        public async Task AMoveWithoutASensorReadingDoesNothing() {
            var dev = new FakeDevice { Sensor = double.NaN };
            CoolerMoveResult r = await CoolerMove.To(dev, -14, CancellationToken.None);
            r.Reached.Should().BeFalse();
            r.Reason.Should().Be(DarkPlanner.SensorUnknown);
            dev.Ramps.Should().BeEmpty();
        }

        [Test]
        public void SettingsDefaultsAreConsistent() {
            Defaults.Problem().Should().BeNull();
            Defaults.TargetPower.Should().Be(80);
            Defaults.LowPower.Should().Be(70);
            Defaults.HighPower.Should().Be(88);
            Defaults.MaxPredictedPower.Should().Be(85);
        }

        [Test]
        public void SettingsRejectAnInconsistentChangeAndPersistAGoodOne() {
            string dir = Path.Combine(Path.GetTempPath(), "cooler-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string path = Path.Combine(dir, "s.json");
                var s = new CoolerSettings(path);
                s.Update(d => d.LowPower = 85).Should().Contain("colder threshold");
                s.Snapshot().LowPower.Should().Be(70);
                s.Update(d => d.HighPower = 100).Should().Contain("below 100%");
                s.Update(d => { d.TargetPower = 75; d.LowPower = 65; }).Should().BeNull();
                new CoolerSettings(path).Snapshot().TargetPower.Should().Be(75);
                new CoolerSettings(path).Snapshot().LowPower.Should().Be(65);
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
