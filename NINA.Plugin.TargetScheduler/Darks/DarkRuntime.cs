using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    public static class DarkRuntime {
        private static readonly Lazy<DarkState> state = new Lazy<DarkState>(() => new DarkState(Path.Combine(Common.PLUGIN_HOME, "observatory-scheduler-darks.json")));
        private static readonly Lazy<DarkStore> store = new Lazy<DarkStore>(() => new DarkStore(Connect));
        private static readonly Lazy<BiasStore> biasStore = new Lazy<BiasStore>(() => new BiasStore(Connect));
        private static readonly Lazy<Flats.FlatRequestStore> flatRequestStore = new Lazy<Flats.FlatRequestStore>(() => new Flats.FlatRequestStore(Connect));

        public static DarkState State => state.Value;
        public static DarkStore Store => store.Value;
        public static BiasStore BiasStore => biasStore.Value;
        public static Flats.FlatRequestStore FlatRequestStore => flatRequestStore.Value;

        private static DbConnection Connect() {
            var c = new SQLiteConnection($"Data Source={Path.Combine(Common.PLUGIN_HOME, "schedulerdb.sqlite")};") { BusyTimeout = 5000 };
            c.Open();
            return c;
        }
    }

    public sealed class TemperatureWatch : ICameraConsumer {
        private readonly object sync = new object();
        private readonly ICameraMediator camera;
        private readonly List<TaskCompletionSource<CameraInfo>> waiters = new List<TaskCompletionSource<CameraInfo>>();
        private double? holdSetpoint;
        private double maxDeviation;
        private double? peakPower;
        private bool disposed;

        public TemperatureWatch(ICameraMediator camera) {
            this.camera = camera;
            camera.RegisterConsumer(this);
        }

        public void UpdateDeviceInfo(CameraInfo info) {
            List<TaskCompletionSource<CameraInfo>> wake;
            lock (sync) {
                if (holdSetpoint.HasValue && info != null && info.Connected && !double.IsNaN(info.Temperature)) {
                    maxDeviation = Math.Max(maxDeviation, Math.Abs(info.Temperature - holdSetpoint.Value));
                }
                if (holdSetpoint.HasValue && info != null && info.Connected) {
                    peakPower = Peak(peakPower, info.CoolerPower);
                }
                wake = new List<TaskCompletionSource<CameraInfo>>(waiters);
                waiters.Clear();
            }
            foreach (var w in wake) {
                w.TrySetResult(info);
            }
        }

        public async Task<CameraInfo> Next(CancellationToken token) {
            var tcs = new TaskCompletionSource<CameraInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync) {
                if (disposed) {
                    throw new ObjectDisposedException(nameof(TemperatureWatch));
                }
                waiters.Add(tcs);
            }
            using (token.Register(() => tcs.TrySetCanceled(token))) {
                return await tcs.Task;
            }
        }

        public double? PeakPower {
            get {
                lock (sync) {
                    return peakPower;
                }
            }
        }

        public void BeginHold(double setpoint, double sensor, double power) {
            lock (sync) {
                holdSetpoint = setpoint;
                maxDeviation = double.IsNaN(sensor) ? 0 : Math.Abs(sensor - setpoint);
                peakPower = Peak(null, power);
            }
        }

        public double EndHold(double sensor, double power) {
            lock (sync) {
                if (holdSetpoint.HasValue && !double.IsNaN(sensor)) {
                    maxDeviation = Math.Max(maxDeviation, Math.Abs(sensor - holdSetpoint.Value));
                }
                if (holdSetpoint.HasValue) {
                    peakPower = Peak(peakPower, power);
                }
                holdSetpoint = null;
                return maxDeviation;
            }
        }

        private static double? Peak(double? peak, double power) {
            if (double.IsNaN(power)) {
                return peak;
            }
            return peak.HasValue ? Math.Max(peak.Value, power) : power;
        }

        public void Dispose() {
            List<TaskCompletionSource<CameraInfo>> wake;
            lock (sync) {
                if (disposed) {
                    return;
                }
                disposed = true;
                wake = new List<TaskCompletionSource<CameraInfo>>(waiters);
                waiters.Clear();
            }
            camera.RemoveConsumer(this);
            foreach (var w in wake) {
                w.TrySetCanceled();
            }
        }
    }
}
