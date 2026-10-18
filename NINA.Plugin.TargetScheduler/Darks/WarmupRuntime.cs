using NINA.Core.Model;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Runs the <see cref="WarmupSession"/> in NINA: polls the installer's request every 5 s, holds every light, dark and
    /// bias frame and every other setpoint move while a warm-up runs, and ramps the setpoint by <see cref="CoolerRamp"/>.
    /// </summary>
    public static class WarmupRuntime {
        public const string Log = WarmupSession.Log;
        public const string Owner = "warm-up";
        public static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);

        private static readonly object sync = new object();
        private static WarmupSession session;
        private static Timer timer;
        private static int ticking;
        private static CancellationTokenSource began = new CancellationTokenSource();

        public static bool Active => session?.Active == true;

        public static string Reason => session?.Reason ?? "the camera is warming for a shutdown";

        /// <summary>Canceled when a warm-up begins, so a setpoint move or wait in progress stops and the warm-up's ramp runs alone.</summary>
        public static CancellationToken BeganToken {
            get {
                lock (sync) {
                    return began.Token;
                }
            }
        }

        public static void Start(ICameraMediator cameraMediator, Func<double?> ambient) {
            lock (sync) {
                if (timer != null || cameraMediator == null) {
                    return;
                }
                session = new WarmupSession(Common.PLUGIN_HOME, new Camera(cameraMediator), ambient);
                session.Began += () => {
                    lock (sync) {
                        began.Cancel();
                    }
                };
                session.Ended += () => {
                    lock (sync) {
                        if (began.IsCancellationRequested) {
                            began = new CancellationTokenSource();
                        }
                    }
                };
                session.ClearLeftovers();
                timer = new Timer(_ => Tick(), null, Poll, Poll);
            }
        }

        public static void Stop() {
            lock (sync) {
                timer?.Dispose();
                timer = null;
            }
        }

        private static void Tick() {
            if (Interlocked.Exchange(ref ticking, 1) == 1) {
                return;
            }
            try {
                session?.Tick();
            } catch (Exception ex) {
                TSLogger.Warning($"{Log}: {ex.Message}");
            } finally {
                Interlocked.Exchange(ref ticking, 0);
            }
        }

        /// <summary>Holds the caller while a warm-up runs, reporting why; returns at once otherwise.</summary>
        public static async Task WaitWhileActive(string what, IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!Active) {
                return;
            }
            TSLogger.Info($"{Log}: holding {what}: {Reason}");
            try {
                while (Active) {
                    progress?.Report(new ApplicationStatus { Status = $"Target Scheduler: {Reason}" });
                    await Task.Delay(Poll, token);
                }
            } finally {
                progress?.Report(new ApplicationStatus { Status = "" });
            }
            TSLogger.Info($"{Log}: the warm-up ended, {what} goes on");
        }

        private sealed class Camera : IWarmupCamera {
            private readonly ICameraMediator camera;

            public Camera(ICameraMediator camera) {
                this.camera = camera;
            }

            public WarmupReading Read() {
                CameraInfo i = camera.GetInfo();
                return i == null
                    ? new WarmupReading()
                    : new WarmupReading { Connected = i.Connected, CanSetTemperature = i.CanSetTemperature, CoolerOn = i.CoolerOn, Sensor = i.Temperature, Setpoint = i.TemperatureSetPoint, Power = i.CoolerPower };
            }

            public Task<bool> StartRamp(double target, double cPerMinute) {
                return Task.Run(async () => {
                    await CoolerRamp.Stop(Log, Owner);
                    return await CoolerRamp.StartBackground(camera, target, Owner, Log, null, cPerMinute);
                });
            }

            public void StopRamp() {
                _ = CoolerRamp.Stop(Log, Owner);
            }
        }
    }
}
