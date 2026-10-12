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
        public void HighEventsInterruptAtTheBoundaryButNeverEachOther() {
            DateTime inside = Start.AddHours(1);
            EventWindows.Preempts(null, Row("high"), inside).Should().BeTrue();
            EventWindows.Preempts(Row("normal"), Row("high"), inside).Should().BeTrue();
            EventWindows.Preempts(Row("high"), Row("high"), inside).Should().BeFalse();
            EventWindows.Preempts(null, Row("normal"), inside).Should().BeFalse();
            EventWindows.Preempts(null, null, inside).Should().BeFalse();
            EventWindows.Preempts(null, Row("high"), Start.AddSeconds(-1)).Should().BeFalse();
            EventWindows.Preempts(null, Row("high"), End).Should().BeFalse();
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
