using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyFlatDevice;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Runs the <see cref="CoolerController"/> in NINA: samples the cooler from the camera's updates and moves the
    /// setpoint only at a light exposure boundary, by the shared 1 °C per minute ramp.
    /// </summary>
    public static class CoolerRuntime {
        public const string Log = "TS Cooler";
        public const string Owner = "cooler";

        private static readonly object sync = new object();
        private static readonly CoolerController controller = new CoolerController();
        private static readonly Lazy<CoolerSettings> settings = new Lazy<CoolerSettings>(() => new CoolerSettings(Path.Combine(Common.PLUGIN_HOME, "observatory-scheduler-cooler.json")));
        private static readonly Lazy<CoolerSetpointStore> store = new Lazy<CoolerSetpointStore>(() => new CoolerSetpointStore(Connect));

        private static ICameraMediator camera;
        private static IWeatherDataMediator weather;
        private static ISafetyMonitorMediator safety;
        private static IFlatDeviceMediator flatDevice;
        private static Sampler sampler;
        private static string busy;
        private static int busyCount;
        private static string lastPaused;
        private static string lastProblem;

        public static CoolerSettings Settings => settings.Value;

        public static void Start(ICameraMediator cameraMediator, IWeatherDataMediator weatherMediator, ISafetyMonitorMediator safetyMediator, IFlatDeviceMediator flatDeviceMediator) {
            lock (sync) {
                if (sampler != null || cameraMediator == null) {
                    return;
                }
                camera = cameraMediator;
                weather = weatherMediator;
                safety = safetyMediator;
                flatDevice = flatDeviceMediator;
                CoverWatch.Ensure(flatDeviceMediator);
                sampler = new Sampler();
                camera.RegisterConsumer(sampler);
            }
            try {
                store.Value.Ensure();
            } catch (Exception ex) {
                TSLogger.Warning($"{Log}: could not create the {CoolerSetpointStore.Table} table: {ex.Message}");
            }
        }

        public static void Stop() {
            lock (sync) {
                if (sampler != null) {
                    camera?.RemoveConsumer(sampler);
                    sampler = null;
                }
            }
        }

        private static DbConnection Connect() {
            var c = new SQLiteConnection($"Data Source={Path.Combine(Common.PLUGIN_HOME, "schedulerdb.sqlite")};") { BusyTimeout = 5000 };
            c.Open();
            return c;
        }

        /// <summary>Marks the setpoint as owned by darks or flats until disposed; no power samples count meanwhile.</summary>
        public static IDisposable Own(string what) {
            lock (sync) {
                busyCount++;
                busy = what;
            }
            return new Release();
        }

        private sealed class Release : IDisposable {
            private int done;

            public void Dispose() {
                if (Interlocked.Exchange(ref done, 1) == 1) {
                    return;
                }
                lock (sync) {
                    busyCount = Math.Max(0, busyCount - 1);
                    if (busyCount == 0) {
                        busy = null;
                    }
                }
            }
        }

        private static string PausedReason() {
            string b;
            lock (sync) {
                b = busyCount > 0 ? busy : null;
            }
            if (b != null) {
                return $"{b} own the setpoint";
            }
            string ramp = CoolerRamp.PendingOwner();
            if (ramp != null) {
                return $"the {ramp} ramp to {CoolerRamp.Pending()} °C is still running";
            }
            try {
                var s = safety?.GetInfo();
                if (s != null && s.Connected && !s.IsSafe) {
                    return "the safety monitor reports unsafe, so the roof may be closed";
                }
            } catch (Exception) {
            }
            try {
                FlatDeviceInfo f = flatDevice?.GetInfo();
                if (f != null) {
                    var cover = new DarkCover {
                        Configured = true,
                        Connected = f.Connected,
                        State = Enum.TryParse(f.CoverState.ToString(), out DarkCoverState st) ? st : DarkCoverState.Unknown,
                        LightOn = f.LightOn
                    };
                    cover = DarkRuntime.Cover.Apply(cover);
                    if ((cover.Connected || cover.Remembered) && cover.State == DarkCoverState.Closed) {
                        return cover.Connected ? "the flap is closed" : "the flap was last reported closed";
                    }
                }
            } catch (Exception) {
            }
            return null;
        }

        private static double? Ambient() {
            try {
                var w = weather?.GetInfo();
                return w != null && w.Connected && !double.IsNaN(w.Temperature) ? w.Temperature : (double?)null;
            } catch (Exception) {
                return null;
            }
        }

        private sealed class Sampler : ICameraConsumer {

            public void UpdateDeviceInfo(CameraInfo info) {
                if (info == null) {
                    return;
                }
                string paused = PausedReason();
                var o = new CoolerObservation {
                    At = DateTime.UtcNow,
                    Connected = info.Connected,
                    CoolerOn = info.CoolerOn,
                    Power = info.CoolerPower,
                    Sensor = info.Temperature,
                    Setpoint = info.TemperatureSetPoint,
                    Ambient = Ambient(),
                    Paused = paused
                };
                bool log;
                lock (sync) {
                    controller.Observe(o);
                    log = paused != lastPaused && controller.Setpoint.HasValue;
                    lastPaused = paused;
                }
                if (log) {
                    TSLogger.Info(paused != null ? $"{Log}: power samples paused: {paused}" : $"{Log}: power samples resumed");
                }
            }

            public void Dispose() {
            }
        }

        private sealed class Device : ICoolerDevice {
            public static readonly TimeSpan ReadingTimeout = TimeSpan.FromSeconds(30);

            private readonly TemperatureWatch watch;
            private readonly IProgress<ApplicationStatus> progress;

            public Device(TemperatureWatch watch, IProgress<ApplicationStatus> progress) {
                this.watch = watch;
                this.progress = progress;
            }

            public CoolerReading Read() {
                return Map(camera.GetInfo());
            }

            public async Task<bool> Ramp(double target, CancellationToken token) {
                token.ThrowIfCancellationRequested();
                await CoolerRamp.Stop(Log);
                token.ThrowIfCancellationRequested();
                Task<bool> ramp = CoolerRamp.StartBackground(camera, target, Owner, Log, progress);
                return await ramp.WaitAsync(token);
            }

            public async Task<CoolerReading> Next(CancellationToken token) {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(ReadingTimeout);
                try {
                    return Map(await watch.Next(timeout.Token));
                } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                    return Read();
                }
            }

            private static CoolerReading Map(CameraInfo i) {
                return i == null
                    ? new CoolerReading { At = DateTime.UtcNow, Connected = false }
                    : new CoolerReading { At = DateTime.UtcNow, Connected = i.Connected, Sensor = i.Temperature, Setpoint = i.TemperatureSetPoint, Power = i.CoolerPower };
            }
        }

        private static CoolerDecision unsettled;

        /// <summary>
        /// Runs before each scheduler light exposure: decides and, if needed, moves the setpoint and waits for the sensor.
        /// Returns a reason to skip the exposure after a move, so the scheduler plans again and the triggers run before
        /// the next light; null to take it. Errors other than a cancel are logged and the exposure goes ahead.
        /// </summary>
        public static async Task<string> BeforeExposure(IProgress<ApplicationStatus> progress, CancellationToken token) {
            try {
                return await Steer(progress, token);
            } catch (Exception ex) when (!(ex is OperationCanceledException) && !token.IsCancellationRequested) {
                TSLogger.Error($"{Log}: the cooler step failed, the exposure goes ahead: {ex.Message}");
                return null;
            }
        }

        private static async Task<string> Steer(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (camera == null) {
                return null;
            }
            CoolerSettingsData s = Settings.Snapshot();
            if (!s.Enabled) {
                return null;
            }
            string problem = s.Problem();
            if (problem != null) {
                if (problem != lastProblem) {
                    TSLogger.Warning($"{Log}: not steering the cooler, the settings are inconsistent: {problem}");
                    lastProblem = problem;
                }
                return null;
            }

            CameraInfo info = camera.GetInfo();
            if (info == null || !info.Connected || !info.CanSetTemperature || !info.CoolerOn) {
                TSLogger.Info($"{Log}: not steering the cooler: {(info == null || !info.Connected ? "the camera is not connected" : !info.CanSetTemperature ? "the camera cannot set its temperature" : "the cooler is off")}");
                return null;
            }

            DateTime now = DateTime.UtcNow;
            string night = DarkReadiness.NightOf(now.ToLocalTime());
            using var watch = new TemperatureWatch(camera);
            var device = new Device(watch, progress);

            CoolerDecision pending;
            lock (sync) {
                pending = unsettled;
                unsettled = null;
            }
            if (pending != null && pending.Target.HasValue) {
                string done = await Finish(pending, device, night, s, progress, token);
                if (done != null) {
                    return done;
                }
                info = camera.GetInfo();
            }

            CoolerDecision d;
            lock (sync) {
                controller.NewNight(night);
                controller.SetPaused(PausedReason());
                d = controller.Decide(now, info.TemperatureSetPoint, s, DarkRuntime.State.Holds(), DarkRuntime.State.Limits(true));
            }
            TSLogger.Info($"{Log}: {d}");

            if (!d.Moves) {
                if (d.Action == CoolerAction.Start && d.Target.HasValue) {
                    lock (sync) {
                        controller.Moved(d, now);
                    }
                    Record(d, night, "kept", s);
                    CoolerReading r = device.Read();
                    if (!(r.Connected && Math.Abs(r.Sensor - d.Target.Value) <= CoolerController.ReadyToleranceC)) {
                        return await Settled(d, device, night, s, progress, token);
                    }
                }
                return null;
            }

            return await Move(d, device, night, s, progress, token);
        }

        private static async Task<string> Finish(CoolerDecision d, Device device, string night, CoolerSettingsData s, IProgress<ApplicationStatus> progress, CancellationToken token) {
            Task<bool> ramp = CoolerRamp.PendingTask(Owner);
            if (ramp == null && !DarkPlanner.SameSetpoint(camera.GetInfo()?.TemperatureSetPoint ?? double.NaN, d.Target.Value)) {
                return null;
            }
            if (ramp != null) {
                TSLogger.Info($"{Log}: waiting for the canceled move's ramp to {d.Target.Value} °C to finish before the next light");
                bool ok;
                try {
                    ok = await ramp.WaitAsync(token);
                } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                    lock (sync) {
                        unsettled = d;
                    }
                    throw;
                }
                if (!ok) {
                    return await Failed(d, new CoolerMoveResult { Sensor = device.Read().Sensor, Reason = $"NINA could not ramp the setpoint to {d.Target.Value} °C" }, device, night, s, progress, token);
                }
            }
            return await Settled(d, device, night, s, progress, token);
        }

        private static async Task<string> Settled(CoolerDecision d, Device device, string night, CoolerSettingsData s, IProgress<ApplicationStatus> progress, CancellationToken token) {
            double target = d.Target.Value;
            progress?.Report(new ApplicationStatus { Status = $"Target Scheduler: waiting for the sensor to reach {target} °C" });
            CoolerMoveResult r;
            try {
                r = await CoolerMove.Settle(device, target, DateTime.UtcNow + CoolerMove.SettleLimit, token);
            } catch (OperationCanceledException) {
                lock (sync) {
                    unsettled = d;
                }
                throw;
            } finally {
                progress?.Report(new ApplicationStatus { Status = "" });
            }
            if (r.Reached) {
                TSLogger.Info(string.Format(CultureInfo.InvariantCulture, "{0}: the sensor is at {1:0.00} °C, within {2} °C of {3} °C; imaging continues", Log, r.Sensor, CoolerController.ReadyToleranceC, target));
                return null;
            }
            return await Failed(d, r, device, night, s, progress, token);
        }

        private static async Task<string> Move(CoolerDecision d, Device device, string night, CoolerSettingsData s, IProgress<ApplicationStatus> progress, CancellationToken token) {
            double target = d.Target.Value;
            progress?.Report(new ApplicationStatus { Status = $"Target Scheduler: moving the cooler setpoint to {target} °C" });
            CoolerMoveResult r;
            try {
                r = await CoolerMove.To(device, target, token);
            } catch (OperationCanceledException) {
                bool started = CoolerRamp.PendingTask(Owner) != null || DarkPlanner.SameSetpoint(camera.GetInfo()?.TemperatureSetPoint ?? double.NaN, target);
                if (started) {
                    lock (sync) {
                        controller.Moved(d, DateTime.UtcNow);
                        unsettled = d;
                    }
                    TSLogger.Info($"{Log}: canceled while moving to {target} °C; the ramp runs on in the background, and the next light waits for the sensor");
                    Record(d, night, "canceled, ramp continues", s);
                } else {
                    TSLogger.Info($"{Log}: canceled before the move to {target} °C started");
                }
                throw;
            } finally {
                progress?.Report(new ApplicationStatus { Status = "" });
            }

            if (r.Reached) {
                lock (sync) {
                    controller.Moved(d, DateTime.UtcNow);
                }
                if (d.From.HasValue) {
                    DarkRuntime.State.RecordMove(d.From.Value, target, r.Took, DateTime.UtcNow);
                }
                TSLogger.Info(string.Format(CultureInfo.InvariantCulture, "{0}: the sensor is at {1:0.00} °C, within {2} °C of {3} °C, {4:0} s after the move started (ramp {5:0.##} min); the scheduler plans again before the next light",
                    Log, r.Sensor, CoolerController.ReadyToleranceC, target, r.Took.TotalSeconds, r.Ramp.TotalMinutes));
                Record(d, night, "reached", s);
                return $"the cooler setpoint moved to {target} °C";
            }

            lock (sync) {
                controller.Moved(d, DateTime.UtcNow);
            }
            return await Failed(d, r, device, night, s, progress, token);
        }

        private static async Task<string> Failed(CoolerDecision d, CoolerMoveResult r, Device device, string night, CoolerSettingsData s, IProgress<ApplicationStatus> progress, CancellationToken token) {
            double target = d.Target.Value;
            TSLogger.Warning($"{Log}: {target} °C was not reached: {r.Reason}");
            Record(d, night, "failed: " + r.Reason, s);
            if (d.Action == CoolerAction.Warmer || r.Reason == DarkPlanner.CameraDisconnected || r.Reason == DarkPlanner.SensorUnknown || !r.Sensor.HasValue || double.IsNaN(r.Sensor.Value) || r.Sensor.Value <= target) {
                return null;
            }

            lock (sync) {
                controller.ColderFailed($"{target} °C was not reached at {DateTime.Now:HH:mm}: {r.Reason}");
            }
            if (d.Ambient.HasValue) {
                DarkRuntime.State.AddLimit(new CoolerLimit { At = DateTime.UtcNow, SetTemp = target, Ambient = d.Ambient, Reached = r.Sensor, Reason = r.Reason });
            }
            double back = CoolerController.CeilGrid(r.Sensor.Value);
            if (back <= target) {
                back = target + CoolerGrid.StepC;
            }
            var warmer = new CoolerDecision {
                Action = CoolerAction.Warmer, From = target, Target = back, Power = d.Power, Ambient = d.Ambient, Fit = d.Fit,
                Reason = string.Format(CultureInfo.InvariantCulture, "the sensor only reached {0:0.0} °C on the way to {1} °C, so the setpoint goes to the grid point above it; colder steps are off for the rest of the night", r.Sensor.Value, target)
            };
            TSLogger.Info($"{Log}: {warmer}");
            return await Move(warmer, device, night, s, progress, token);
        }

        private static void Record(CoolerDecision d, string night, string outcome, CoolerSettingsData s) {
            try {
                store.Value.Record(d, night, outcome, s, DateTime.UtcNow);
            } catch (Exception ex) {
                TSLogger.Warning($"{Log}: could not record the decision in {CoolerSetpointStore.Table}: {ex.Message}");
            }
        }
    }
}
