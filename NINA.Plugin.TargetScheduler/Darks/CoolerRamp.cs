using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Moves the camera setpoint at 1 °C per minute. Only one background ramp runs at a time: a new move first
    /// stops the one still running, and a background ramp keeps going when the instruction that started it is canceled.
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

        public static async Task Stop(string log) {
            CancellationTokenSource cts;
            Task<bool> task;
            string owner;
            lock (sync) {
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

        public static Task<bool> StartBackground(ICameraMediator camera, double target, string owner, string log, IProgress<ApplicationStatus> progress) {
            var cts = new CancellationTokenSource();
            Task<bool> task = RampTo(camera, target, log, progress, cts.Token);
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

        public static async Task<bool> RampTo(ICameraMediator camera, double target, string log, IProgress<ApplicationStatus> progress, CancellationToken token) {
            CameraInfo info = camera.GetInfo();
            if (double.IsNaN(info.Temperature)) {
                TSLogger.Warning($"{log}: not moving the setpoint to {target} °C: {DarkPlanner.SensorUnknown}");
                return false;
            }
            TimeSpan ramp = DarkPlanner.Ramp(info.Temperature, target);
            TSLogger.Info($"{log}: ramping the setpoint from {info.TemperatureSetPoint} °C (sensor {info.Temperature} °C) to {target} °C over {ramp.TotalMinutes:0.##} min");
            bool ok = await camera.CoolCamera(target, ramp, progress, token);
            if (ok && !DarkPlanner.SameSetpoint(camera.GetInfo().TemperatureSetPoint, target)) {
                ok = await camera.CoolCamera(target, TimeSpan.Zero, progress, token);
            }
            return ok;
        }
    }
}
