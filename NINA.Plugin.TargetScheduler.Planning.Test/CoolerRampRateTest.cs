using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class CoolerRampRateTest {

        /// <summary>
        /// NINA 3.x CameraVM.RegulateTemperature as it runs for CoolCamera(target, duration): a straight line from the sensor
        /// temperature at the start to the target over the duration, set every 15 s and rounded to whole degrees.
        /// </summary>
        public static List<(double T, double Setpoint)> NinaSetpoints(double sensor, double target, TimeSpan duration) {
            var steps = new List<(double, double)>();
            if (duration <= TimeSpan.Zero || Math.Abs(sensor - target) <= 1) {
                steps.Add((0, target));
                return steps;
            }
            double slope = (target - sensor) / duration.TotalSeconds;
            int checkpoints = (int)(duration.TotalSeconds / 15);
            for (int i = 1; i <= checkpoints; i++) {
                double t = i * 15;
                steps.Add((t - 15, (int)Math.Round(sensor + slope * t, MidpointRounding.ToEven)));
            }
            return steps;
        }

        /// <summary>A sensor that follows its setpoint with a 30 s lag, sampled every second.</summary>
        public static List<(double T, double Sensor)> Follow(double start, List<(double T, double Setpoint)> steps, double tau = 30, double tail = 300) {
            var r = new List<(double, double)>();
            double sensor = start;
            double end = steps.Last().T + tail;
            int k = 0;
            for (double t = 0; t <= end; t += 1) {
                while (k + 1 < steps.Count && steps[k + 1].T <= t) {
                    k++;
                }
                double sp = t >= steps[0].T ? steps[k].Setpoint : start;
                sensor += (sp - sensor) * (1 - Math.Exp(-1 / tau));
                r.Add((t, sensor));
            }
            return r;
        }

        private static double MaxRisePerMinute(List<(double T, double V)> series) {
            double max = 0;
            for (int i = 0; i < series.Count; i++) {
                int j = series.FindIndex(i, s => s.T >= series[i].T + 60);
                if (j < 0) {
                    break;
                }
                max = Math.Max(max, Math.Abs(series[j].V - series[i].V));
            }
            return max;
        }

        [TestCase(-16.0, 9.5)]
        [TestCase(-21.0, 6.8)]
        [TestCase(-25.0, 25.0)]
        [TestCase(-5.0, 12.0)]
        [TestCase(-16.0, null)]
        public void TheWarmUpRampsBetweenOneAndTwoDegreesPerMinute(double sensor, double? ambient) {
            double target = WarmupPlan.Target(ambient);
            WarmupPlan.Needed(sensor, target).Should().BeTrue();
            TimeSpan d = WarmupPlan.Duration(sensor, target);
            double rate = (target - sensor) / d.TotalMinutes;
            rate.Should().BeInRange(1.0, 2.0);

            var steps = NinaSetpoints(sensor, target, d);
            steps.First().Setpoint.Should().BeLessOrEqualTo(sensor + 1, "the first step starts at the sensor, not at the target");
            steps.Last().Setpoint.Should().BeApproximately(target, 0.5);
            MaxRisePerMinute(steps.Select(s => (s.T, s.Setpoint)).ToList()).Should().BeLessOrEqualTo(2.0);

            var followed = Follow(sensor, steps);
            MaxRisePerMinute(followed).Should().BeLessOrEqualTo(2.0);
            followed.Last().Sensor.Should().BeGreaterThan(target - WarmupPlan.NearAmbientC);
        }

        [Test]
        public void TheIncidentOn2026Oct16WasTwentyDegreesInAMinute() {
            var observed = new List<(double T, double V)> { (0, -15.8), (60, 3.9), (90, 5.3) };
            MaxRisePerMinute(observed).Should().BeGreaterThan(19);
            var steps = NinaSetpoints(-15.8, WarmupPlan.Target(9.82), WarmupPlan.Duration(-15.8, WarmupPlan.Target(9.82)));
            MaxRisePerMinute(Follow(-15.8, steps)).Should().BeLessOrEqualTo(2.0);
        }

        [Test]
        public void TheWarmUpTargetIsTheAmbientRoundedDownAndAtMostTwenty() {
            WarmupPlan.Target(9.82).Should().Be(9);
            WarmupPlan.Target(-3.4).Should().Be(-4);
            WarmupPlan.Target(27).Should().Be(WarmupPlan.MaxTargetC);
            WarmupPlan.Target(null).Should().Be(WarmupPlan.FallbackTargetC);
            WarmupPlan.Target(double.NaN).Should().Be(WarmupPlan.FallbackTargetC);
        }

        [Test]
        public void AWarmSensorNeedsNoWarmUp() {
            WarmupPlan.Needed(7.5, 9).Should().BeFalse();
            WarmupPlan.Needed(6.9, 9).Should().BeTrue();
            WarmupPlan.Needed(double.NaN, 9).Should().BeFalse();
        }

        [Test]
        public void TheWarmUpIsDoneOnlyAfterTheRampNearTheTargetOrWithTheCoolerIdle() {
            WarmupPlan.Done(false, 8.5, 2, 9).Should().BeFalse();
            WarmupPlan.Done(true, -3, 40, 9).Should().BeFalse();
            WarmupPlan.Done(true, 7.2, 40, 9).Should().BeTrue();
            WarmupPlan.Done(true, 4, 3, 20).Should().BeTrue("with the ambient unknown the target is 20 °C, and a cooler at 3% has the sensor at the air temperature");
            WarmupPlan.Done(true, 4, double.NaN, 20).Should().BeFalse();
        }

        [Test]
        public void TheDarksRampStaysAtOneDegreePerMinute() {
            DarkPlanner.Ramp(-20, -13).Should().Be(TimeSpan.FromMinutes(7));
            DarkPlanner.Ramp(-20, -13, DarkPlanner.RampCPerMinute).Should().Be(TimeSpan.FromMinutes(7));
            DarkPlanner.Ramp(-16, 9, WarmupPlan.RateCPerMinute).Should().Be(TimeSpan.FromSeconds(1005));
            DarkPlanner.Ramp(-16, 9, 0).Should().Be(DarkPlanner.MaxRamp);
        }
    }
}
