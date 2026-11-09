using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>The camera as the setpoint stepper sees it.</summary>
    public interface ISetpointCamera {

        CoolerReading Read();

        /// <summary>Sets the setpoint at once, with the cooler on. Never a ramp of NINA's own.</summary>
        Task<bool> Set(double setpoint, CancellationToken token);

        /// <summary>Turns the cooler off.</summary>
        Task<bool> CoolerOff();
    }

    /// <summary>
    /// Moves the camera setpoint in whole-degree steps the plugin times itself. It first sets the setpoint to the sensor
    /// temperature, so the sensor is held where it is, then steps at most <see cref="StepC"/> every
    /// <see cref="MinInterval"/> or slower, and steps again only while the sensor is within <see cref="FollowC"/> of
    /// the setpoint. NINA's CoolCamera with a duration and WarmCamera are never used: on the ZWO ASI2600MM Pro both jump the
    /// setpoint, and the sensor warmed 12 to 19 °C per minute on 2026-10-18 and 2026-11-09.
    /// </summary>
    public class SetpointStepper {
        public const double StepC = 1.0;
        public const double MaxRateCPerMinute = 1.0;
        public const double FollowC = 1.0;
        public const double IdlePowerPercent = 5.0;
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan MaxLag = TimeSpan.FromMinutes(10);

        private readonly ISetpointCamera camera;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Func<DateTime> clock;
        private readonly Action<string> log;

        public SetpointStepper(ISetpointCamera camera, Func<TimeSpan, CancellationToken, Task> delay = null, Func<DateTime> clock = null, Action<string> log = null) {
            this.camera = camera;
            this.delay = delay ?? Task.Delay;
            this.clock = clock ?? (() => DateTime.UtcNow);
            this.log = log ?? (_ => { });
        }

        /// <summary>The time between steps for a rate, never faster than 1 °C per minute.</summary>
        public static TimeSpan Interval(double cPerMinute) {
            if (!(cPerMinute > 0) || cPerMinute >= MaxRateCPerMinute) {
                return MinInterval;
            }
            return TimeSpan.FromSeconds(Math.Ceiling(StepC / cPerMinute * 60));
        }

        /// <summary>The first setpoint: the sensor temperature in whole degrees, never past the target.</summary>
        public static double Hold(double sensor, double target) {
            double hold = Math.Round(sensor, MidpointRounding.AwayFromZero);
            return target >= sensor ? Math.Min(hold, target) : Math.Max(hold, target);
        }

        /// <summary>The ramp time from the sensor temperature to the target in whole steps, at least one interval for the hold.</summary>
        public static TimeSpan Duration(double sensor, double target, double cPerMinute) {
            if (double.IsNaN(sensor) || double.IsNaN(target)) {
                return DarkPlanner.MaxRamp;
            }
            double steps = Math.Ceiling(Math.Abs(target - Hold(sensor, target)) / StepC - 1e-9);
            return TimeSpan.FromTicks(Interval(cPerMinute).Ticks * (long)(steps + 1));
        }

        private static string F(double v) {
            return v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Holds the sensor temperature, then steps the setpoint to the target. False when the camera is gone, a set fails,
        /// <paramref name="stop"/> gives a reason, or the sensor does not follow for <see cref="MaxLag"/>. A cancel leaves the
        /// setpoint at the last step, which the cooler holds.
        /// </summary>
        public async Task<bool> Ramp(double target, double cPerMinute, Func<string> stop, CancellationToken token) {
            CoolerReading r = camera.Read();
            if (r == null || !r.Connected) {
                log("not moving the setpoint: " + DarkPlanner.CameraDisconnected);
                return false;
            }
            if (double.IsNaN(r.Sensor)) {
                log("not moving the setpoint to " + F(target) + " °C: " + DarkPlanner.SensorUnknown);
                return false;
            }
            TimeSpan interval = Interval(cPerMinute);
            double sp = Hold(r.Sensor, target);
            log(string.Format(CultureInfo.InvariantCulture, "holding the sensor at {0} °C (sensor {1} °C, setpoint was {2}), then stepping to {3} °C by {4} °C every {5:0} s",
                F(sp), F(r.Sensor), double.IsNaN(r.Setpoint) ? "unknown" : F(r.Setpoint) + " °C", F(target), StepC, interval.TotalSeconds));
            if (!await camera.Set(sp, token)) {
                log("the camera did not take the setpoint " + F(sp) + " °C");
                return false;
            }
            DateTime? lagSince = null;
            while (!DarkPlanner.SameSetpoint(sp, target)) {
                await delay(interval, token);
                token.ThrowIfCancellationRequested();
                string why = stop?.Invoke();
                if (why != null) {
                    log("stopping at " + F(sp) + " °C, which the cooler holds: " + why);
                    return false;
                }
                r = camera.Read();
                if (r == null || !r.Connected) {
                    log("stopping at " + F(sp) + " °C: " + DarkPlanner.CameraDisconnected);
                    return false;
                }
                bool warming = target > sp;
                if (warming && r.Sensor < sp - FollowC && !double.IsNaN(r.Power) && r.Power <= IdlePowerPercent) {
                    log(string.Format(CultureInfo.InvariantCulture, "the sensor stays at {0} °C below the {1} °C setpoint with the cooler at {2:0}%, so it is at the air temperature; the ramp ends there",
                        F(r.Sensor), F(sp), r.Power));
                    return true;
                }
                if (double.IsNaN(r.Sensor) || Math.Abs(r.Sensor - sp) > FollowC) {
                    lagSince ??= clock();
                    if (clock() - lagSince.Value >= MaxLag) {
                        log(string.Format(CultureInfo.InvariantCulture, "stopping at {0} °C: the sensor did not come within {1} °C of it in {2:0} min (sensor {3} °C, cooler {4:0}%)",
                            F(sp), FollowC, MaxLag.TotalMinutes, F(r.Sensor), r.Power));
                        return false;
                    }
                    log(string.Format(CultureInfo.InvariantCulture, "holding {0} °C: the sensor is at {1} °C, more than {2} °C away (cooler {3:0}%)", F(sp), F(r.Sensor), FollowC, r.Power));
                    continue;
                }
                lagSince = null;
                double next = warming ? Math.Min(sp + StepC, target) : Math.Max(sp - StepC, target);
                if (!await camera.Set(next, token)) {
                    log("the camera did not take the setpoint " + F(next) + " °C; it stays at " + F(sp) + " °C");
                    return false;
                }
                log(string.Format(CultureInfo.InvariantCulture, "setpoint {0} -> {1} °C (sensor {2} °C, cooler {3:0}%)", F(sp), F(next), F(r.Sensor), r.Power));
                sp = next;
            }
            return true;
        }

        /// <summary>
        /// The warm-up when nothing is left for the camera: steps the setpoint to <paramref name="target"/> at 1 °C per minute,
        /// waits one more interval for the sensor, and turns the cooler off. False, with the cooler left on at the last step,
        /// when the ramp ends early.
        /// </summary>
        public async Task<bool> WarmAndStop(double target, Func<string> stop, CancellationToken token) {
            if (!await Ramp(target, MaxRateCPerMinute, stop, token)) {
                return false;
            }
            await delay(MinInterval, token);
            token.ThrowIfCancellationRequested();
            string why = stop?.Invoke();
            if (why != null) {
                log("not turning the cooler off: " + why);
                return false;
            }
            CoolerReading r = camera.Read();
            if (r == null || !r.Connected) {
                log("not turning the cooler off: " + DarkPlanner.CameraDisconnected);
                return false;
            }
            if (!await camera.CoolerOff()) {
                log("the camera did not turn the cooler off");
                return false;
            }
            log(string.Format(CultureInfo.InvariantCulture, "cooler off at {0} °C (sensor {1} °C, cooler was {2:0}%)", F(target), F(r.Sensor), r.Power));
            return true;
        }
    }
}
