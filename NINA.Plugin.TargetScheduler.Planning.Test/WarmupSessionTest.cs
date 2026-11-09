using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class WarmupSessionTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 17, 3, 44, 0, DateTimeKind.Utc);
        private string home;
        private DateTime now;

        /// <summary>
        /// A camera whose setpoint follows NINA's stepped ramp from the sensor to the target and whose sensor lags the
        /// setpoint by 30 s, on the test's clock.
        /// </summary>
        private sealed class SimCamera : IWarmupCamera {
            private readonly Func<DateTime> clock;
            private DateTime last;
            private DateTime rampStart;
            private double rampFrom;
            private double rampTo;
            private TimeSpan rampFor;
            private TaskCompletionSource<bool> ramp;

            public SimCamera(Func<DateTime> clock, double sensor, double power = 99) {
                this.clock = clock;
                last = clock();
                Sensor = sensor;
                Setpoint = sensor;
                Power = power;
            }

            public const double Air = 9.8;

            public bool Connected { get; set; } = true;
            public bool CanSet { get; set; } = true;
            public double Sensor { get; private set; }
            public double Setpoint { get; private set; }
            public double Power { get; set; }
            public bool? RampResult { get; set; } = true;
            public List<(double Target, double Rate)> Ramps { get; } = new List<(double, double)>();
            public int Stops { get; private set; }
            public List<(DateTime At, double Sensor)> History { get; } = new List<(DateTime, double)>();

            public void Advance() {
                DateTime t = clock();
                while (last < t) {
                    last = last.AddSeconds(1);
                    if (ramp != null) {
                        double f = Math.Min(1, (last - rampStart).TotalSeconds / rampFor.TotalSeconds);
                        Setpoint = Math.Round(rampFrom + (rampTo - rampFrom) * f);
                        if (f >= 1 && !ramp.Task.IsCompleted && RampResult.HasValue) {
                            ramp.SetResult(RampResult.Value);
                        }
                    }
                    Sensor += (Math.Min(Setpoint, Air) - Sensor) * (1 - Math.Exp(-1 / 30.0));
                    Power = Math.Max(0, Math.Min(100, 3.77 * (Air - Setpoint) - 37.4));
                    History.Add((last, Sensor));
                }
            }

            public WarmupReading Read() {
                Advance();
                return new WarmupReading { Connected = Connected, CanSetTemperature = CanSet, CoolerOn = true, Sensor = Sensor, Setpoint = Setpoint, Power = Power };
            }

            public Task<bool> StartRamp(double target, double cPerMinute) {
                Ramps.Add((target, cPerMinute));
                rampStart = clock();
                rampFrom = Sensor;
                rampTo = target;
                rampFor = DarkPlanner.Ramp(Sensor, target, cPerMinute);
                ramp = new TaskCompletionSource<bool>();
                return ramp.Task;
            }

            public void StopRamp() {
                Stops++;
                ramp = null;
            }
        }

        [SetUp]
        public void SetUp() {
            home = Path.Combine(TestContext.CurrentContext.WorkDirectory, "warmup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(home);
            now = T0;
        }

        [TearDown]
        public void TearDown() {
            Directory.Delete(home, true);
        }

        private WarmupSession Session(SimCamera cam, double? ambient = 9.8) {
            return new WarmupSession(home, cam, () => ambient, () => now);
        }

        private void Request(string id = "test") {
            string p = Path.Combine(home, WarmupSession.RequestFile);
            File.WriteAllText(p, id);
            File.SetLastWriteTimeUtc(p, now);
        }

        private WarmupStatus OnDisk() {
            string p = Path.Combine(home, WarmupSession.StatusFile);
            return File.Exists(p) ? JsonSerializer.Deserialize<WarmupStatus>(File.ReadAllText(p)) : null;
        }

        private void Run(WarmupSession s, SimCamera cam, TimeSpan for_, bool touch = true) {
            DateTime end = now + for_;
            while (now < end) {
                now = now.AddSeconds(5);
                cam.Advance();
                if (touch && File.Exists(Path.Combine(home, WarmupSession.RequestFile))) {
                    File.SetLastWriteTimeUtc(Path.Combine(home, WarmupSession.RequestFile), now);
                }
                s.Tick();
            }
        }

        private static double MaxRisePerMinute(List<(DateTime At, double Sensor)> h) {
            double max = 0;
            for (int i = 0; i + 60 < h.Count; i++) {
                max = Math.Max(max, h[i + 60].Sensor - h[i].Sensor);
            }
            return max;
        }

        [Test]
        public void WithoutARequestNothingHappens() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            s.Tick();
            s.Active.Should().BeFalse();
            cam.Ramps.Should().BeEmpty();
            OnDisk().Should().BeNull();
        }

        [Test]
        public void AColdCameraWarmsToTheAmbientAtOneAndAHalfDegreesPerMinute() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            int began = 0;
            s.Began += () => began++;
            Request();
            s.Tick();

            s.Active.Should().BeTrue();
            began.Should().Be(1);
            cam.Ramps.Should().Equal((9.0, WarmupPlan.RateCPerMinute));
            OnDisk().State.Should().Be("warming");
            OnDisk().Reason.Should().Contain("from -16.0 °C to 9 °C");

            Run(s, cam, TimeSpan.FromMinutes(15));
            OnDisk().State.Should().Be("warming", "25 °C at 1 °C per minute takes 25 min");
            Run(s, cam, TimeSpan.FromMinutes(15));

            WarmupStatus done = OnDisk();
            done.State.Should().Be("done");
            done.Sensor.Should().BeGreaterThan(9 - WarmupPlan.NearAmbientC);
            s.Active.Should().BeTrue("imaging stays held until the installer removes the request");
            MaxRisePerMinute(cam.History).Should().BeLessOrEqualTo(1.5);
        }

        [Test]
        public void RemovingTheRequestEndsTheWarmUpAndStopsTheRamp() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            int ended = 0;
            s.Ended += () => ended++;
            Request();
            s.Tick();
            Run(s, cam, TimeSpan.FromMinutes(3));
            File.Delete(Path.Combine(home, WarmupSession.RequestFile));
            s.Tick();

            s.Active.Should().BeFalse();
            ended.Should().Be(1);
            cam.Stops.Should().Be(1);
            OnDisk().Should().BeNull();
        }

        [Test]
        public void ARequestNotTouchedForFiveMinutesEnds() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            Request();
            s.Tick();
            Run(s, cam, TimeSpan.FromMinutes(5), touch: false);
            s.Active.Should().BeTrue();
            Run(s, cam, TimeSpan.FromSeconds(10), touch: false);
            s.Active.Should().BeFalse();
            File.Exists(Path.Combine(home, WarmupSession.RequestFile)).Should().BeFalse();
        }

        [Test]
        public void AWarmOrDisconnectedCameraIsDoneAtOnceWithoutARamp() {
            var cam = new SimCamera(() => now, 8.1, 4);
            WarmupSession s = Session(cam);
            Request();
            s.Tick();
            OnDisk().State.Should().Be("done");
            cam.Ramps.Should().BeEmpty();

            File.Delete(Path.Combine(home, WarmupSession.RequestFile));
            s.Tick();
            var off = new SimCamera(() => now, -16) { Connected = false };
            s = Session(off);
            Request();
            s.Tick();
            OnDisk().State.Should().Be("done");
            OnDisk().Reason.Should().Contain("not connected");
            off.Ramps.Should().BeEmpty();
        }

        [Test]
        public void ADisconnectOrARefusedRampFails() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            Request();
            s.Tick();
            Run(s, cam, TimeSpan.FromMinutes(2));
            cam.Connected = false;
            Run(s, cam, TimeSpan.FromSeconds(5));
            OnDisk().State.Should().Be("failed");
            OnDisk().Reason.Should().Contain("disconnected");

            File.Delete(Path.Combine(home, WarmupSession.RequestFile));
            s.Tick();
            var refused = new SimCamera(() => now, -16) { RampResult = false };
            s = Session(refused);
            Request();
            s.Tick();
            Run(s, refused, TimeSpan.FromMinutes(30));
            OnDisk().State.Should().Be("failed");
            OnDisk().Reason.Should().Be("NINA could not ramp the setpoint");
        }

        [Test]
        public void WithoutTheAmbientTheTargetIsTwentyAndAnIdleCoolerEndsIt() {
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam, null);
            Request();
            s.Tick();
            cam.Ramps.Single().Target.Should().Be(WarmupPlan.FallbackTargetC);
            Run(s, cam, TimeSpan.FromMinutes(40));
            OnDisk().State.Should().Be("done");
        }

        [Test]
        public void AnUnknownSensorTemperatureFailsInsteadOfReportingDone() {
            var cam = new SimCamera(() => now, double.NaN);
            WarmupSession s = Session(cam);
            Request();
            s.Tick();
            OnDisk().State.Should().Be("failed");
            cam.Ramps.Should().BeEmpty();
        }

        [Test]
        public void ANewRequestReplacesTheOneBeingHandledAndTheStatusNamesIt() {
            var cold = new SimCamera(() => now, -16);
            WarmupSession s = Session(cold);
            Request("first");
            s.Tick();
            OnDisk().Request.Should().Be("first");
            Request("second");
            Run(s, cold, TimeSpan.FromSeconds(5));
            OnDisk().Request.Should().Be("second");
            OnDisk().State.Should().Be("warming");
            cold.Ramps.Should().HaveCount(2);
            cold.Stops.Should().Be(1);
        }

        [Test]
        public void ARequestLeftFromBeforeARestartIsRemoved() {
            Request();
            File.WriteAllText(Path.Combine(home, WarmupSession.StatusFile), "{}");
            var cam = new SimCamera(() => now, -16);
            WarmupSession s = Session(cam);
            s.ClearLeftovers();
            File.Exists(Path.Combine(home, WarmupSession.RequestFile)).Should().BeFalse();
            File.Exists(Path.Combine(home, WarmupSession.StatusFile)).Should().BeFalse();
            s.Tick();
            s.Active.Should().BeFalse();
        }
    }
}
