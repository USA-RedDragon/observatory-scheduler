using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public sealed class LiveAutofocusProbe {
        public const string KillSwitchVariable = "OBSERVATORY_SCHEDULER_LIVE_AUTOFOCUS";
        private static readonly TimeSpan FirstTick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(2);

        private readonly IFilterWheelMediator filterWheel;
        private readonly Func<bool> settingOn;
        private readonly LiveAutofocusPoller poller;
        private readonly object timerLock = new object();
        private Timer timer;
        private Type viewModelType;
        private PropertyInfo inProgress;
        private volatile bool stopped;

        public LiveAutofocusProbe(IFilterWheelMediator filterWheel, Func<bool> settingOn, Action changed) {
            this.filterWheel = filterWheel;
            this.settingOn = settingOn;
            poller = new LiveAutofocusPoller(SwitchedOn, Gate, Sample, m => TSLogger.Warning(m), changed);
        }

        public static bool EnvironmentAllows() {
            var v = Environment.GetEnvironmentVariable(KillSwitchVariable);
            return string.IsNullOrWhiteSpace(v) || !(v.Trim().Equals("off", StringComparison.OrdinalIgnoreCase) || v.Trim() == "0" || v.Trim().Equals("false", StringComparison.OrdinalIgnoreCase));
        }

        public StatusAutofocus Snapshot() {
            try {
                return poller.Snapshot();
            } catch (Exception) {
                return null;
            }
        }

        public void Start() {
            lock (timerLock) {
                timer = new Timer(_ => Run(), null, FirstTick, Timeout.InfiniteTimeSpan);
            }
        }

        public void Stop() {
            stopped = true;
            lock (timerLock) {
                timer?.Dispose();
                timer = null;
            }
        }

        private void Run() {
            TimeSpan next;
            try {
                next = stopped ? LiveAutofocusPoller.Stop : poller.Tick();
            } catch (Exception) {
                next = LiveAutofocusPoller.Stop;
            }
            lock (timerLock) {
                if (stopped || timer == null || next == LiveAutofocusPoller.Stop) {
                    return;
                }
                try {
                    timer.Change(next, Timeout.InfiniteTimeSpan);
                } catch (ObjectDisposedException) {
                }
            }
        }

        private bool SwitchedOn() {
            return EnvironmentAllows() && settingOn();
        }

        private string Gate() {
            var nina = Version.TryParse(NINA.Core.Utility.CoreUtil.Version, out var v) ? v : null;
            var hf = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == LiveAutofocusExpectation.Default.HocusFocusAssembly);
            var vmType = hf?.GetType(LiveAutofocusExpectation.Default.ViewModelType, false);
            var reason = LiveAutofocusGate.Check(nina, hf?.GetName().Version, vmType, typeof(IAutoFocusVM));
            if (reason == null) {
                inProgress = vmType.GetProperty("AutoFocusInProgress", BindingFlags.Public | BindingFlags.Instance);
                viewModelType = vmType;
                TSLogger.Info($"live autofocus points on: NINA {nina}, Hocus Focus {hf.GetName().Version}, watching {vmType.FullName} windows");
            }
            return reason;
        }

        private LiveSample Sample() {
            var app = Application.Current;
            var dispatcher = app?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || stopped) {
                return new LiveSample();
            }
            var op = dispatcher.InvokeAsync(ReadOnUiThread, DispatcherPriority.Background);
            bool done;
            try {
                done = op.Task.Wait(UiTimeout);
            } catch (AggregateException) when (stopped || dispatcher.HasShutdownStarted || op.Status == DispatcherOperationStatus.Aborted) {
                return new LiveSample();
            }
            if (!done) {
                op.Abort();
                return null;
            }
            var sample = op.Result;
            if (sample.Present) {
                sample.Filter = filterWheel?.GetInfo()?.SelectedFilter?.Name;
            }
            return sample;
        }

        private LiveSample ReadOnUiThread() {
            var app = Application.Current;
            if (app == null) {
                return new LiveSample();
            }
            LiveSample found = null;
            foreach (Window w in app.Windows) {
                var content = w.Content;
                if (content == null || content.GetType() != viewModelType || content is not IAutoFocusVM vm) {
                    continue;
                }
                if (!(bool)inProgress.GetValue(content)) {
                    continue;
                }
                found = new LiveSample {
                    Present = true,
                    Identity = content,
                    Points = PointSnapshot.Copy(vm.FocusPoints, p => new AutofocusPoint { Position = p.X, Value = p.Y, Error = p.ErrorY }, AutofocusRuns.MaxPoints),
                };
            }
            return found ?? new LiveSample();
        }
    }
}
