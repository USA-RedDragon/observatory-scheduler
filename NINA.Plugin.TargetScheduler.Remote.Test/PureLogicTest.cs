using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class PureLogicTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);

        [Test]
        public void SkipListMatchesTargetsAndProjectsUntilExpiry() {
            var s = new SkipList();
            s.Add(new SkipPayload { Scope = "target", TargetId = 13, ProjectId = 5, TargetName = "P2" }, Now.AddMinutes(60));
            s.Add(new SkipPayload { Scope = "project", ProjectId = 7, ProjectName = "California" }, Now.AddHours(5));
            s.Match(5, 13, Now).Name.Should().Be("P2");
            s.Match(5, 12, Now).Should().BeNull();
            s.Match(7, 99, Now).Name.Should().Be("California");
            s.Match(5, 13, Now.AddMinutes(61)).Should().BeNull();
            s.Active(Now.AddMinutes(61)).Should().ContainSingle();

            var copy = new SkipList();
            copy.Load(s.Serialize(Now), Now);
            copy.Match(7, 1, Now).Should().NotBeNull();
            copy.Remove(new SkipPayload { Scope = "project", ProjectId = 7 }).Should().BeTrue();
            copy.Match(7, 1, Now).Should().BeNull();
        }

        [Test]
        public void SkippingAgainReplacesTheEntry() {
            var s = new SkipList();
            var p = new SkipPayload { Scope = "target", TargetId = 13 };
            s.Add(p, Now.AddMinutes(10));
            s.Add(p, Now.AddMinutes(90));
            s.Active(Now).Should().ContainSingle().Which.Until.Should().Be(Now.AddMinutes(90));
        }

        [Test]
        public void ControlTracksReplanPauseAndAutoResume() {
            var c = new SchedulerControl();
            int changes = 0;
            c.Changed = () => changes++;
            c.ReplanRequested.Should().BeFalse();
            c.RequestReplan("x");
            c.TakeReplan().Should().Be("x");
            c.TakeReplan().Should().BeNull();
            c.Pause(new PausePayload { Mount = "park" }, Now.AddMinutes(30));
            c.Paused.Mount.Should().Be("park");
            c.ShouldResume(Now).Should().BeFalse();
            c.ShouldResume(Now.AddMinutes(31)).Should().BeTrue();
            c.Paused.Should().BeNull();
            c.Resume().Should().BeFalse();
            changes.Should().Be(3);
        }

        [Test]
        public void GuardSpotsEveryEditThatShouldEndTheWindow() {
            var picked = new TargetSnapshot { ProjectState = 1, ProjectPriority = 1, TargetActive = true, EnabledPlanIds = new HashSet<int> { 40, 41 } };
            TargetSnapshot Now(Action<TargetSnapshot> f) {
                var s = new TargetSnapshot { ProjectState = 1, ProjectPriority = 1, TargetActive = true, EnabledPlanIds = new HashSet<int> { 40, 41 } };
                f(s);
                return s;
            }
            ContinuationGuard.Reason(picked, Now(_ => { })).Should().BeNull();
            ContinuationGuard.Reason(picked, Now(s => s.ProjectState = 2)).Should().Contain("no longer active");
            ContinuationGuard.Reason(picked, Now(s => s.TargetActive = false)).Should().Contain("inactive");
            ContinuationGuard.Reason(picked, Now(s => s.ProjectPriority = 0)).Should().Contain("priority");
            ContinuationGuard.Reason(picked, Now(s => s.EnabledPlanIds.Remove(41))).Should().Contain("41 was disabled");
            ContinuationGuard.Reason(picked, Now(s => s.EnabledPlanIds.Add(42))).Should().Contain("42 was enabled");
            ContinuationGuard.Reason(picked, null).Should().Contain("no longer exists");
        }

        [Test]
        public void SnapshotReadsTheDatabase() {
            using var db = new TestDb();
            db.Exec("INSERT INTO exposureplan (Id, profileId, exposure, desired, targetid, enabled) VALUES (41, 'p', 300, 10, 13, 0)");
            var snap = new SqlSchedulerDatabase(db.Connect).ReadSnapshot(13);
            snap.ProjectId.Should().Be(5);
            snap.ProjectState.Should().Be(1);
            snap.ProjectPriority.Should().Be(1);
            snap.TargetActive.Should().BeTrue();
            snap.EnabledPlanIds.Should().BeEquivalentTo(new[] { 40 });
            new SqlSchedulerDatabase(db.Connect).ReadSnapshot(999).Should().BeNull();
        }

        [Test]
        public void SettingsPersistAndMintAToken() {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try {
                var path = Path.Combine(dir, "remote.json");
                var s = new RemoteSettings(path);
                var token = s.Snapshot().ApiToken;
                token.Should().HaveLength(48);
                s.Snapshot().WebEditing.Should().BeFalse();
                s.Snapshot().ApiPort.Should().Be(8189);
                s.Update(d => { d.WebEditing = true; d.ApiEnabled = true; d.ApiPort = 0; });
                var again = new RemoteSettings(path).Snapshot();
                again.ApiToken.Should().Be(token);
                again.WebEditing.Should().BeTrue();
                again.ApiPort.Should().Be(8189);
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, true);
                }
            }
        }

        private static PlannedExposure Exp(int min, int target, string filter, bool pick = false, bool wait = false) {
            return new PlannedExposure {
                Start = Now.AddMinutes(min), End = Now.AddMinutes(min + 5), Wait = wait, TargetId = target, TargetName = "T" + target, ProjectId = target * 10,
                Filter = filter, ExposureSeconds = 300, NewPick = pick, Total = pick ? 1.0 : null, Scores = pick ? new List<RuleScore> { new RuleScore { Rule = "Project Priority", Weight = 0.5, Score = 0.5 } } : null,
            };
        }

        [Test]
        public void PreviewMergesRunsAndMarksPicks() {
            var plans = new List<PlannedExposure> {
                Exp(0, 1, "Ha", pick: true), Exp(5, 1, "Ha"), Exp(10, 1, "OIII"),
                Exp(15, 2, "L", pick: true), Exp(20, 2, "L"),
                Exp(25, 0, null, wait: true),
                Exp(60, 1, "Ha", pick: true),
            };
            plans[2].EndReason = "T2 scored higher at the re-plan (0.90 vs 0.80)";
            plans[4].EndReason = "not visible";
            plans[4].TargetEnd = Now.AddMinutes(30);
            plans[6].EndReason = "the simulation failed at 07:05: boom";
            var blocks = PreviewBuilder.Blocks(plans);
            blocks.Select(b => (b.TargetId, b.Filter, b.Count, b.Picked)).Should().Equal(
                (1, "Ha", 2, true), (1, "OIII", 1, false), (2, "L", 2, true), (0, null, 0, false), (1, "Ha", 1, true));
            blocks[0].End.Should().Be(Now.AddMinutes(10));
            blocks[0].Scores.Should().ContainSingle();
            blocks[0].Reason.Should().BeNull();
            blocks[1].Scores.Should().BeNull();
            blocks[1].Reason.Should().Be("T2 scored higher at the re-plan (0.90 vs 0.80)");
            blocks[2].Reason.Should().Be("not visible");
            blocks[4].Reason.Should().Be("the simulation failed at 07:05: boom");
        }

        [Test]
        public void PreviewBlockReasonsComeOnlyFromTheRun() {
            var plans = new List<PlannedExposure> { Exp(0, 1, "Ha", pick: true), Exp(5, 2, "L", pick: true) };
            plans[0].TargetEnd = Now.AddMinutes(10);
            plans[1].CompletesTarget = true;
            plans[1].EndReason = "not visible";
            var blocks = PreviewBuilder.Blocks(plans);
            blocks[0].Reason.Should().BeNull();
            blocks[1].Reason.Should().Be(PreviewBuilder.Complete);
        }

        private static Rejection Rej(int target, int min, string reason, string pass = Rejection.PassPick, double? total = null, string winner = null, double? winnerTotal = null) {
            return new Rejection {
                TargetId = target, ProjectName = "P" + target, TargetName = "T" + target, Reason = reason, At = Now.AddMinutes(min), Pass = pass,
                Total = total, WinnerName = winner, WinnerTotal = winnerTotal,
            };
        }

        [Test]
        public void LeftOutSkipsTargetsThatGotTimeAndIgnoresTheEndPass() {
            var blocks = new List<PreviewBlock> { new PreviewBlock { TargetId = 1 } };
            var passes = new[] { 0, 60, 120, 600 }.Select(m => Now.AddMinutes(m)).ToList();
            var left = PreviewBuilder.LeftOut(new[] {
                Rej(1, 0, "lower score"),
                Rej(2, 0, "not yet visible"),
                Rej(2, 60, "all exposure plans rejected for moon avoidance"),
                Rej(2, 120, "all exposure plans rejected for moon avoidance"),
                Rej(2, 600, "not visible", Rejection.PassEnd),
            }, blocks, passes, Now.AddMinutes(600));
            var l = left.Should().ContainSingle().Which;
            l.TargetId.Should().Be(2);
            l.Reason.Should().Be("all exposure plans rejected for moon avoidance");
            l.At.Should().Be(Now.AddMinutes(60));
            l.Until.Should().Be(Now.AddMinutes(600));
            l.AfterNight.Should().BeFalse();
            l.Reasons.Select(r => (r.Reason, r.Minutes)).Should().Equal(("not yet visible", 60.0), ("all exposure plans rejected for moon avoidance", 540.0));
        }

        [Test]
        public void LeftOutPrefersTheClosestScoreLoss() {
            var passes = new[] { 0, 30, 90, 500 }.Select(m => Now.AddMinutes(m)).ToList();
            var left = PreviewBuilder.LeftOut(new[] {
                Rej(3, 0, "not yet visible"),
                Rej(3, 30, "lower score", total: 0.5, winner: "M31", winnerTotal: 0.9),
                Rej(3, 90, "lower score", total: 0.72, winner: "M33", winnerTotal: 0.81),
                Rej(3, 500, "not visible", Rejection.PassEnd),
            }, new List<PreviewBlock>(), passes, Now.AddMinutes(500));
            var l = left.Should().ContainSingle().Which;
            l.Reason.Should().Be(PreviewBuilder.LowerScore);
            l.WinnerName.Should().Be("M33");
            l.WinnerTotal.Should().Be(0.81);
            l.Total.Should().Be(0.72);
            l.At.Should().Be(Now.AddMinutes(90));
            l.Until.Should().Be(Now.AddMinutes(500));
        }

        [Test]
        public void LeftOutOnlyAtTheEndIsLabelled() {
            var left = PreviewBuilder.LeftOut(new[] { Rej(4, 600, "not visible", Rejection.PassEnd) }, new List<PreviewBlock>(), new[] { Now.AddMinutes(600) }, Now.AddMinutes(600));
            var l = left.Should().ContainSingle().Which;
            l.AfterNight.Should().BeTrue();
            l.Reason.Should().Be("not visible");
            l.Reasons.Should().BeNull();
        }

        [Test]
        public void PreviewStatesItsAssumptions() {
            var a = PreviewBuilder.Assumptions(30, 10, 20, 5000);
            a.Select(x => x.Key).Should().Equal("weather", "grading", "overhead", "visibility", "web", "limits");
            a.Single(x => x.Key == "visibility").Text.Should().Contain("30 s").And.Contain("10 s");
        }

        private static GraderHfrSettings Hfr(bool improvement = false, double? auto = null, string mode = "immediate") {
            return new GraderHfrSettings { ProjectGrading = true, Enabled = true, SigmaFactor = 2, AcceptImprovement = improvement, AutoAcceptLevel = auto, MaxSampleSize = 10, Mode = mode, DelayThresholdPercent = 50 };
        }

        private static GraderPlanInput Plan(params double[] samples) {
            return new GraderPlanInput { PlanId = 9, Filter = "OIII", ExposureSeconds = 300, Acquired = 12, ReferenceImageId = 77, Matching = 11, Samples = samples.ToList() };
        }

        [Test]
        public void GraderLimitUsesTheSampleStandardDeviation() {
            var l = GraderLimits.Build(Hfr(), Plan(2, 4, 4, 4, 5, 5, 7, 9));
            l.State.Should().Be(GraderLimits.StateLimit);
            l.Samples.Should().Be(8);
            l.Mean.Should().Be(5);
            l.Sd.Should().BeApproximately(Math.Sqrt(32.0 / 7), 1e-12);
            l.Upper.Should().BeApproximately(5 + 2 * Math.Sqrt(32.0 / 7), 1e-12);
            l.RejectAbove.Should().Be(l.Upper);
            l.RejectBelow.Should().Be(l.Lower);
            l.PopulationRule.Should().Contain("the 8 newest of 11 subs").And.Contain("sample size 10");
        }

        [Test]
        public void GraderLimitAppliesImprovementAndAutoAccept() {
            var l = GraderLimits.Build(Hfr(improvement: true, auto: 20), Plan(2, 4, 4, 4, 5, 5, 7, 9));
            l.RejectBelow.Should().BeNull();
            l.RejectAbove.Should().Be(20);
            var low = GraderLimits.Build(Hfr(auto: 0.5), Plan(2, 4, 4, 4, 5, 5, 7, 9));
            low.RejectAbove.Should().Be(low.Upper);
            low.RejectBelow.Should().Be(low.Lower);
            GraderLimits.Build(Hfr(auto: 1), Plan(2, 4, 4, 4, 5, 5, 7, 9)).RejectBelow.Should().BeNull();
        }

        [Test]
        public void GraderLimitStates() {
            GraderLimits.Build(Hfr(), Plan(2, 3)).State.Should().Be(GraderLimits.StateTooFewSamples);
            GraderLimits.Build(Hfr(), new GraderPlanInput { PlanId = 1 }).State.Should().Be(GraderLimits.StateNoImages);
            var off = Hfr();
            off.Enabled = false;
            GraderLimits.Build(off, Plan(1, 2, 3)).State.Should().Be(GraderLimits.StateHfrGradingOff);
            var project = Hfr();
            project.ProjectGrading = false;
            GraderLimits.Build(project, Plan(1, 2, 3)).State.Should().Be(GraderLimits.StateProjectGradingOff);
            GraderLimits.Build(Hfr(mode: "delayed"), Plan(1, 2, 3)).PopulationRule.Should().StartWith("all 3 subs").And.Contain("50%");
            var nan = GraderLimits.Build(Hfr(), Plan(1, double.NaN, 3, 4));
            nan.State.Should().Be(GraderLimits.StateInvalidSamples);
            nan.InvalidSamples.Should().Be(1);
            nan.Mean.Should().BeNull();
            GraderLimits.Build(Hfr(), Plan(1, 2, 3)).ZeroRejected.Should().BeTrue();
            GraderLimits.Build(Hfr(auto: 1), Plan(1, 2, 3)).ZeroRejected.Should().BeFalse();
        }
    }
}
