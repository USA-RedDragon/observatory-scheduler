using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class EventWindowPlannerTest {
        private static readonly DateTime Night = new DateTime(2023, 12, 17, 23, 0, 0);

        [SetUp]
        public void SetUp() {
            TargetEditGuard.Instance.Clear();
            TargetVisibilityCache.Clear();
            TargetWindowTable.Current = TargetWindowTable.Empty;
        }

        [TearDown]
        public void TearDown() {
            TargetWindowTable.Current = TargetWindowTable.Empty;
            SubLengthTable.Current = SubLengthTable.Empty;
            TargetVisibilityCache.Clear();
        }

        private static IProfile Profile() {
            return PlanMocks.GetMockProfileService(TestData.Pittsboro_NC).Object.ActiveProfile;
        }

        private static IWeatherDataMediator Weather() {
            return PlanMocks.GetWeatherDataMediator(false, 0);
        }

        private static ProfilePreference Prefs() {
            return new ProfilePreference("abcd-1234");
        }

        private static void Windows(params (string guid, DateTime start, DateTime end, string priority, bool ignoreMoon)[] rows) {
            List<TargetWindowRow> list = new List<TargetWindowRow>();
            foreach (var r in rows) {
                list.Add(new TargetWindowRow { TargetGuid = r.guid, StartUtc = r.start.ToUniversalTime(), EndUtc = r.end.ToUniversalTime(), Priority = r.priority, IgnoreMoon = r.ignoreMoon });
            }
            TargetWindowTable.Current = new TargetWindowTable(list);
        }

        private static (Mock<IProject>, Mock<ITarget>) Target(string name, string guid, int id) {
            Mock<IProject> pp = PlanMocks.GetMockPlanProject(name, ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget(name, TestData.M42);
            pt.SetupProperty(t => t.Guid, guid);
            pt.SetupProperty(t => t.DatabaseId, id);
            pt.SetupProperty(t => t.IsPreview, true);
            PlanMocks.AddMockPlanFilter(pt, PlanMocks.GetMockPlanExposure("Ha", 10, 0));
            PlanMocks.AddMockPlanTarget(pp, pt);
            pt.SetupProperty(t => t.ExposureSelector, new RepeatUntilDoneExposureSelector(pp.Object, pt.Object, new Target()));
            return (pp, pt);
        }

        private static ITarget Visible(DateTime atTime) {
            var (pp, pt) = Target("ev", "ev", 1);
            new Planner(atTime, Profile(), Prefs(), Weather(), false, false).FilterForVisibility(PlanMocks.ProjectsList(pp.Object));
            return pt.Object;
        }

        [Test]
        public void WithoutAWindowVisibilityIsUnchanged() {
            ITarget plain = Visible(Night);
            Windows(("other", Night.AddHours(-10), Night.AddHours(10), "high", true));
            ITarget again = Visible(Night);
            again.Rejected.Should().Be(plain.Rejected);
            again.StartTime.Should().Be(plain.StartTime);
            again.EndTime.Should().Be(plain.EndTime);
            plain.Rejected.Should().BeFalse();
        }

        [Test]
        public void AnEndedWindowRejectsTheTarget() {
            Windows(("ev", Night.AddHours(-5), Night.AddHours(-1), "high", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeTrue();
            t.RejectedReason.Should().Be(Reasons.TargetOutsideEventWindow);
        }

        [Test]
        public void AWindowOnALaterNightRejectsTheTarget() {
            Windows(("ev", Night.AddDays(2), Night.AddDays(3), "normal", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeTrue();
            t.RejectedReason.Should().Be(Reasons.TargetOutsideEventWindow);
        }

        [Test]
        public void TheVisibleSpanIsClippedToTheWindowEnd() {
            ITarget plain = Visible(Night);
            DateTime end = Night.AddHours(1);
            plain.EndTime.Should().BeAfter(end);
            Windows(("ev", Night.AddHours(-5), end, "normal", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeFalse();
            t.EndTime.Should().BeCloseTo(end, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void ATargetBeforeItsWindowWaitsForTheStart() {
            DateTime start = Night.AddHours(1);
            Windows(("ev", start, Night.AddHours(4), "normal", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeTrue();
            t.RejectedReason.Should().Be(Reasons.TargetBeforeEventWindow);
            t.StartTime.Should().BeCloseTo(start, TimeSpan.FromMinutes(1));
            new TargetImagingExpert(Profile(), Prefs(), false).VisibleLater(t).Should().BeTrue();
        }

        [Test]
        public void ReadyEventsAreFilteredByPriority() {
            Windows(("normal", Night.AddHours(-1), Night.AddHours(1), "normal", false), ("high", Night.AddHours(-1), Night.AddHours(1), "high", false));
            ITarget dso = Target("dso", "dso", 1).Item2.Object;
            ITarget normal = Target("normal", "normal", 2).Item2.Object;
            ITarget high = Target("high", "high", 3).Item2.Object;
            Planner planner = new Planner(Night, Profile(), Prefs(), Weather(), false, false);

            List<ITarget> ready = new List<ITarget> { dso, normal };
            planner.FilterForEventPriority(ready);
            ready.Should().Equal(dso);
            normal.Rejected.Should().BeTrue();

            ready = new List<ITarget> { dso, high };
            planner.FilterForEventPriority(ready);
            ready.Should().Equal(high);
            dso.RejectedReason.Should().Be(EventWindows.ReasonHighEvent);
        }

        [Test]
        public void ARunningTargetEndsAtTheWindowEnd() {
            var (pp, pt) = Target("ev", "ev", 1);
            pt.SetupProperty(t => t.EndTime, Night.AddHours(12));
            pt.SetupProperty(t => t.MinimumTimeSpanEnd, Night.AddHours(1));
            pt.SetupProperty(t => t.BonusTimeSpanEnd, Night.AddHours(1));
            PreviousTargetExpert sut = new PreviousTargetExpert(Profile(), Prefs(), true, TestData.Pittsboro_NC);

            sut.CanContinue(Night, Weather(), pt.Object).Should().BeTrue();
            Windows(("ev", Night.AddHours(-1), Night.AddMinutes(10), "high", false));
            sut.CanContinue(Night, Weather(), pt.Object).Should().BeTrue();
            sut.CanContinue(Night.AddMinutes(10), Weather(), pt.Object).Should().BeFalse();
            sut.CanContinue(Night.AddMinutes(12), Weather(), pt.Object).Should().BeFalse();
        }

        [Test]
        public void AHighEventInterruptsARunningTargetAtTheBoundary() {
            var (dsoProject, dso) = Target("dso", "dso", 1);
            var (eventProject, ev) = Target("ev", "ev", 2);
            List<IProject> projects = PlanMocks.ProjectsList(dsoProject.Object, eventProject.Object);
            Planner planner = new Planner(Night, Profile(), Prefs(), Weather(), false, true, projects);

            planner.HighEventPreempts(dso.Object).Should().BeFalse();

            Windows(("ev", Night.AddHours(-1), Night.AddHours(2), "normal", false));
            planner.HighEventPreempts(dso.Object).Should().BeFalse();

            Windows(("ev", Night.AddHours(1), Night.AddHours(2), "high", false));
            planner.HighEventPreempts(dso.Object).Should().BeFalse();

            Windows(("ev", Night.AddHours(-1), Night.AddHours(2), "high", false));
            planner.HighEventPreempts(dso.Object).Should().BeTrue();
            ev.Object.Rejected.Should().BeFalse();
            ev.Object.SelectedExposure.Should().BeNull();
            ev.Object.StartTime.Should().Be(default);
        }

        [Test]
        public void AHighEventInTheRunningTargetsProjectInterrupts() {
            var (project, dso) = Target("dso", "dso", 1);
            Mock<ITarget> ev = PlanMocks.GetMockPlanTarget("ev", TestData.M42);
            ev.SetupProperty(t => t.Guid, "ev");
            ev.SetupProperty(t => t.DatabaseId, 2);
            ev.SetupProperty(t => t.IsPreview, true);
            PlanMocks.AddMockPlanFilter(ev, PlanMocks.GetMockPlanExposure("Ha", 10, 0));
            PlanMocks.AddMockPlanTarget(project, ev);
            ev.SetupProperty(t => t.ExposureSelector, new RepeatUntilDoneExposureSelector(project.Object, ev.Object, new Target()));
            dso.SetupProperty(t => t.MinimumTimeSpanEnd, Night.AddMinutes(30));
            Planner planner = new Planner(Night, Profile(), Prefs(), Weather(), false, true, PlanMocks.ProjectsList(project.Object));

            Windows(("ev", Night.AddHours(-1), Night.AddHours(2), "high", false));
            planner.HighEventPreempts(dso.Object).Should().BeTrue();
            planner.PreemptedBy.Should().Be("dso/ev");
            dso.Object.MinimumTimeSpanEnd.Should().Be(Night.AddMinutes(30));
            dso.Object.Rejected.Should().BeFalse();
        }

        [Test]
        public void AWindowShorterThanTheMinimumTimeIsImaged() {
            Windows(("ev", Night.AddMinutes(-5), Night.AddMinutes(10), "normal", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeFalse();
            t.EndTime.Should().BeCloseTo(Night.AddMinutes(10), TimeSpan.FromMinutes(1));
        }

        [Test]
        public void AShortWindowLaterTonightWaitsForItsStart() {
            DateTime start = Night.AddHours(1);
            Windows(("ev", start, start.AddMinutes(10), "normal", false));
            ITarget t = Visible(Night);
            t.Rejected.Should().BeTrue();
            t.RejectedReason.Should().Be(Reasons.TargetBeforeEventWindow);
            t.StartTime.Should().BeOnOrAfter(start);
            t.StartTime.Should().BeCloseTo(start, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void AHighEventNeverInterruptsAnotherHighEvent() {
            var (aProject, a) = Target("a", "a", 1);
            var (bProject, b) = Target("b", "b", 2);
            List<IProject> projects = PlanMocks.ProjectsList(aProject.Object, bProject.Object);
            Windows(("a", Night.AddHours(-1), Night.AddHours(2), "high", false), ("b", Night.AddHours(-1), Night.AddHours(2), "high", false));
            new Planner(Night, Profile(), Prefs(), Weather(), false, true, projects).HighEventPreempts(a.Object).Should().BeFalse();
        }

        [Test]
        public void AHighEventThatCannotBeImagedDoesNotInterrupt() {
            var (dsoProject, dso) = Target("dso", "dso", 1);
            var (eventProject, ev) = Target("ev", "ev", 2);
            ev.Object.ExposurePlans[0].MoonAvoidanceEnabled = true;
            ev.Object.ExposurePlans[0].MoonDownEnabled = true;
            ev.Object.ExposurePlans[0].MoonRelaxMaxAltitude = -90;
            List<IProject> projects = PlanMocks.ProjectsList(dsoProject.Object, eventProject.Object);
            Windows(("ev", Night.AddHours(-1), Night.AddHours(2), "high", false));
            new Planner(Night, Profile(), Prefs(), Weather(), false, true, projects).HighEventPreempts(dso.Object).Should().BeFalse();
        }
    }
}
