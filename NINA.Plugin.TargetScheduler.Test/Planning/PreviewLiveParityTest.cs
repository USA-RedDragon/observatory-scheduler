using FluentAssertions;
using Moq;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Plugin;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    /// <summary>
    /// The Tonight preview and the live planner run the same Planner. These tests give both the same projects
    /// and time, the way each one gets them, and check that they pick the same target.
    /// </summary>
    [TestFixture]
    public class PreviewLiveParityTest {
        private const int ProjectA = 10, ProjectB = 20, TargetA = 1, TargetB = 2;

        private static readonly DateTime T0 = new DateTime(2023, 12, 17, 19, 0, 0);

        private IProfile profile;
        private IWeatherDataMediator weather;

        [SetUp]
        public void Setup() {
            TargetEditGuard.Instance.Clear();
            var profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            profileMock.SetupProperty(m => m.ActiveProfile.MeridianFlipSettings.MinutesAfterMeridian, 5);
            profile = profileMock.Object.ActiveProfile;
            weather = PlanMocks.GetWeatherDataMediator(false, 0);
        }

        [Test]
        public void ThePreviewKeepsTheRunningTargetExactlyAsLongAsTheLivePlanner() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Projects(), skips, null).PlanTarget;
            running.DatabaseId.Should().Be(TargetA);

            var t1 = T0.AddMinutes(10);
            var liveNext = Live(t1, Projects(), skips, running);
            liveNext.PlanTarget.DatabaseId.Should().Be(TargetA);

            var fresh = Projects();
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, null));
            Preview(t1, fresh, skips, previewRunning).PlanTarget.DatabaseId.Should().Be(liveNext.PlanTarget.DatabaseId);

            var t2 = running.MinimumTimeSpanEnd.AddMinutes(1);
            var liveAfter = Live(t2, Projects(), skips, running);
            liveAfter.PlanTarget.DatabaseId.Should().Be(TargetB);
            Preview(t2, fresh, skips, previewRunning).PlanTarget.DatabaseId.Should().Be(liveAfter.PlanTarget.DatabaseId);
        }

        [Test]
        public void WithoutTheLiveWindowThePreviewReplansAtOnceAndDisagrees() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Projects(), skips, null).PlanTarget;
            var t1 = T0.AddMinutes(10);
            Live(t1, Projects(), skips, running).PlanTarget.DatabaseId.Should().Be(TargetA);

            var fresh = Projects();
            var unseeded = Find(fresh, TargetA);
            Preview(t1, fresh, skips, unseeded).PlanTarget.DatabaseId.Should().Be(TargetB);
        }

        [Test]
        public void WhenTheLivePlannerWillReplanThePreviewReplansToo() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Projects(), skips, null).PlanTarget;
            var t1 = T0.AddMinutes(10);

            var fresh = Projects();
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, "a re-plan was requested from the web"));
            previewRunning.MinimumTimeSpanEnd.Should().Be(default);
            Preview(t1, fresh, skips, previewRunning).PlanTarget.DatabaseId.Should().Be(TargetB);
        }

        [Test]
        public void APendingSkipOfTheRunningTargetMovesThePreviewOnAtItsStart() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Projects(), skips, null).PlanTarget;
            var t1 = T0.AddMinutes(10);

            var pending = skips.Copy();
            pending.Add(new SkipPayload { Scope = "target", TargetId = TargetA, ProjectId = ProjectA, TargetName = "A" }, t1.AddHours(1));
            var liveAfterSkip = Live(t1, Projects(), pending, running);
            liveAfterSkip.PlanTarget.DatabaseId.Should().Be(TargetB);

            var fresh = Projects();
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, "the waiting scheduler.skip command applies when the exposure ends"));
            Preview(t1, fresh, pending, previewRunning).PlanTarget.DatabaseId.Should().Be(liveAfterSkip.PlanTarget.DatabaseId);
        }

        [Test]
        public void ThePlannerHonorsTheSkipsItIsGiven() {
            var projects = Projects();
            var skips = SkipBUntil(T0.AddMinutes(5));
            var planner = new Planner(T0, profile, new ProfilePreference("abcd-1234"), weather, false, true, projects) { Skips = skips };
            planner.FilterForSkips(projects);
            Find(projects, TargetB).Rejected.Should().BeTrue();
            Find(projects, TargetB).RejectedReason.Should().StartWith("skipped until ");
            Find(projects, TargetA).Rejected.Should().BeFalse();

            var later = Projects();
            new Planner(T0.AddMinutes(6), profile, new ProfilePreference("abcd-1234"), weather, false, true, later) { Skips = skips }.FilterForSkips(later);
            Find(later, TargetB).Rejected.Should().BeFalse();
        }

        [Test]
        public void TheSequencesEndOfImagingEndsTheTargetInBothPlanners() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var limit = new ImagingLimit { End = T0.AddHours(2), Basis = "the Sun Altitude loop condition" };
            var live = Live(T0, Projects(), skips, null, limit).PlanTarget;
            live.EndTime.Should().Be(limit.End);

            var withLimit = Preview(T0, Projects(), skips, null, limit).PlanTarget;
            withLimit.DatabaseId.Should().Be(live.DatabaseId);
            withLimit.EndTime.Should().Be(live.EndTime);

            Preview(T0, Projects(), skips, null).PlanTarget.EndTime.Should().BeAfter(limit.End);
        }

        [TestCase(1.005, TargetA)]
        [TestCase(1.015, TargetB)]
        public void BothPlannersApplyTheTieBandTheSameWay(double challengerFactor, int expected) {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Tied(challengerFactor), skips, null).PlanTarget;
            running.DatabaseId.Should().Be(TargetA);
            var t2 = running.MinimumTimeSpanEnd.AddMinutes(1);

            var liveProjects = Tied(challengerFactor);
            var live = Live(t2, liveProjects, skips, running);
            var ratio = Find(liveProjects, TargetB).ScoringResults.TotalScore / Find(liveProjects, TargetA).ScoringResults.TotalScore;
            ratio.Should().BeApproximately(challengerFactor, 1e-9);
            live.PlanTarget.DatabaseId.Should().Be(expected);

            var fresh = Tied(challengerFactor);
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, null));
            var preview = Preview(t2, fresh, skips, previewRunning);
            preview.PlanTarget.DatabaseId.Should().Be(live.PlanTarget.DatabaseId);
            (preview.PlanTarget.ScoringResults.Held != null).Should().Be(expected == TargetA);
            (live.PlanTarget.ScoringResults.Held != null).Should().Be(expected == TargetA);
        }

        [Test]
        public void ASkippedRunningTargetNeverHoldsInEitherPlanner() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Tied(1.005), skips, null).PlanTarget;
            var t2 = running.MinimumTimeSpanEnd.AddMinutes(1);
            var skipA = new SkipList();
            skipA.Add(new SkipPayload { Scope = "target", TargetId = TargetA, ProjectId = ProjectA, TargetName = "A" }, t2.AddHours(1).ToUniversalTime());

            Live(t2, Tied(1.005), skipA, running).PlanTarget.DatabaseId.Should().Be(TargetB);

            var fresh = Tied(1.005);
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, null));
            Preview(t2, fresh, skipA, previewRunning).PlanTarget.DatabaseId.Should().Be(TargetB);
        }

        [Test]
        public void ADoneRunningTargetNeverHoldsInEitherPlanner() {
            var skips = SkipBUntil(T0.AddMinutes(5));
            var running = Live(T0, Tied(1.005), skips, null).PlanTarget;
            var t2 = running.MinimumTimeSpanEnd.AddMinutes(1);

            Live(t2, Tied(1.005, aDone: true), skips, running).PlanTarget.DatabaseId.Should().Be(TargetB);

            var fresh = Tied(1.005, aDone: true);
            var previewRunning = Find(fresh, TargetA);
            PreviewRunner.KeepLiveWindow(previewRunning, Imaging(running, null));
            Preview(t2, fresh, skips, previewRunning).PlanTarget.DatabaseId.Should().Be(TargetB);
        }

        private static List<IProject> Tied(double challengerFactor, bool aDone = false) {
            var projects = PlanMocks.ProjectsList(Project(ProjectA, TargetA, "A", ProjectPriority.Normal, aDone ? 100 : 0), Project(ProjectB, TargetB, "B", ProjectPriority.Normal));
            var weights = projects.Single(p => p.DatabaseId == ProjectB).RuleWeights;
            foreach (var rule in weights.Keys.ToList()) {
                weights[rule] *= challengerFactor;
            }
            return projects;
        }

        private SchedulerPlan Live(DateTime at, List<IProject> projects, SkipList skips, ITarget previous, ImagingLimit limit = null) {
            return new Planner(at, profile, new ProfilePreference("abcd-1234"), weather, false, false, projects) { Skips = skips, Limit = limit }.GetPlan(previous);
        }

        private SchedulerPlan Preview(DateTime at, List<IProject> projects, SkipList skips, ITarget previous, ImagingLimit limit = null) {
            foreach (var p in projects) {
                p.Rejected = false;
                p.RejectedReason = null;
                foreach (var t in p.Targets) {
                    t.Rejected = false;
                    t.RejectedReason = null;
                    t.ScoringResults = null;
                    foreach (var e in t.ExposurePlans) {
                        e.Rejected = false;
                        e.RejectedReason = null;
                    }
                }
            }
            return new Planner(at, profile, new ProfilePreference("abcd-1234"), weather, false, true, projects) { Skips = skips, LiveSampling = true, Limit = limit }.GetPlan(previous);
        }

        private static LiveImaging Imaging(ITarget running, string replanReason) {
            return new LiveImaging {
                TargetId = running.DatabaseId,
                TargetName = running.Name,
                Window = new PlanWindow {
                    Start = running.StartTime,
                    End = running.EndTime,
                    MinimumEnd = running.MinimumTimeSpanEnd,
                    BonusEnd = running.BonusTimeSpanEnd,
                    Culmination = running.CulminationTime,
                },
                ReplanReason = replanReason,
            };
        }

        private static SkipList SkipBUntil(DateTime until) {
            var skips = new SkipList();
            skips.Add(new SkipPayload { Scope = "target", TargetId = TargetB, ProjectId = ProjectB, TargetName = "B" }, until.ToUniversalTime());
            return skips;
        }

        private static ITarget Find(List<IProject> projects, int targetId) {
            return projects.SelectMany(p => p.Targets).Single(t => t.DatabaseId == targetId);
        }

        /// <summary>
        /// The two projects as a fresh load from the database gives them: A at low priority, B at high priority,
        /// both on M31 so that only the priority and the switch penalty tell them apart.
        /// </summary>
        private static List<IProject> Projects() {
            return PlanMocks.ProjectsList(Project(ProjectA, TargetA, "A", ProjectPriority.Low), Project(ProjectB, TargetB, "B", ProjectPriority.High));
        }

        private static IProject Project(int projectId, int targetId, string name, ProjectPriority priority, int accepted = 0) {
            Mock<IProject> pp = PlanMocks.GetMockPlanProject(name, ProjectState.Active);
            pp.SetupProperty(p => p.DatabaseId, projectId);
            pp.SetupProperty(p => p.Priority, priority);
            pp.Object.RuleWeights[TargetSwitchPenaltyRule.RULE_NAME] = 0;
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget(name, TestData.M31);
            pt.SetupProperty(t => t.DatabaseId, targetId);
            pt.SetupProperty(t => t.IsPreview, true);
            pt.SetupProperty(t => t.ExposureSelector, new RepeatUntilDoneExposureSelector(pp.Object, pt.Object, new Target()));
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("L", 100, accepted, 300, targetId * 100);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp, pt);
            return pp.Object;
        }
    }
}
