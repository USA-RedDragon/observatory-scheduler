using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// The warm-up before anything drops the cooler (a NINA stop for a plugin install, a camera disconnect, the cooler off):
    /// the setpoint ramps from the sensor temperature to the ambient at <see cref="RateCPerMinute"/>, and the warm-up is done
    /// once the ramp has ended and the sensor is within <see cref="NearAmbientC"/> of the target, or the cooler has nothing
    /// left to do (power at most <see cref="IdlePowerPercent"/>, the sensor is at the air temperature).
    /// </summary>
    public static class WarmupPlan {
        public const double RateCPerMinute = 1.5;
        public const double NearAmbientC = 2.0;
        public const double IdlePowerPercent = 5.0;
        public const double MaxTargetC = 20.0;

        /// <summary>The target without a weather reading: NINA's own warm-up target.</summary>
        public const double FallbackTargetC = 20.0;

        public static double Target(double? ambient) {
            if (!ambient.HasValue || double.IsNaN(ambient.Value)) {
                return FallbackTargetC;
            }
            return Math.Min(MaxTargetC, Math.Floor(ambient.Value));
        }

        /// <summary>True when the sensor is cold enough below the target that dropping the cooler now would warm it fast.</summary>
        public static bool Needed(double sensor, double target) {
            return !double.IsNaN(sensor) && sensor < target - NearAmbientC;
        }

        public static TimeSpan Duration(double sensor, double target) {
            return DarkPlanner.Ramp(sensor, target, RateCPerMinute);
        }

        public static bool Done(bool rampEnded, double sensor, double power, double target) {
            if (!rampEnded || double.IsNaN(sensor)) {
                return false;
            }
            return !Needed(sensor, target) || (!double.IsNaN(power) && power <= IdlePowerPercent);
        }

        public static string Describe(double sensor, double target) {
            return string.Format(CultureInfo.InvariantCulture, "from {0:0.0} °C to {1:0.#} °C over {2:0.##} min ({3} °C per minute)", sensor, target, Duration(sensor, target).TotalMinutes, RateCPerMinute);
        }
    }

    public class WarmupReading {
        public bool Connected { get; set; }
        public bool CanSetTemperature { get; set; }
        public bool CoolerOn { get; set; }
        public double Sensor { get; set; } = double.NaN;
        public double Setpoint { get; set; } = double.NaN;
        public double Power { get; set; } = double.NaN;
    }

    /// <summary>The camera as the warm-up sees it.</summary>
    public interface IWarmupCamera {

        WarmupReading Read();

        /// <summary>Starts ramping the setpoint from the sensor temperature to the target at the rate; the task ends with the ramp.</summary>
        Task<bool> StartRamp(double target, double cPerMinute);

        /// <summary>Stops the warm-up's ramp if it still runs.</summary>
        void StopRamp();
    }

    public class WarmupStatus {
        public string State { get; set; }
        public string Reason { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public double Target { get; set; }
        public double? Ambient { get; set; }
        public double RateCPerMinute { get; set; }
        public bool Connected { get; set; }
        public bool CoolerOn { get; set; }
        public double? Sensor { get; set; }
        public double? Setpoint { get; set; }
        public double? Power { get; set; }
    }

    /// <summary>
    /// The installer's warm-up request, handled one poll at a time. The installer creates <see cref="RequestFile"/> in the
    /// plugin folder and touches it while it waits. On a new request the session starts the ramp and reports in
    /// <see cref="StatusFile"/>: warming, then done or failed. The request ends when the file is deleted or has not been
    /// touched for <see cref="StaleAfter"/>; a request left from before NINA started is removed by <see cref="ClearLeftovers"/>.
    /// </summary>
    public class WarmupSession {
        public const string Log = "TS Warm-up";
        public const string RequestFile = "camera-warmup.request";
        public const string StatusFile = "camera-warmup.status.json";
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

        private readonly object sync = new object();
        private readonly string home;
        private readonly IWarmupCamera camera;
        private readonly Func<double?> ambient;
        private readonly Func<DateTime> clock;
        private WarmupStatus status;
        private Task<bool> ramp;

        public WarmupSession(string home, IWarmupCamera camera, Func<double?> ambient, Func<DateTime> clock = null) {
            this.home = home;
            this.camera = camera;
            this.ambient = ambient;
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        private volatile bool active;

        public bool Active => active;

        public event Action Began;

        public event Action Ended;

        public WarmupStatus Status {
            get {
                lock (sync) {
                    return status;
                }
            }
        }

        public string Reason {
            get {
                WarmupStatus s = Status;
                return s == null ? "the camera is warming for a shutdown" : $"the camera is warming for a shutdown ({s.State}: {s.Reason})";
            }
        }

        public string RequestPath => Path.Combine(home, RequestFile);

        public string StatusPath => Path.Combine(home, StatusFile);

        public void ClearLeftovers() {
            foreach (string p in new[] { RequestPath, StatusPath }) {
                try {
                    if (File.Exists(p)) {
                        File.Delete(p);
                        TSLogger.Info($"{Log}: removed {Path.GetFileName(p)} left from before NINA started; a warm-up request never survives a restart");
                    }
                } catch (Exception ex) {
                    TSLogger.Warning($"{Log}: could not remove {p}: {ex.Message}");
                }
            }
        }

        public void Tick() {
            bool requested = File.Exists(RequestPath);
            if (requested && clock() - File.GetLastWriteTimeUtc(RequestPath) > StaleAfter) {
                TSLogger.Warning($"{Log}: {RequestFile} was not touched for {StaleAfter.TotalMinutes:0} min, so the installer is gone; ending the warm-up");
                TryDelete(RequestPath);
                requested = false;
            }
            if (requested && !Active) {
                Begin();
            } else if (!requested && Active) {
                End();
            } else if (Active) {
                Update();
            }
        }

        private void Begin() {
            WarmupReading r = camera.Read() ?? new WarmupReading();
            double? air = ambient?.Invoke();
            double target = WarmupPlan.Target(air);
            var s = new WarmupStatus { StartedAt = clock(), Target = target, Ambient = air, RateCPerMinute = WarmupPlan.RateCPerMinute };
            Fill(s, r);
            if (!r.Connected) {
                s.State = "done";
                s.Reason = "the camera is not connected, so there is no cooler to drop";
            } else if (!r.CanSetTemperature) {
                s.State = "done";
                s.Reason = "the camera cannot set its temperature";
            } else if (!WarmupPlan.Needed(r.Sensor, target)) {
                s.State = "done";
                s.Reason = string.Format(CultureInfo.InvariantCulture, "the sensor is at {0:0.0} °C, within {1} °C of {2:0.#} °C", r.Sensor, WarmupPlan.NearAmbientC, target);
            } else {
                s.State = "warming";
                s.Reason = "ramping the setpoint " + WarmupPlan.Describe(r.Sensor, target) + (air.HasValue ? "" : "; the ambient is not known, so the target is NINA's own warm-up target");
            }
            lock (sync) {
                status = s;
            }
            active = true;
            Began?.Invoke();
            TSLogger.Info($"{Log}: warm-up requested: {s.State}: {s.Reason}");
            if (s.State == "warming") {
                ramp = camera.StartRamp(target, WarmupPlan.RateCPerMinute);
            }
            Write(s);
        }

        private void Update() {
            WarmupStatus s = Status;
            if (s == null) {
                return;
            }
            WarmupReading r = camera.Read() ?? new WarmupReading();
            Fill(s, r);
            if (s.State == "warming") {
                Task<bool> t = ramp;
                bool ended = t != null && t.IsCompleted;
                if (!r.Connected) {
                    s.State = "failed";
                    s.Reason = "the camera disconnected during the warm-up";
                } else if (ended && (t.IsFaulted || t.IsCanceled || !t.Result)) {
                    s.State = "failed";
                    s.Reason = t.IsFaulted ? $"NINA could not ramp the setpoint: {t.Exception?.GetBaseException().Message}" : "NINA could not ramp the setpoint";
                } else if (WarmupPlan.Done(ended, r.Sensor, r.Power, s.Target)) {
                    s.State = "done";
                    s.Reason = string.Format(CultureInfo.InvariantCulture, "the sensor is at {0:0.0} °C after {1:0.#} min, cooler {2:0}%", r.Sensor, (clock() - s.StartedAt).TotalMinutes, r.Power);
                }
                if (s.State != "warming") {
                    TSLogger.Info($"{Log}: {s.State}: {s.Reason}");
                }
            }
            Write(s);
        }

        private void End() {
            WarmupStatus s;
            lock (sync) {
                s = status;
                status = null;
            }
            active = false;
            TSLogger.Info($"{Log}: the request was withdrawn{(s != null ? $" ({s.State}: {s.Reason})" : "")}; imaging resumes and the cooler controller cools again by its ramp");
            Task<bool> t = ramp;
            ramp = null;
            if (t != null && !t.IsCompleted) {
                camera.StopRamp();
            }
            TryDelete(StatusPath);
            Ended?.Invoke();
        }

        private void Fill(WarmupStatus s, WarmupReading r) {
            s.UpdatedAt = clock();
            s.Connected = r.Connected;
            s.CoolerOn = r.CoolerOn;
            s.Sensor = double.IsNaN(r.Sensor) ? null : r.Sensor;
            s.Setpoint = double.IsNaN(r.Setpoint) ? null : r.Setpoint;
            s.Power = double.IsNaN(r.Power) ? null : r.Power;
        }

        private void Write(WarmupStatus s) {
            try {
                string tmp = StatusPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, StatusPath, true);
            } catch (Exception ex) {
                TSLogger.Warning($"{Log}: could not write {StatusFile}: {ex.Message}");
            }
        }

        private static void TryDelete(string p) {
            try {
                if (File.Exists(p)) {
                    File.Delete(p);
                }
            } catch (Exception) {
            }
        }
    }
}
