using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public class LiveSample {
        public bool Present { get; set; }
        public object Identity { get; set; }
        public List<AutofocusPoint> Points { get; set; } = new List<AutofocusPoint>();
        public string Filter { get; set; }
    }

    public static class PointSnapshot {

        public static List<AutofocusPoint> Copy<T>(IList<T> source, Func<T, AutofocusPoint> map, int max) {
            var copy = new List<AutofocusPoint>();
            if (source == null || map == null) {
                return copy;
            }
            int count;
            try {
                count = source.Count;
            } catch (Exception) {
                return copy;
            }
            for (int i = 0; i < count && copy.Count < max; i++) {
                T item;
                try {
                    if (i >= source.Count) {
                        break;
                    }
                    item = source[i];
                } catch (Exception) {
                    break;
                }
                if (item == null) {
                    continue;
                }
                var p = map(item);
                if (p == null || !Finite(p.Position) || !Finite(p.Value)) {
                    continue;
                }
                if (p.Error.HasValue && !Finite(p.Error.Value)) {
                    p.Error = null;
                }
                copy.Add(p);
            }
            return copy;
        }

        private static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }

    public class LiveAutofocusExpectation {
        public Version[] Nina { get; set; } = { new Version(3, 1, 2, 9001), new Version(3, 2, 0, 9001) };
        public Version HocusFocus { get; set; } = new Version(3, 0, 0, 21);
        public string HocusFocusAssembly { get; set; } = "NINA.Joko.Plugins.HocusFocus";
        public string ViewModelType { get; set; } = "NINA.Joko.Plugins.HocusFocus.AutoFocus.HocusFocusVM";
        public string ViewModelInterface { get; set; } = "NINA.WPF.Base.Interfaces.ViewModel.IAutoFocusVM";
        public string CollectionType { get; set; } = "NINA.Core.Utility.AsyncObservableCollection`1";
        public string PointType { get; set; } = "OxyPlot.Series.ScatterErrorPoint";

        public static readonly LiveAutofocusExpectation Default = new LiveAutofocusExpectation();
    }

    public static class LiveAutofocusGate {
        public const string HocusFocusNotLoaded = "Hocus Focus is not loaded";

        public static string Check(Version nina, Version hocusFocus, Type viewModel, Type viewModelInterface, LiveAutofocusExpectation expect = null) {
            expect ??= LiveAutofocusExpectation.Default;
            if (nina == null || Array.IndexOf(expect.Nina, nina) < 0) {
                return $"NINA is {nina?.ToString() ?? "unknown"}, live autofocus needs one of {string.Join(", ", expect.Nina.Select(v => v.ToString()))}";
            }
            if (hocusFocus == null) {
                return HocusFocusNotLoaded;
            }
            if (hocusFocus != expect.HocusFocus) {
                return $"Hocus Focus is {hocusFocus}, live autofocus needs exactly {expect.HocusFocus}";
            }
            if (viewModel == null || viewModel.FullName != expect.ViewModelType) {
                return $"type {expect.ViewModelType} not found";
            }
            if (viewModelInterface == null || viewModelInterface.FullName != expect.ViewModelInterface || !viewModelInterface.IsInterface) {
                return $"interface {expect.ViewModelInterface} not found";
            }
            if (!viewModelInterface.IsAssignableFrom(viewModel)) {
                return $"{expect.ViewModelType} does not implement {expect.ViewModelInterface}";
            }
            var points = viewModelInterface.GetProperty("FocusPoints", BindingFlags.Public | BindingFlags.Instance);
            if (points == null || points.GetGetMethod() == null) {
                return $"{expect.ViewModelInterface}.FocusPoints not found";
            }
            var pt = points.PropertyType;
            if (!pt.IsGenericType || pt.GetGenericTypeDefinition().FullName != expect.CollectionType) {
                return $"{expect.ViewModelInterface}.FocusPoints is {pt.FullName}, expected {expect.CollectionType}";
            }
            var item = pt.GetGenericArguments()[0];
            if (item.FullName != expect.PointType) {
                return $"FocusPoints holds {item.FullName}, expected {expect.PointType}";
            }
            if (!typeof(IList<>).MakeGenericType(item).IsAssignableFrom(pt)) {
                return $"FocusPoints is not an IList<{item.Name}>";
            }
            foreach (var name in new[] { "X", "Y", "ErrorY" }) {
                var p = item.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || p.PropertyType != typeof(double) || p.GetGetMethod() == null) {
                    return $"{expect.PointType}.{name} is not a readable double";
                }
            }
            var inProgress = viewModel.GetProperty("AutoFocusInProgress", BindingFlags.Public | BindingFlags.Instance);
            if (inProgress == null || inProgress.PropertyType != typeof(bool) || inProgress.GetGetMethod() == null) {
                return $"{expect.ViewModelType}.AutoFocusInProgress is not a readable bool";
            }
            var own = viewModel.GetProperty("FocusPoints", BindingFlags.Public | BindingFlags.Instance);
            if (own == null || own.PropertyType != pt || own.GetGetMethod() == null) {
                return $"{expect.ViewModelType}.FocusPoints not found";
            }
            return null;
        }
    }

    public class LiveAutofocusTracker {
        private readonly object sync = new object();
        private object identity;
        private StatusAutofocus run;

        public bool Active {
            get {
                lock (sync) {
                    return identity != null;
                }
            }
        }

        public bool Observe(LiveSample sample, DateTime nowUtc) {
            lock (sync) {
                if (sample == null || !sample.Present || sample.Identity == null) {
                    if (identity == null) {
                        return false;
                    }
                    identity = null;
                    run = null;
                    return true;
                }
                if (!ReferenceEquals(sample.Identity, identity)) {
                    identity = sample.Identity;
                    run = new StatusAutofocus {
                        RunId = Guid.NewGuid().ToString(),
                        State = AutofocusStates.Running,
                        StartedAt = nowUtc,
                        Filter = sample.Filter,
                        Points = Points(sample),
                    };
                    return true;
                }
                var changed = false;
                if (!string.IsNullOrEmpty(sample.Filter) && sample.Filter != run.Filter) {
                    run.Filter = sample.Filter;
                    changed = true;
                }
                var points = Points(sample);
                if (!Same(points, run.Points)) {
                    run.Points = points;
                    changed = true;
                }
                return changed;
            }
        }

        public bool Reset() {
            lock (sync) {
                var had = identity != null;
                identity = null;
                run = null;
                return had;
            }
        }

        public StatusAutofocus Snapshot() {
            lock (sync) {
                if (run == null) {
                    return null;
                }
                return new StatusAutofocus {
                    RunId = run.RunId,
                    State = run.State,
                    StartedAt = run.StartedAt,
                    Filter = run.Filter,
                    Points = run.Points.Select(p => new AutofocusPoint { Position = p.Position, Value = p.Value, Error = p.Error }).ToList(),
                };
            }
        }

        private static List<AutofocusPoint> Points(LiveSample s) {
            return (s.Points ?? new List<AutofocusPoint>()).Take(AutofocusRuns.MaxPoints)
                .Select(p => new AutofocusPoint { Position = p.Position, Value = p.Value, Error = p.Error }).ToList();
        }

        private static bool Same(List<AutofocusPoint> a, List<AutofocusPoint> b) {
            if (a.Count != b.Count) {
                return false;
            }
            for (int i = 0; i < a.Count; i++) {
                if (a[i].Position != b[i].Position || a[i].Value != b[i].Value || a[i].Error != b[i].Error) {
                    return false;
                }
            }
            return true;
        }
    }

    public class LiveAutofocusPoller {
        public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan Stop = System.Threading.Timeout.InfiniteTimeSpan;

        private readonly Func<bool> switchedOn;
        private readonly Func<string> gate;
        private readonly Func<LiveSample> sample;
        private readonly Action<string> warn;
        private readonly Action changed;
        private readonly Func<DateTime> now;
        private readonly LiveAutofocusTracker tracker = new LiveAutofocusTracker();
        public const int GateAttempts = 120;
        private bool gated;
        private int gateAttempts;
        private volatile bool disabled;

        public LiveAutofocusPoller(Func<bool> switchedOn, Func<string> gate, Func<LiveSample> sample, Action<string> warn, Action changed, Func<DateTime> now = null) {
            this.switchedOn = switchedOn;
            this.gate = gate;
            this.sample = sample;
            this.warn = warn;
            this.changed = changed;
            this.now = now ?? (() => DateTime.UtcNow);
        }

        public bool Disabled => disabled;

        public bool Active => !disabled && tracker.Active;

        public StatusAutofocus Snapshot() => disabled ? null : tracker.Snapshot();

        public TimeSpan Tick() {
            if (disabled) {
                return Stop;
            }
            try {
                if (!switchedOn()) {
                    if (tracker.Reset()) {
                        Changed();
                    }
                    return IdleInterval;
                }
                if (!gated) {
                    string reason;
                    try {
                        reason = gate();
                    } catch (Exception e) {
                        reason = $"the check failed: {e.Message}";
                    }
                    if (reason == LiveAutofocusGate.HocusFocusNotLoaded && ++gateAttempts < GateAttempts) {
                        return IdleInterval;
                    }
                    if (reason != null) {
                        Disable($"live autofocus points are off for this session: {reason}");
                        return Stop;
                    }
                    gated = true;
                }
                var s = sample();
                if (s != null && tracker.Observe(s, now())) {
                    Changed();
                }
                return tracker.Active ? ActiveInterval : IdleInterval;
            } catch (Exception e) {
                Disable($"live autofocus points are off for this session after an error: {e.GetBaseException().Message}");
                return Stop;
            }
        }

        private void Disable(string message) {
            disabled = true;
            try {
                warn?.Invoke(message);
            } catch (Exception) {
            }
            if (tracker.Reset()) {
                Changed();
            }
        }

        private void Changed() {
            try {
                changed?.Invoke();
            } catch (Exception) {
            }
        }
    }
}
