using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class EventWindowTest {
        private static readonly DateTime Start = new DateTime(2026, 10, 13, 0, 30, 0, DateTimeKind.Utc);
        private static readonly DateTime End = new DateTime(2026, 10, 13, 6, 0, 0, DateTimeKind.Utc);

        private class Candidate {
            public string Name;
            public TargetWindowRow Window;
            public string Reason;
        }

        private static TargetWindowRow Row(string priority, bool ignoreMoon = false, string guid = "g") {
            return new TargetWindowRow { TargetGuid = guid, StartUtc = Start, EndUtc = End, Priority = priority, IgnoreMoon = ignoreMoon };
        }

        private static Candidate C(string name, string priority) {
            return new Candidate { Name = name, Window = priority == null ? null : Row(priority) };
        }

        private static List<string> Prefer(params Candidate[] candidates) {
            List<Candidate> ready = candidates.ToList();
            EventWindows.Prefer(ready, c => c.Window, (c, reason) => c.Reason = reason);
            return ready.Select(c => c.Name).ToList();
        }

        [Test]
        public void ParsesIsoUtcTimesOnly() {
            TargetWindowTable.TryParseUtc("2026-10-13T00:30:00Z", out DateTime t).Should().BeTrue();
            t.Should().Be(Start);
            t.Kind.Should().Be(DateTimeKind.Utc);
            TargetWindowTable.TryParseUtc(" 2026-10-13T00:30:00.250Z ", out t).Should().BeTrue();
            t.Should().Be(Start.AddMilliseconds(250));
            TargetWindowTable.TryParseUtc("2026-10-12T19:30:00-05:00", out t).Should().BeTrue();
            t.Should().Be(Start);
            TargetWindowTable.TryParseUtc("2026-10-13T00:30Z", out t).Should().BeTrue();
            t.Should().Be(Start);

            foreach (string bad in new[] { null, "", " ", "2026-10-13T00:30:00", "2026-10-13 00:30:00Z", "2026-10-13", "13/10/2026 00:30Z", "2026-02-30T00:00:00Z", "2026-10-13T25:00:00Z", "soon" }) {
                TargetWindowTable.TryParseUtc(bad, out _).Should().BeFalse(bad ?? "null");
            }
        }

        [Test]
        public void FormatsUtcWithoutTrailingZeros() {
            TargetWindowTable.FormatUtc(Start).Should().Be("2026-10-13T00:30:00Z");
            TargetWindowTable.FormatUtc(Start.AddMilliseconds(250)).Should().Be("2026-10-13T00:30:00.25Z");
        }

        [Test]
        public void PriorityNormalizes() {
            TargetWindowRow.NormalPriority(null).Should().Be("normal");
            TargetWindowRow.NormalPriority(" ").Should().Be("normal");
            TargetWindowRow.NormalPriority(" High ").Should().Be("high");
            TargetWindowRow.NormalPriority("normal").Should().Be("normal");
            TargetWindowRow.NormalPriority("urgent").Should().BeNull();
            Row("high").IsHigh.Should().BeTrue();
            Row("normal").IsHigh.Should().BeFalse();
            Row(null).IsHigh.Should().BeFalse();
        }

        [Test]
        public void EligibleOnlyFromStartUntilBeforeEnd() {
            TargetWindowRow w = Row("normal");
            EventWindows.RejectReason(w, Start.AddSeconds(-1)).Should().Be(EventWindows.ReasonBefore);
            EventWindows.RejectReason(w, Start).Should().BeNull();
            EventWindows.RejectReason(w, End.AddSeconds(-1)).Should().BeNull();
            EventWindows.RejectReason(w, End).Should().Be(EventWindows.ReasonOutside);
            EventWindows.RejectReason(w, End.AddDays(3)).Should().Be(EventWindows.ReasonOutside);
            EventWindows.RejectReason(null, End.AddDays(3)).Should().BeNull();
            w.Contains(Start).Should().BeTrue();
            w.Contains(End).Should().BeFalse();
            EventWindows.Ended(w, End.AddSeconds(-1)).Should().BeFalse();
            EventWindows.Ended(w, End).Should().BeTrue();
            EventWindows.Ended(null, End).Should().BeFalse();
        }

        [Test]
        public void LocalPlannerTimesCompareInUtc() {
            TargetWindowRow w = Row("normal");
            DateTime localInside = Start.AddMinutes(1).ToLocalTime();
            DateTime localBefore = Start.AddMinutes(-1).ToLocalTime();
            localInside.Kind.Should().Be(DateTimeKind.Local);
            EventWindows.RejectReason(w, localInside).Should().BeNull();
            EventWindows.RejectReason(w, localBefore).Should().Be(EventWindows.ReasonBefore);
            EventWindows.RejectReason(w, DateTime.SpecifyKind(End.ToLocalTime(), DateTimeKind.Unspecified)).Should().Be(EventWindows.ReasonOutside);
            w.Contains(DateTime.SpecifyKind(Start.AddMinutes(1).ToLocalTime(), DateTimeKind.Unspecified)).Should().BeTrue();
        }

        [Test]
        public void ClipsTheImagingSpanToTheWindow() {
            TargetWindowRow w = Row("high");
            DateTime nightStart = Start.AddHours(-2).ToLocalTime();
            DateTime nightEnd = End.AddHours(5).ToLocalTime();
            var clipped = EventWindows.Clip(w, nightStart, nightEnd);
            clipped.Should().NotBeNull();
            clipped.Value.Start.Should().Be(Start.ToLocalTime());
            clipped.Value.End.Should().Be(End.ToLocalTime());
            clipped.Value.End.Kind.Should().Be(DateTimeKind.Local);

            var inside = EventWindows.Clip(w, Start.AddHours(1), Start.AddHours(2));
            inside.Value.Should().Be((Start.AddHours(1), Start.AddHours(2)));
            inside.Value.Start.Kind.Should().Be(DateTimeKind.Utc);

            DateTime unspecified = DateTime.SpecifyKind(Start.AddHours(-1).ToLocalTime(), DateTimeKind.Unspecified);
            var keepsKind = EventWindows.Clip(w, unspecified, unspecified.AddHours(3));
            keepsKind.Value.Start.Should().Be(DateTime.SpecifyKind(Start.ToLocalTime(), DateTimeKind.Unspecified));
            keepsKind.Value.Start.Kind.Should().Be(DateTimeKind.Unspecified);

            EventWindows.Clip(w, End, End.AddHours(8)).Should().BeNull();
            EventWindows.Clip(w, Start.AddDays(-1), Start.AddDays(-1).AddHours(10)).Should().BeNull();
            EventWindows.Clip(null, nightStart, nightEnd).Value.Should().Be((nightStart, nightEnd));
        }

        [Test]
        public void NormalEventsOnlyFillGaps() {
            var normal = C("event", "normal");
            var dso = C("dso", null);
            Prefer(normal, dso).Should().Equal("dso");
            normal.Reason.Should().Be(EventWindows.ReasonGapsOnly);
            dso.Reason.Should().BeNull();

            Prefer(C("event", "normal")).Should().Equal("event");
            Prefer(C("e1", "normal"), C("e2", "normal")).Should().Equal("e1", "e2");
        }

        [Test]
        public void HighEventsGoAheadOfEverythingElse() {
            var dso = C("dso", null);
            var normal = C("normal", "normal");
            var high = C("high", "high");
            Prefer(dso, normal, high).Should().Equal("high");
            dso.Reason.Should().Be(EventWindows.ReasonHighEvent);
            normal.Reason.Should().Be(EventWindows.ReasonHighEvent);
            high.Reason.Should().BeNull();

            Prefer(C("h1", "high"), C("dso", null), C("h2", "high")).Should().Equal("h1", "h2");
            Prefer(C("normal", "normal"), C("high", "high")).Should().Equal("high");
        }

        [Test]
        public void HighEventsInterruptAtTheBoundaryButNotAHighEventAsNearItsMiddle() {
            DateTime inside = Start.AddHours(1);
            EventWindows.Preempts(null, Row("high"), inside).Should().BeTrue();
            EventWindows.Preempts(Row("normal"), Row("high"), inside).Should().BeTrue();
            EventWindows.Preempts(Row("high"), Row("high"), inside).Should().BeFalse();
            EventWindows.Preempts(null, Row("normal"), inside).Should().BeFalse();
            EventWindows.Preempts(null, null, inside).Should().BeFalse();
            EventWindows.Preempts(null, Row("high"), Start.AddSeconds(-1)).Should().BeFalse();
            EventWindows.Preempts(null, Row("high"), End).Should().BeFalse();
        }

        private static TargetWindowRow Around(DateTime middle, double halfHours, string priority = "high") {
            return new TargetWindowRow { TargetGuid = "g", StartUtc = middle.AddHours(-halfHours), EndUtc = middle.AddHours(halfHours), Priority = priority };
        }

        [Test]
        public void PhaseIsTheDistanceFromTheMiddleOverHalfTheWindow() {
            TargetWindowRow w = Row("high");
            DateTime middle = Start.AddHours(2.75);
            EventWindows.Phase(w, middle).Should().BeApproximately(0, 1e-9);
            EventWindows.Phase(w, Start).Should().BeApproximately(1, 1e-9);
            EventWindows.Phase(w, End).Should().BeApproximately(1, 1e-9);
            EventWindows.Phase(w, middle.AddHours(-1.375)).Should().BeApproximately(0.5, 1e-9);
            EventWindows.Phase(w, middle.AddHours(1.375)).Should().BeApproximately(0.5, 1e-9);
            EventWindows.Phase(w, middle.ToLocalTime()).Should().BeApproximately(0, 1e-9);
            EventWindows.Phase(null, middle).Should().Be(double.PositiveInfinity);
        }

        [Test]
        public void TheHighEventNearestItsMiddleIsPreferred() {
            DateTime at = new DateTime(2026, 10, 20, 1, 16, 0, DateTimeKind.Utc);
            var garlic = new Candidate { Name = "21142", Window = Around(new DateTime(2026, 10, 20, 0, 54, 30, DateTimeKind.Utc), 12.49) };
            var cygnus = new Candidate { Name = "6014", Window = Around(new DateTime(2026, 10, 20, 5, 28, 30, DateTimeKind.Utc), 6.64) };
            var dso = C("dso", null);
            List<Candidate> ready = new List<Candidate> { cygnus, dso, garlic };
            EventWindows.Prefer(ready, c => c.Window, (c, r) => c.Reason = r, null, at);
            ready.Select(c => c.Name).Should().Equal("21142");
            cygnus.Reason.Should().Be(EventWindows.ReasonNearerMiddle);
            dso.Reason.Should().Be(EventWindows.ReasonHighEvent);
            EventWindows.IsPriorityReason(EventWindows.ReasonNearerMiddle).Should().BeTrue();
            EventWindows.IsPriorityReason(EventWindows.ReasonRunningHighEvent).Should().BeTrue();
        }

        [Test]
        public void HighEventsWithinThePhaseBandGoOnToTheScore() {
            DateTime at = Start.AddHours(2.75);
            var a = new Candidate { Name = "a", Window = Around(at.AddHours(0.2), 4) };
            var b = new Candidate { Name = "b", Window = Around(at.AddHours(-0.3), 4) };
            var c = new Candidate { Name = "c", Window = Around(at.AddHours(1.2), 4) };
            List<Candidate> ready = new List<Candidate> { a, b, c };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r, null, at);
            ready.Select(x => x.Name).Should().Equal("a", "b");
            c.Reason.Should().Be(EventWindows.ReasonNearerMiddle);
        }

        [Test]
        public void WithoutATimeHighEventsAreNotSplitByPhase() {
            var a = new Candidate { Name = "a", Window = Around(Start, 4) };
            var b = new Candidate { Name = "b", Window = Around(Start.AddHours(3), 4) };
            List<Candidate> ready = new List<Candidate> { a, b };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r);
            ready.Select(x => x.Name).Should().Equal("a", "b");
        }

        [Test]
        public void NormalEventsAreNotSplitByPhase() {
            DateTime at = Start.AddHours(1);
            var a = new Candidate { Name = "a", Window = Around(at, 4, "normal") };
            var b = new Candidate { Name = "b", Window = Around(at.AddHours(3.5), 4, "normal") };
            var dso = C("dso", null);
            List<Candidate> ready = new List<Candidate> { a, b };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r, x => ReferenceEquals(x, b), at);
            ready.Select(x => x.Name).Should().Equal("a", "b");
            ready = new List<Candidate> { a, dso, b };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r, x => ReferenceEquals(x, b), at);
            ready.Select(x => x.Name).Should().Equal("dso");
        }

        [Test]
        public void ARunningHighEventHoldsUntilAnotherIsClearlyNearerItsMiddle() {
            DateTime at = Start.AddHours(2.75);
            var running = new Candidate { Name = "running", Window = Around(at.AddHours(1.6), 4) };
            var near = new Candidate { Name = "near", Window = Around(at.AddHours(1.45), 4) };
            List<Candidate> ready = new List<Candidate> { near, running };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r, x => ReferenceEquals(x, running), at);
            ready.Select(x => x.Name).Should().Equal("running");
            near.Reason.Should().Be(EventWindows.ReasonRunningHighEvent);
            EventWindows.Preempts(running.Window, near.Window, at).Should().BeFalse();

            near.Reason = null;
            var clear = new Candidate { Name = "clear", Window = Around(at.AddHours(1.3), 4) };
            ready = new List<Candidate> { running, near, clear };
            EventWindows.Prefer(ready, x => x.Window, (x, r) => x.Reason = r, x => ReferenceEquals(x, running), at);
            ready.Select(x => x.Name).Should().Equal("near", "clear");
            running.Reason.Should().Be(EventWindows.ReasonNearerMiddle);
            EventWindows.Preempts(running.Window, clear.Window, at).Should().BeTrue();
            EventWindows.Preempts(clear.Window, running.Window, at).Should().BeFalse();
            EventWindows.Preempts(near.Window, running.Window, at).Should().BeFalse();
        }

        private class Eclipse {
            public int Id;
            public double Score;
            public TargetWindowRow Window;
        }

        private static List<(DateTime At, int Id)> Night(DateTime dusk, DateTime end, params Eclipse[] eclipses) {
            List<(DateTime, int)> picks = new List<(DateTime, int)>();
            Eclipse running = null;
            DateTime minimumEnd = DateTime.MinValue;
            for (DateTime at = dusk.AddMinutes(-30); at < end; at = at.AddMinutes(5)) {
                if (at < dusk) { continue; }
                List<Eclipse> ready = eclipses.Where(e => e.Window.Contains(at)).ToList();
                bool preempted = running != null && ready.Any(e => e != running && EventWindows.Preempts(running.Window, e.Window, at));
                if (running == null || at >= minimumEnd || preempted || !ready.Contains(running)) {
                    Eclipse previous = running;
                    EventWindows.Prefer(ready, e => e.Window, null, e => ReferenceEquals(e, previous), at);
                    Eclipse best = ready.OrderByDescending(e => e.Score).First();
                    running = ready.Contains(previous) && best.Score <= previous.Score * 1.01 ? previous : best;
                    if (!ReferenceEquals(running, previous)) { minimumEnd = at.AddMinutes(30); }
                }
                picks.Add((at, running.Id));
            }
            return picks;
        }

        private static TargetWindowRow Utc(string start, string end) {
            return new TargetWindowRow { TargetGuid = "g", StartUtc = DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal), EndUtc = DateTime.Parse(end, null, System.Globalization.DateTimeStyles.AdjustToUniversal), Priority = "high" };
        }

        [Test]
        public void TonightsOverlappingEclipsesAreTakenNearestTheirMiddles() {
            var garlic = new Eclipse { Id = 21142, Score = 1.52, Window = Utc("2026-10-19T12:25:00Z", "2026-10-20T13:24:00Z") };
            var cygnus = new Eclipse { Id = 6014, Score = 1.64, Window = Utc("2026-10-19T22:50:00Z", "2026-10-20T12:07:00Z") };
            DateTime dusk = new DateTime(2026, 10, 20, 1, 16, 0, DateTimeKind.Utc);
            DateTime garlicEclipseEnd = new DateTime(2026, 10, 20, 4, 58, 0, DateTimeKind.Utc);
            DateTime cygnusMiddle = new DateTime(2026, 10, 20, 5, 28, 0, DateTimeKind.Utc);

            List<(DateTime At, int Id)> picks = Night(dusk, new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc), garlic, cygnus);

            picks.First().Should().Be((dusk, 21142));
            List<int> runs = picks.Select(p => p.Id).Where((id, i) => i == 0 || id != picks[i - 1].Id).ToList();
            runs.Should().Equal(21142, 6014);
            DateTime switched = picks.First(p => p.Id == 6014).At;
            switched.Should().BeOnOrAfter(new DateTime(2026, 10, 20, 3, 30, 0, DateTimeKind.Utc));
            switched.Should().BeOnOrBefore(new DateTime(2026, 10, 20, 4, 15, 0, DateTimeKind.Utc));
            switched.Should().BeBefore(garlicEclipseEnd);
            switched.Should().BeBefore(cygnusMiddle);
        }

        [Test]
        public void TwoEclipsesWithTheSameMiddleNeverFlipFlop() {
            DateTime dusk = new DateTime(2026, 10, 20, 1, 16, 0, DateTimeKind.Utc);
            DateTime middle = dusk.AddHours(3);
            var a = new Eclipse { Id = 1, Score = 1.0, Window = Around(middle, 6) };
            var b = new Eclipse { Id = 2, Score = 1.5, Window = Around(middle.AddMinutes(10), 6.2) };
            List<(DateTime At, int Id)> picks = Night(dusk, dusk.AddHours(8), a, b);
            picks.Select(p => p.Id).Distinct().Should().HaveCount(1);
        }

        private class Running {
            public int Id;
            public string Project;
            public TargetWindowRow Window;
        }

        [Test]
        public void PreemptingCandidatesIncludeEventsInTheRunningTargetsProject() {
            DateTime inside = Start.AddHours(1);
            var dso = new Running { Id = 1, Project = "p" };
            var sameProjectEvent = new Running { Id = 2, Project = "p", Window = Row("high") };
            var otherEvent = new Running { Id = 3, Project = "q", Window = Row("high") };
            var normal = new Running { Id = 4, Project = "q", Window = Row("normal") };
            var reloadedDso = new Running { Id = 1, Project = "p" };
            var all = new[] { dso, sameProjectEvent, otherEvent, normal, reloadedDso };

            var found = EventWindows.Preempting(all, dso, r => r.Id, r => r.Window, inside);
            found.Should().Equal(sameProjectEvent, otherEvent);
            found.Select(r => r.Project).Distinct().Should().Equal("p", "q");

            EventWindows.Preempting(all, reloadedDso, r => r.Id, r => r.Window, inside).Should().Equal(sameProjectEvent, otherEvent);
            EventWindows.Preempting(all, sameProjectEvent, r => r.Id, r => r.Window, inside).Should().BeEmpty();
            EventWindows.Preempting(all, dso, r => r.Id, r => r.Window, End).Should().BeEmpty();
            EventWindows.Preempting(all, null, r => r.Id, r => r.Window, inside).Should().BeEmpty();
            EventWindows.Preempting(null, dso, r => r.Id, r => r.Window, inside).Should().BeEmpty();
        }

        [Test]
        public void AWindowShorterThanTheProjectMinimumIsStillImaged() {
            TargetWindowRow w = Row("normal");
            DateTime spanStart = Start;
            DateTime spanEnd = Start.AddMinutes(20);
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, Start.AddMinutes(-30)).Should().Be(1200);
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, Start).Should().Be(1200);
            EventWindows.MinimumSeconds(w, 600, spanStart, spanEnd, Start).Should().Be(600);
            EventWindows.MinimumSeconds(null, 3600, spanStart, spanEnd, Start).Should().Be(3600);
        }

        [Test]
        public void TheLastMinutesOfAWindowAreStillImaged() {
            TargetWindowRow w = Row("normal");
            DateTime spanStart = Start;
            DateTime spanEnd = End;
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, End.AddMinutes(-90)).Should().Be(3600);
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, End.AddMinutes(-15)).Should().Be(900);
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, End.AddSeconds(-10).ToLocalTime()).Should().Be(10);
            EventWindows.MinimumSeconds(w, 3600, spanStart, spanEnd, End).Should().Be(1);
        }

        [Test]
        public void StartsNeverPrecedeTheWindowStart() {
            TargetWindowRow w = Row("normal");
            DateTime sampleBefore = Start.AddSeconds(-10).ToLocalTime();
            DateTime clamped = EventWindows.NotBefore(w, sampleBefore);
            clamped.Should().Be(Start.ToLocalTime());
            clamped.Kind.Should().Be(DateTimeKind.Local);
            EventWindows.NotBefore(w, Start.AddMinutes(5)).Should().Be(Start.AddMinutes(5));
            EventWindows.NotBefore(null, sampleBefore).Should().Be(sampleBefore);
            DateTime unspecified = DateTime.SpecifyKind(sampleBefore, DateTimeKind.Unspecified);
            EventWindows.NotBefore(w, unspecified).Should().Be(DateTime.SpecifyKind(Start.ToLocalTime(), DateTimeKind.Unspecified));
        }

        [Test]
        public void PriorityReasonsAreRecognizedForPreviewDiagnostics() {
            EventWindows.IsPriorityReason(EventWindows.ReasonGapsOnly).Should().BeTrue();
            EventWindows.IsPriorityReason(EventWindows.ReasonHighEvent).Should().BeTrue();
            EventWindows.IsPriorityReason(EventWindows.ReasonOutside).Should().BeFalse();
            EventWindows.IsPriorityReason(null).Should().BeFalse();
            EventWindows.PreemptedReason("Events/Mars with M 44").Should().Be("high-priority event Events/Mars with M 44 was ready");
        }

        [Test]
        public void IgnoreMoonIsCarriedOnTheRow() {
            new TargetWindowTable(new[] { Row("normal", true, "g1"), Row("high", false, "g2") }).Get("G1").IgnoreMoon.Should().BeTrue();
            new TargetWindowTable(new[] { Row("normal", true, "g1"), Row("high", false, "g2") }).Get("g2").IgnoreMoon.Should().BeFalse();
        }

        [Test]
        public void WithoutWindowsNothingChanges() {
            TargetWindowTable.Empty.Count.Should().Be(0);
            TargetWindowTable.Empty.Get("g").Should().BeNull();
            new TargetWindowTable(new[] { Row("high", guid: "other") }).Get("g").Should().BeNull();

            var a = C("a", null);
            var b = C("b", null);
            var c = C("c", null);
            List<Candidate> ready = new List<Candidate> { a, b, c };
            bool rejected = false;
            EventWindows.Prefer(ready, x => x.Window, (x, r) => rejected = true);
            ready.Should().Equal(a, b, c);
            rejected.Should().BeFalse();
            EventWindows.Prefer<Candidate>(null, x => x.Window, (x, r) => rejected = true);
            rejected.Should().BeFalse();

        }
    }
}
