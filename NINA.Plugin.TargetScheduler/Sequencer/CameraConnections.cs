using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Records when the camera last connected and feeds every camera update into the shared
    /// <see cref="SensorHold"/>, for <see cref="SensorGate"/>. It subscribes once to the camera
    /// mediator, which lives as long as NINA. It also remembers the last setpoint the cooler held, and when the
    /// camera connects again while the plugin owns the cooler it ramps back to that setpoint from the sensor
    /// (<see cref="ReconnectRestore"/>) instead of letting the cooler jump.
    /// </summary>
    public static class CameraConnections {
        public const string Log = "TS Cooler";
        public const string RestoreOwner = "reconnect";
        public static readonly TimeSpan FirstUpdateWait = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan CoolerOffAfter = TimeSpan.FromSeconds(30);

        private static readonly object lockObj = new object();
        private static ICameraMediator watched;
        private static Consumer consumer;
        private static DateTime? connectedAt;
        private static double? lastSetpoint;
        private static TaskCompletionSource<bool> firstUpdate;

        public static SensorHold Hold { get; } = new SensorHold(SensorHold.DefaultMaxGap);

        /// <summary>The steady-state cooler power at the camera's setpoint, for the darks' 80% limit.</summary>
        public static CoolerSteady Steady { get; } = new CoolerSteady();

        public static void Watch(ICameraMediator cameraMediator) {
            if (cameraMediator == null) {
                return;
            }
            lock (lockObj) {
                if (ReferenceEquals(watched, cameraMediator)) {
                    return;
                }
                if (watched != null) {
                    watched.Connected -= OnConnected;
                    watched.RemoveConsumer(consumer);
                }
                watched = cameraMediator;
                watched.Connected += OnConnected;
                consumer = new Consumer();
                watched.RegisterConsumer(consumer);
            }
        }

        public static DateTime? ConnectedAt {
            get {
                lock (lockObj) {
                    return connectedAt;
                }
            }
        }

        public static SensorReading Read(CameraInfo info) {
            return info == null
                ? new SensorReading(false, false, false, double.NaN, double.NaN, CoolerRamp.Pending(), ConnectedAt)
                : new SensorReading(info.Connected, info.CanSetTemperature, info.CoolerOn, info.Temperature, info.TemperatureSetPoint, CoolerRamp.Pending(), ConnectedAt);
        }

        private static Task OnConnected(object sender, EventArgs e) {
            double? before;
            bool owned;
            ICameraMediator camera;
            TaskCompletionSource<bool> first;
            lock (lockObj) {
                connectedAt = DateTime.UtcNow;
                before = lastSetpoint;
                camera = watched;
                first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                firstUpdate = first;
            }
            owned = !WarmupRuntime.Active && CoolerRuntime.OwnsCooler();
            if (camera != null && before.HasValue && owned) {
                _ = Task.Run(() => RestoreAfterConnect(camera, before.Value, first.Task));
            }
            return Task.CompletedTask;
        }

        private static async Task RestoreAfterConnect(ICameraMediator camera, double before, Task first) {
            try {
                await Task.WhenAny(first, Task.Delay(FirstUpdateWait));
                CameraInfo i = camera.GetInfo();
                bool owned = !WarmupRuntime.Active && CoolerRuntime.OwnsCooler();
                (double? target, string why) = i == null
                    ? ((double?)null, "no camera info")
                    : ReconnectRestore.Decide(owned, before, i.Connected, i.CanSetTemperature, i.CoolerOn, i.Temperature, i.TemperatureSetPoint);
                if (!target.HasValue) {
                    TSLogger.Info($"{Log}: the camera connected again; not restoring {before} °C: {why}");
                    return;
                }
                TSLogger.Warning($"{Log}: the camera connected again while the plugin owns the cooler: {why}");
                await CoolerRamp.Stop(Log);
                _ = CoolerRamp.StartBackground(camera, target.Value, RestoreOwner, Log, null);
            } catch (Exception ex) {
                TSLogger.Error($"{Log}: restoring the setpoint after the camera connected failed: {ex.Message}");
            }
        }

        private static void Remember(CameraInfo info, DateTime now) {
            TaskCompletionSource<bool> first = null;
            lock (lockObj) {
                if (!info.Connected) {
                    return;
                }
                first = firstUpdate;
                firstUpdate = null;
                if (!info.CanSetTemperature) {
                    lastSetpoint = null;
                } else if (info.CoolerOn && !double.IsNaN(info.TemperatureSetPoint)) {
                    lastSetpoint = info.TemperatureSetPoint;
                } else if (!info.CoolerOn && connectedAt.HasValue && now - connectedAt.Value > CoolerOffAfter) {
                    lastSetpoint = null;
                }
            }
            first?.TrySetResult(true);
        }

        private sealed class Consumer : ICameraConsumer {

            public void UpdateDeviceInfo(CameraInfo info) {
                if (info != null) {
                    DateTime now = DateTime.UtcNow;
                    Hold.Observe(now, Read(info));
                    Steady.Observe(now, info.Connected, info.Temperature, info.TemperatureSetPoint, info.CoolerPower);
                    Remember(info, now);
                }
            }

            public void Dispose() {
            }
        }
    }
}
