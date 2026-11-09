using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Moves the camera setpoint by <see cref="SetpointStepper"/>: it holds the sensor temperature, then steps 1 °C at a time,
    /// at most 1 °C per minute, while the sensor follows. Only one background
    /// ramp runs at a time: a new move first stops the one still running, and a background ramp keeps going when the
    /// instruction that started it is canceled. While the camera warms for a shutdown (<see cref="WarmupRuntime"/>), every
    /// other move is refused and only the warm-up can stop its own ramp.
    /// </summary>
    public static class CoolerRamp {
        /// <summary>The owner of the warm-up that runs when nothing is left for the camera.</summary>
        public const string IdleWarmOwner = "idle warm-up";

        private static readonly object sync = new object();
        private static CancellationTokenSource backgroundCts;
        private static Task<bool> backgroundTask;
        private static double backgroundTarget;
        private static string backgroundOwner;

        public static double? Pending() {
            lock (sync) {
                return backgroundTask != null && !backgroundTask.IsCompleted ? backgroundTarget : (double?)null;
            }
        }

        public static string PendingOwner() {
            lock (sync) {
                return backgroundTask != null && !backgroundTask.IsCompleted ? backgroundOwner : null;
            }
        }

        public static Task<bool> PendingTask(string owner) {
            lock (sync) {
                return backgroundTask != null && !backgroundTask.IsCompleted && backgroundOwner == owner ? backgroundTask : null;
            }
        }

        private static bool Refused(string owner, double target, string log) {
            if (WarmupRuntime.Active && owner != WarmupRuntime.Owner) {
                TSLogger.Warning($"{log}: not moving the setpoint to {target} °C: {WarmupRuntime.Reason}");
                return true;
            }
            return false;
        }

        public static async Task Stop(string log, string caller = null) {
            CancellationTokenSource cts;
            Task<bool> task;
            string owner;
            lock (sync) {
                if (backgroundTask != null && !backgroundTask.IsCompleted && backgroundOwner == WarmupRuntime.Owner && caller != WarmupRuntime.Owner) {
                    return;
                }
                cts = backgroundCts;
                task = backgroundTask;
                owner = backgroundOwner;
                backgroundCts = null;
                backgroundTask = null;
            }
            if (task == null) {
                return;
            }
            if (!task.IsCompleted) {
                TSLogger.Info($"{log}: stopping the {owner} ramp that is still running");
                cts.Cancel();
                try {
                    await task;
                } catch (Exception) {
                }
            }
            cts.Dispose();
        }

        public static Task<bool> StartBackground(ICameraMediator camera, double target, string owner, string log, IProgress<ApplicationStatus> progress, double cPerMinute = DarkPlanner.RampCPerMinute) {
            return Background(target, owner, log, t => Ramp(camera, target, log, progress, t, cPerMinute));
        }

        /// <summary>
        /// Starts the warm-up for a camera with nothing left to do: steps the setpoint to <paramref name="target"/> at
        /// 1 °C per minute and turns the cooler off. Any other move stops it, as does a reason from <paramref name="stop"/>.
        /// </summary>
        public static Task<bool> StartWarmOff(ICameraMediator camera, double target, string owner, string log, Func<string> stop) {
            return Background(target, owner, log, t => Stepper(camera, log, null).WarmAndStop(target, stop, t));
        }

        private static Task<bool> Background(double target, string owner, string log, Func<CancellationToken, Task<bool>> body) {
            if (Refused(owner, target, log)) {
                return Task.FromResult(false);
            }
            var cts = new CancellationTokenSource();
            CancellationTokenSource previous = null;
            Task<bool> previousTask = null;
            string previousOwner = null;
            Task<bool> task;
            lock (sync) {
                if (backgroundTask != null && !backgroundTask.IsCompleted) {
                    if (backgroundOwner == WarmupRuntime.Owner && owner != WarmupRuntime.Owner) {
                        TSLogger.Warning($"{log}: not moving the setpoint to {target} °C: {WarmupRuntime.Reason}");
                        return Task.FromResult(false);
                    }
                    previous = backgroundCts;
                    previousTask = backgroundTask;
                    previousOwner = backgroundOwner;
                }
                task = Task.Run(async () => {
                    if (previousTask != null) {
                        try {
                            await previousTask;
                        } catch (Exception) {
                        }
                    }
                    cts.Token.ThrowIfCancellationRequested();
                    return await body(cts.Token);
                });
                backgroundCts = cts;
                backgroundTask = task;
                backgroundTarget = target;
                backgroundOwner = owner;
            }
            if (previous != null) {
                TSLogger.Info($"{log}: stopping the {previousOwner} ramp that is still running");
                previous.Cancel();
            }
            _ = task.ContinueWith(t => TSLogger.Warning($"{log}: the {owner} ramp to {target} °C ended: {t.Exception?.GetBaseException().Message ?? "canceled"}"),
                CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
            return task;
        }

        public static Task<bool> RampTo(ICameraMediator camera, double target, string log, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (Refused(null, target, log)) {
                return Task.FromResult(false);
            }
            return Ramp(camera, target, log, progress, token, DarkPlanner.RampCPerMinute);
        }

        private static Task<bool> Ramp(ICameraMediator camera, double target, string log, IProgress<ApplicationStatus> progress, CancellationToken token, double cPerMinute) {
            return Stepper(camera, log, progress).Ramp(target, cPerMinute, null, token);
        }

        private static SetpointStepper Stepper(ICameraMediator camera, string log, IProgress<ApplicationStatus> progress) {
            return new SetpointStepper(new NinaCamera(camera, progress), log: m => TSLogger.Info($"{log}: {m}"));
        }

        /// <summary>
        /// The camera through NINA: a setpoint is set by CoolCamera with no duration, which sets it at once and turns the
        /// cooler on; the cooler is turned off on the device itself, never by NINA's WarmCamera.
        /// </summary>
        private sealed class NinaCamera : ISetpointCamera {
            private readonly ICameraMediator camera;
            private readonly IProgress<ApplicationStatus> progress;

            public NinaCamera(ICameraMediator camera, IProgress<ApplicationStatus> progress) {
                this.camera = camera;
                this.progress = progress ?? new Progress<ApplicationStatus>();
            }

            public CoolerReading Read() {
                CameraInfo i = camera.GetInfo();
                return i == null
                    ? new CoolerReading { At = DateTime.UtcNow, Connected = false }
                    : new CoolerReading { At = DateTime.UtcNow, Connected = i.Connected, Sensor = i.Temperature, Setpoint = i.TemperatureSetPoint, Power = i.CoolerPower };
            }

            public Task<bool> Set(double setpoint, CancellationToken token) {
                return camera.CoolCamera(setpoint, TimeSpan.Zero, progress, token);
            }

            public Task<bool> CoolerOff() {
                if (camera.GetDevice() is ICamera c) {
                    c.CoolerOn = false;
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }
    }
}
