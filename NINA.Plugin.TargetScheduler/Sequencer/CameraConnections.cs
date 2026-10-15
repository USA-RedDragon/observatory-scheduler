using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Records when the camera last connected and feeds every camera update into the shared
    /// <see cref="SensorHold"/>, for <see cref="SensorGate"/>. It subscribes once to the camera
    /// mediator, which lives as long as NINA.
    /// </summary>
    public static class CameraConnections {
        private static readonly object lockObj = new object();
        private static ICameraMediator watched;
        private static Consumer consumer;
        private static DateTime? connectedAt;

        public static SensorHold Hold { get; } = new SensorHold(SensorHold.DefaultMaxGap);

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
            lock (lockObj) {
                connectedAt = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        private sealed class Consumer : ICameraConsumer {

            public void UpdateDeviceInfo(CameraInfo info) {
                if (info != null) {
                    Hold.Observe(DateTime.UtcNow, Read(info));
                }
            }

            public void Dispose() {
            }
        }
    }
}
