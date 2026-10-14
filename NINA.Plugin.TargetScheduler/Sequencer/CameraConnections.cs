using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Records when the camera last connected, for <see cref="SensorGate"/>. It subscribes once to
    /// the camera mediator, which lives as long as NINA.
    /// </summary>
    public static class CameraConnections {
        private static readonly object lockObj = new object();
        private static ICameraMediator watched;
        private static DateTime? connectedAt;

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
                }
                watched = cameraMediator;
                watched.Connected += OnConnected;
            }
        }

        public static DateTime? ConnectedAt {
            get {
                lock (lockObj) {
                    return connectedAt;
                }
            }
        }

        private static Task OnConnected(object sender, EventArgs e) {
            lock (lockObj) {
                connectedAt = DateTime.Now;
            }
            return Task.CompletedTask;
        }
    }
}
