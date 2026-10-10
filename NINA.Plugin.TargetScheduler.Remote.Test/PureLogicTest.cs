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
            plans[4].TargetEnd = Now.AddMinutes(30);
            var blocks = PreviewBuilder.Blocks(plans);
            blocks.Select(b => (b.TargetId, b.Filter, b.Count, b.Picked)).Should().Equal(
                (1, "Ha", 2, true), (1, "OIII", 1, false), (2, "L", 2, true), (0, null, 0, false), (1, "Ha", 1, true));
            blocks[0].End.Should().Be(Now.AddMinutes(10));
            blocks[0].Scores.Should().ContainSingle();
            blocks[1].Scores.Should().BeNull();
            blocks[1].Reason.Should().Contain("T2 scored higher");
            blocks[2].Reason.Should().Contain("leaves its window");
            blocks[4].Reason.Should().Be("end of the night");
        }

        [Test]
        public void LeftOutSkipsTargetsThatGotTime() {
            var blocks = new List<PreviewBlock> { new PreviewBlock { TargetId = 1 } };
            var left = PreviewBuilder.LeftOut(new[] {
                new LeftOut { TargetId = 1, ProjectName = "A", TargetName = "a", Reason = "lower score" },
                new LeftOut { TargetId = 2, ProjectName = "B", TargetName = "b", Reason = "not visible" },
                new LeftOut { TargetId = 2, ProjectName = "B", TargetName = "b", Reason = "moon avoidance" },
            }, blocks);
            left.Should().ContainSingle().Which.Reason.Should().Be("moon avoidance");
        }
    }
}
