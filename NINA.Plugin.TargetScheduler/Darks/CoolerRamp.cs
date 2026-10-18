using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Moves the camera setpoint from the sensor temperature at 1 °C per minute, or the rate given. Only one background
    /// ramp runs at a time: a new move first stops the one still running, and a background ramp keeps going when the
    /// instruction that started it is canceled. While the camera warms for a shutdown (<see cref="WarmupRuntime"/>), every
    /// other move is refused and only the warm-up can stop its own ramp.
    /// </summary>
    public static class CoolerRamp {
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
            if (Refused(owner, target, log)) {
                return Task.FromResult(false);
            }
            var cts = new CancellationTokenSource();
            Task<bool> task = Ramp(camera, target, log, progress, cts.Token, cPerMinute);
            _ = task.ContinueWith(t => TSLogger.Warning($"{log}: the {owner} ramp to {target} °C ended: {t.Exception?.GetBaseException().Message ?? "canceled"}"),
                CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
            lock (sync) {
                backgroundCts = cts;
                backgroundTask = task;
                backgroundTarget = target;
                backgroundOwner = owner;
            }
            return task;
        }

        public static Task<bool> RampTo(ICameraMediator camera, double target, string log, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (Refused(null, target, log)) {
                return Task.FromResult(false);
            }
            return Ramp(camera, target, log, progress, token, DarkPlanner.RampCPerMinute);
        }

        private static async Task<bool> Ramp(ICameraMediator camera, double target, string log, IProgress<ApplicationStatus> progress, CancellationToken token, double cPerMinute) {
            CameraInfo info = camera.GetInfo();
            if (double.IsNaN(info.Temperature)) {
                TSLogger.Warning($"{log}: not moving the setpoint to {target} °C: {DarkPlanner.SensorUnknown}");
                return false;
            }
            TimeSpan ramp = DarkPlanner.Ramp(info.Temperature, target, cPerMinute);
            TSLogger.Info($"{log}: ramping the setpoint from {info.TemperatureSetPoint} °C (sensor {info.Temperature} °C) to {target} °C over {ramp.TotalMinutes:0.##} min");
            bool ok = await camera.CoolCamera(target, ramp, progress ?? new Progress<ApplicationStatus>(), token);
            if (ok && !DarkPlanner.SameSetpoint(camera.GetInfo().TemperatureSetPoint, target)) {
                ok = await camera.CoolCamera(target, TimeSpan.Zero, progress ?? new Progress<ApplicationStatus>(), token);
            }
            return ok;
        }
    }
}
