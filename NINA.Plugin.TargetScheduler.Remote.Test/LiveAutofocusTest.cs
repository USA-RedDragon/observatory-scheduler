using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Core.Utility {

    public class AsyncObservableCollection<T> : ObservableCollection<T> {
    }
}

namespace OxyPlot.Series {

    public class ScatterErrorPoint {

        public ScatterErrorPoint(double x, double y, double errorX, double errorY) {
            X = x;
            Y = y;
            ErrorX = errorX;
            ErrorY = errorY;
        }

        public double X { get; }
        public double Y { get; }
        public double ErrorX { get; }
        public double ErrorY { get; }
    }
}

namespace NINA.WPF.Base.Interfaces.ViewModel {

    public interface IAutoFocusVM {
        NINA.Core.Utility.AsyncObservableCollection<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; }
        TimeSpan AutoFocusDuration { get; set; }
    }
}

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class HocusFocusVM : NINA.WPF.Base.Interfaces.ViewModel.IAutoFocusVM {
        public NINA.Core.Utility.AsyncObservableCollection<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; } = new NINA.Core.Utility.AsyncObservableCollection<OxyPlot.Series.ScatterErrorPoint>();
        public TimeSpan AutoFocusDuration { get; set; }
        public bool AutoFocusInProgress { get; private set; }
    }
}

namespace NINA.Plugin.TargetScheduler.Remote.Test.LiveFakes {

    public class WrongPoint {
        public double X { get; set; }
        public double Y { get; set; }
        public float ErrorY { get; set; }
    }

    public interface IWrongPoints {
        NINA.Core.Utility.AsyncObservableCollection<WrongPoint> FocusPoints { get; set; }
        TimeSpan AutoFocusDuration { get; set; }
    }

    public class WrongPointsVM : IWrongPoints {
        public NINA.Core.Utility.AsyncObservableCollection<WrongPoint> FocusPoints { get; set; }
        public TimeSpan AutoFocusDuration { get; set; }
    }

    public interface INoDuration {
        NINA.Core.Utility.AsyncObservableCollection<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; }
    }

    public class NoDurationVM : INoDuration {
        public NINA.Core.Utility.AsyncObservableCollection<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; }
    }

    public interface IListPoints {
        List<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; }
        TimeSpan AutoFocusDuration { get; set; }
    }

    public class ListPointsVM : IListPoints {
        public List<OxyPlot.Series.ScatterErrorPoint> FocusPoints { get; set; }
        public TimeSpan AutoFocusDuration { get; set; }
    }

    public class Unrelated {
    }
}

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    using NINA.Joko.Plugins.HocusFocus.AutoFocus;
    using NINA.Plugin.TargetScheduler.Remote.Test.LiveFakes;
    using NINA.WPF.Base.Interfaces.ViewModel;
    using OxyPlot.Series;

    [TestFixture]
    public class LiveAutofocusTest {
        private static readonly Version Nina = new Version(3, 1, 2, 9001);
        private static readonly Version Hf = new Version(3, 0, 0, 21);

        private static LiveAutofocusExpectation Expect(Type vm, Type iface) {
            return new LiveAutofocusExpectation { ViewModelType = vm.FullName, ViewModelInterface = iface.FullName };
        }

        [Test]
        public void GatePassesOnTheExactVersionsAndMembers() {
            LiveAutofocusGate.Check(Nina, Hf, typeof(HocusFocusVM), typeof(IAutoFocusVM)).Should().BeNull();
        }

        [Test]
        public void GateRefusesOtherVersions() {
            LiveAutofocusGate.Check(new Version(3, 2, 0, 2001), Hf, typeof(HocusFocusVM), typeof(IAutoFocusVM)).Should().Contain("NINA is 3.2.0.2001");
            LiveAutofocusGate.Check(null, Hf, typeof(HocusFocusVM), typeof(IAutoFocusVM)).Should().Contain("NINA is unknown");
            LiveAutofocusGate.Check(Nina, new Version(3, 0, 0, 22), typeof(HocusFocusVM), typeof(IAutoFocusVM)).Should().Contain("Hocus Focus is 3.0.0.22");
            LiveAutofocusGate.Check(Nina, null, typeof(HocusFocusVM), typeof(IAutoFocusVM)).Should().Be(LiveAutofocusGate.HocusFocusNotLoaded);
        }

        [Test]
        public void GateRefusesMissingOrWrongMembers() {
            LiveAutofocusGate.Check(Nina, Hf, null, typeof(IAutoFocusVM)).Should().Contain("not found");
            LiveAutofocusGate.Check(Nina, Hf, typeof(Unrelated), typeof(IAutoFocusVM)).Should().Contain("not found");
            LiveAutofocusGate.Check(Nina, Hf, typeof(HocusFocusVM), null).Should().Contain("not found");
            LiveAutofocusGate.Check(Nina, Hf, typeof(Unrelated), typeof(IAutoFocusVM), Expect(typeof(Unrelated), typeof(IAutoFocusVM))).Should().Contain("does not implement");
            LiveAutofocusGate.Check(Nina, Hf, typeof(WrongPointsVM), typeof(IWrongPoints), Expect(typeof(WrongPointsVM), typeof(IWrongPoints))).Should().Contain("FocusPoints holds");
            var wrongPoint = new LiveAutofocusExpectation { ViewModelType = typeof(WrongPointsVM).FullName, ViewModelInterface = typeof(IWrongPoints).FullName, PointType = typeof(WrongPoint).FullName };
            LiveAutofocusGate.Check(Nina, Hf, typeof(WrongPointsVM), typeof(IWrongPoints), wrongPoint).Should().Contain("ErrorY is not a readable double");
            LiveAutofocusGate.Check(Nina, Hf, typeof(NoDurationVM), typeof(INoDuration), Expect(typeof(NoDurationVM), typeof(INoDuration))).Should().Contain("AutoFocusInProgress is not a readable bool");
            LiveAutofocusGate.Check(Nina, Hf, typeof(ListPointsVM), typeof(IListPoints), Expect(typeof(ListPointsVM), typeof(IListPoints))).Should().Contain("expected NINA.Core.Utility.AsyncObservableCollection`1");
        }

        [Test]
        public void SnapshotCopiesAndDropsBadPoints() {
            var list = new List<ScatterErrorPoint> { new ScatterErrorPoint(4900, 3.1, 0, 0.2), null, new ScatterErrorPoint(5000, double.NaN, 0, 0.1), new ScatterErrorPoint(5100, 2.2, 0, double.PositiveInfinity) };
            var copy = PointSnapshot.Copy(list, p => new AutofocusPoint { Position = p.X, Value = p.Y, Error = p.ErrorY }, 10);
            copy.Should().HaveCount(2);
            copy[0].Error.Should().Be(0.2);
            copy[1].Position.Should().Be(5100);
            copy[1].Error.Should().BeNull();
            PointSnapshot.Copy<ScatterErrorPoint>(null, p => null, 10).Should().BeEmpty();
            FluentActions.Invoking(() => PointSnapshot.Copy(list, p => throw new InvalidOperationException("map"), 10)).Should().Throw<InvalidOperationException>();
            PointSnapshot.Copy(Enumerable.Range(0, 20).Select(i => new ScatterErrorPoint(i, 1, 0, 0)).ToList(), p => new AutofocusPoint { Position = p.X, Value = p.Y }, 5).Should().HaveCount(5);
        }

        [Test]
        public void SnapshotSurvivesConcurrentModification() {
            var list = new List<ScatterErrorPoint>();
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var writer = Task.Run(() => {
                var r = new Random(1);
                while (!stop.IsCancellationRequested) {
                    if (list.Count > 40 || r.Next(10) == 0) {
                        list.Clear();
                    } else {
                        list.Insert(r.Next(list.Count + 1), new ScatterErrorPoint(r.Next(4000, 6000), 1 + r.NextDouble(), 0, 0.1));
                    }
                }
            });
            var copies = 0;
            while (!stop.IsCancellationRequested) {
                var copy = PointSnapshot.Copy(list, p => new AutofocusPoint { Position = p.X, Value = p.Y, Error = p.ErrorY }, 500);
                copy.All(p => p.Position >= 4000 && p.Position < 6000 && p.Value >= 1 && p.Value < 2).Should().BeTrue();
                copies++;
            }
            writer.Wait();
            copies.Should().BeGreaterThan(10);

            var collection = new NINA.Core.Utility.AsyncObservableCollection<ScatterErrorPoint>();
            for (int i = 0; i < 9; i++) {
                collection.Add(new ScatterErrorPoint(4800 + i * 50, 3 - i * 0.1, 0, 0.05));
            }
            PointSnapshot.Copy(collection, p => new AutofocusPoint { Position = p.X, Value = p.Y }, 500).Should().HaveCount(9);
        }

        private static LiveSample Present(object vm, string filter = "L", params double[] xy) {
            var s = new LiveSample { Present = true, Identity = vm, Filter = filter };
            for (int i = 0; i + 1 < xy.Length; i += 2) {
                s.Points.Add(new AutofocusPoint { Position = xy[i], Value = xy[i + 1], Error = 0.1 });
            }
            return s;
        }

        [Test]
        public void TrackerFollowsTheRunningWindow() {
            var t = new LiveAutofocusTracker();
            var at = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
            var vm = new object();
            t.Observe(new LiveSample(), at).Should().BeFalse();
            t.Snapshot().Should().BeNull();

            t.Observe(Present(vm), at).Should().BeTrue();
            t.Active.Should().BeTrue();
            var s = t.Snapshot();
            s.State.Should().Be(AutofocusStates.Running);
            s.StartedAt.Should().Be(at);
            s.Points.Should().BeEmpty();
            var id = s.RunId;

            t.Observe(Present(vm), at.AddSeconds(1)).Should().BeFalse();
            t.Observe(Present(vm, "L", 4900, 3.1), at.AddSeconds(2)).Should().BeTrue();
            t.Observe(Present(vm, null, 4900, 3.1, 5000, 2.4), at.AddSeconds(3)).Should().BeTrue();
            s = t.Snapshot();
            s.RunId.Should().Be(id);
            s.Filter.Should().Be("L");
            s.Points.Select(p => p.Position).Should().Equal(4900, 5000);
            s.Points.Add(new AutofocusPoint());
            t.Snapshot().Points.Should().HaveCount(2);

            t.Observe(Present(vm), at.AddSeconds(4)).Should().BeTrue();
            t.Snapshot().Points.Should().BeEmpty();
            t.Snapshot().RunId.Should().Be(id);
            t.Observe(Present(vm, "L", 4950, 2.8), at.AddSeconds(5)).Should().BeTrue();
            t.Snapshot().Points.Should().HaveCount(1);

            t.Observe(new LiveSample(), at.AddSeconds(6)).Should().BeTrue();
            t.Snapshot().Should().BeNull();
            t.Active.Should().BeFalse();
            t.Observe(new LiveSample(), at.AddSeconds(7)).Should().BeFalse();

            var next = new object();
            t.Observe(Present(next, "Ha"), at.AddSeconds(8)).Should().BeTrue();
            t.Snapshot().RunId.Should().NotBe(id);
            t.Snapshot().StartedAt.Should().Be(at.AddSeconds(8));
            t.Observe(Present(vm), at.AddSeconds(9)).Should().BeTrue();
            t.Snapshot().StartedAt.Should().Be(at.AddSeconds(9));
            t.Reset().Should().BeTrue();
            t.Snapshot().Should().BeNull();
        }

        [Test]
        public void PayloadShape() {
            var t = new LiveAutofocusTracker();
            var vm = new object();
            t.Observe(Present(vm, "Ha", 4900, 3.1, 5000, 2.4), new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc));
            var snap = t.Snapshot();
            var json = JsonSerializer.Serialize(new Status { Autofocus = snap }, Json.Options);
            json.Should().Contain(@"""autofocus"":{""run_id"":""" + snap.RunId + @""",""state"":""running"",""started_at"":""2026-10-12T08:00:00.000Z"",""filter"":""Ha"",""points"":[{""position"":4900,""value"":3.1,""error"":0.1},{""position"":5000,""value"":2.4,""error"":0.1}]}");
        }

        private sealed class Harness {
            public bool On = true;
            public string GateReason;
            public int GateCalls;
            public Func<LiveSample> Next = () => new LiveSample();
            public int Samples;
            public readonly List<string> Warnings = new List<string>();
            public int Changes;
            public LiveAutofocusPoller Poller;

            public Harness() {
                Poller = new LiveAutofocusPoller(() => On, () => { GateCalls++; return GateReason; }, () => { Samples++; return Next(); }, Warnings.Add, () => Changes++);
            }
        }

        [Test]
        public void PollerGatesOnceAndPacesByActivity() {
            var h = new Harness();
            var vm = new object();
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            h.Next = () => Present(vm, "L", 4900, 3.1);
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.ActiveInterval);
            h.Poller.Snapshot().State.Should().Be(AutofocusStates.Running);
            h.Next = () => null;
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.ActiveInterval);
            h.Poller.Snapshot().Should().NotBeNull();
            h.Next = () => new LiveSample();
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            h.Poller.Snapshot().Should().BeNull();
            h.GateCalls.Should().Be(1);
            h.Changes.Should().Be(2);
            h.Warnings.Should().BeEmpty();
        }

        [Test]
        public void FailedGateDisablesWithOneWarning() {
            var h = new Harness { GateReason = "Hocus Focus is 3.0.0.22" };
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.Stop);
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.Stop);
            h.Poller.Disabled.Should().BeTrue();
            h.Samples.Should().Be(0);
            h.GateCalls.Should().Be(1);
            h.Warnings.Should().ContainSingle().Which.Should().Contain("Hocus Focus is 3.0.0.22");

            var throwing = new LiveAutofocusPoller(() => true, () => throw new TypeLoadException("gone"), () => new LiveSample(), h.Warnings.Add, null);
            throwing.Tick().Should().Be(LiveAutofocusPoller.Stop);
            throwing.Disabled.Should().BeTrue();
            h.Warnings.Last().Should().Contain("gone");
        }

        [Test]
        public void GateWaitsForHocusFocusToLoad() {
            var h = new Harness { GateReason = LiveAutofocusGate.HocusFocusNotLoaded };
            for (int i = 1; i < LiveAutofocusPoller.GateAttempts; i++) {
                h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            }
            h.Warnings.Should().BeEmpty();
            h.Samples.Should().Be(0);
            h.GateReason = null;
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            h.Samples.Should().Be(1);
            h.Poller.Disabled.Should().BeFalse();

            var never = new Harness { GateReason = LiveAutofocusGate.HocusFocusNotLoaded };
            for (int i = 1; i < LiveAutofocusPoller.GateAttempts; i++) {
                never.Poller.Tick();
            }
            never.Poller.Tick().Should().Be(LiveAutofocusPoller.Stop);
            never.Warnings.Should().ContainSingle().Which.Should().Contain(LiveAutofocusGate.HocusFocusNotLoaded);
        }

        [Test]
        public void AnErrorDisablesForTheSession() {
            var h = new Harness();
            var vm = new object();
            h.Next = () => Present(vm, "L", 4900, 3.1);
            h.Poller.Tick();
            h.Poller.Snapshot().Should().NotBeNull();
            h.Next = () => throw new AggregateException(new InvalidOperationException("collection was modified"));
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.Stop);
            h.Poller.Disabled.Should().BeTrue();
            h.Poller.Snapshot().Should().BeNull();
            h.Warnings.Should().ContainSingle().Which.Should().Contain("collection was modified");
            var samples = h.Samples;
            h.Next = () => Present(vm);
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.Stop);
            h.Samples.Should().Be(samples);
            h.Warnings.Should().HaveCount(1);
        }

        [Test]
        public void KillSwitchStopsSampling() {
            var h = new Harness { On = false };
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            h.Samples.Should().Be(0);
            h.GateCalls.Should().Be(0);
            h.On = true;
            var vm = new object();
            h.Next = () => Present(vm, "L", 4900, 3.1);
            h.Poller.Tick();
            h.Poller.Snapshot().Should().NotBeNull();
            h.On = false;
            h.Poller.Tick().Should().Be(LiveAutofocusPoller.IdleInterval);
            h.Poller.Snapshot().Should().BeNull();
            h.Samples.Should().Be(1);
        }

        [Test]
        public void ThrowingListenersAreIgnored() {
            var p = new LiveAutofocusPoller(() => true, () => null, () => Present(new object()), m => throw new Exception("log"), () => throw new Exception("push"));
            p.Invoking(x => x.Tick()).Should().NotThrow();
            p.Disabled.Should().BeFalse();
            p.Snapshot().Should().NotBeNull();
        }
    }
}
