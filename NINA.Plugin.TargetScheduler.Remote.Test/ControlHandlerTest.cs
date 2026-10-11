using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class ControlHandlerTest {

        [Test]
        public void SkipForMinutes() {
            var ctx = new FakeContext();
            var r = new SkipHandler().Apply(Cmd.Of(CommandKinds.Skip, @"{""scope"":""target"",""target_id"":13,""target_name"":""Panel 2"",""minutes"":60}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            ctx.FakeControlInstance.Skips.Should().ContainSingle().Which.until.Should().Be(ctx.Now.AddMinutes(60));
            ctx.FakeControlInstance.Replans.Should().HaveCount(1);
        }

        [Test]
        public void SkipForTheNightUsesNightEnd() {
            var ctx = new FakeContext();
            ctx.FakeControlInstance.Night = ctx.Now.AddHours(5);
            new SkipHandler().Apply(Cmd.Of(CommandKinds.Skip, @"{""scope"":""project"",""project_id"":5,""project_name"":""Sadr Region""}"), ctx);
            ctx.FakeControlInstance.Skips.Should().ContainSingle().Which.until.Should().Be(ctx.Now.AddHours(5));
        }

        [Test]
        public void UnskipRemoves() {
            var ctx = new FakeContext();
            const string p = @"{""scope"":""target"",""target_id"":13,""minutes"":60}";
            new SkipHandler().Apply(Cmd.Of(CommandKinds.Skip, p), ctx);
            new UnskipHandler().Apply(Cmd.Of(CommandKinds.Unskip, p), ctx).Message.Should().BeNull();
            ctx.FakeControlInstance.Skips.Should().BeEmpty();
            new UnskipHandler().Apply(Cmd.Of(CommandKinds.Unskip, p), ctx).Message.Should().Contain("no longer");
        }

        [TestCase(@"{""scope"":""mosaic"",""project_id"":5}")]
        [TestCase(@"{""scope"":""target""}")]
        [TestCase(@"{""scope"":""project"",""project_id"":5,""minutes"":-1}")]
        public void BadSkipsAreRejected(string payload) {
            new SkipHandler().Apply(Cmd.Of(CommandKinds.Skip, payload), new FakeContext()).Status.Should().Be(CommandStatus.Rejected);
        }

        [Test]
        public void PauseAndResume() {
            var ctx = new FakeContext();
            new PauseHandler().Apply(Cmd.Of(CommandKinds.Pause, @"{""mount"":""park"",""resume_after_minutes"":30}"), ctx).Status.Should().Be(CommandStatus.Applied);
            ctx.FakeControlInstance.Paused.Mount.Should().Be("park");
            ctx.FakeControlInstance.PausedUntil.Should().Be(ctx.Now.AddMinutes(30));
            new ResumeHandler().Timing.Should().Be(CommandTiming.Immediate);
            new ResumeHandler().Apply(Cmd.Of(CommandKinds.Resume, "{}"), ctx).Message.Should().BeNull();
            new ResumeHandler().Apply(Cmd.Of(CommandKinds.Resume, "{}"), ctx).Message.Should().Contain("not paused");
        }

        [Test]
        public void PauseRejectsUnknownMount() {
            new PauseHandler().Validate(Cmd.Of(CommandKinds.Pause, @"{""mount"":""spin""}")).Should().Contain("mount");
            new PauseHandler().Validate(Cmd.Of(CommandKinds.Pause, @"{""mount"":""track"",""resume_at"":""soon""}")).Should().Contain("resume_at");
        }

        [Test]
        public void ResumeAtClockTimeRollsToTomorrow() {
            var now = new DateTime(2026, 10, 9, 23, 30, 0);
            PauseHandler.TryParseResumeAt("03:00", now, out var at).Should().BeTrue();
            at.Should().Be(new DateTime(2026, 10, 10, 3, 0, 0));
            PauseHandler.TryParseResumeAt("23:45", now, out at).Should().BeTrue();
            at.Should().Be(new DateTime(2026, 10, 9, 23, 45, 0));
        }

        private const string AbortRunning = @"{""plan_id"":40,""started_at"":""2026-10-10T06:45:00.1234567Z"",""target_name"":""Panel 2"",""filter"":""Ha""}";

        private static CaptureKey Running() => new CaptureKey { PlanId = 40, StartedAt = new DateTime(2026, 10, 10, 6, 45, 0, 123, DateTimeKind.Utc) };

        [Test]
        public void AbortStopsTheRunningExposureAndAppliesAtOnce() {
            var ctx = new FakeContext();
            ctx.FakeControlInstance.Exposing = Running();
            var h = new AbortExposureHandler();
            h.Kind.Should().Be("exposure.abort");
            h.Timing.Should().Be(CommandTiming.Immediate);
            h.IsEdit.Should().BeFalse();
            var r = h.Apply(Cmd.Of(CommandKinds.AbortExposure, AbortRunning), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().StartWith("Stopping the exposure and discarding the partial frame.");
            ctx.FakeControlInstance.Aborts.Should().ContainSingle().Which.Should().Be("exposure aborted by web");
        }

        [Test]
        public void AbortIsANoOpWhenNothingIsExposing() {
            var ctx = new FakeContext();
            var r = new AbortExposureHandler().Apply(Cmd.Of(CommandKinds.AbortExposure, AbortRunning), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().Be("Nothing was exposing, so nothing was aborted.");
            ctx.FakeControlInstance.Aborts.Should().BeEmpty();
            ctx.FakeControlInstance.Replans.Should().BeEmpty();
        }

        [Test]
        public void AStaleAbortLeavesALaterExposureAlone() {
            var ctx = new FakeContext();
            ctx.FakeControlInstance.Exposing = new CaptureKey { PlanId = 40, StartedAt = Running().StartedAt.AddMinutes(10) };
            var r = new AbortExposureHandler().Apply(Cmd.Of(CommandKinds.AbortExposure, AbortRunning), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().Contain("already ended").And.Contain("left alone");
            ctx.FakeControlInstance.Aborts.Should().BeEmpty();
            ctx.FakeControlInstance.Exposing.Should().NotBeNull();
        }

        [TestCase("{}")]
        [TestCase("null")]
        [TestCase(@"{""plan_id"":40}")]
        [TestCase(@"{""started_at"":""2026-10-10T06:45:00Z""}")]
        [TestCase(@"{""plan_id"":40,""started_at"":""soon""}")]
        public void AbortNeedsTheExposureItTargets(string payload) {
            var ctx = new FakeContext();
            ctx.FakeControlInstance.Exposing = Running();
            new AbortExposureHandler().Apply(Cmd.Of(CommandKinds.AbortExposure, payload), ctx).Status.Should().Be(CommandStatus.Rejected);
            ctx.FakeControlInstance.Aborts.Should().BeEmpty();
        }

        [Test]
        public void BuiltinRegistryHasEveryKind() {
            var reg = CommandRegistry.Builtin();
            reg.Kinds.Should().BeEquivalentTo(new[] {
                CommandKinds.ProjectEdit, CommandKinds.TargetEdit, CommandKinds.ExposurePlanEdit,
                CommandKinds.Skip, CommandKinds.Unskip, CommandKinds.Pause, CommandKinds.Resume, CommandKinds.Replan, CommandKinds.AbortExposure,
                CommandKinds.ExposureTemplateEdit, CommandKinds.ProjectBatchEdit, CommandKinds.ExposurePlanBatchEdit, CommandKinds.ExposureTemplateBatchEdit,
                CommandKinds.ExposureTemplateClone, CommandKinds.ExposureTemplateDelete, CommandKinds.GoalEdit, CommandKinds.RuleWeightEdit,
                CommandKinds.ApplySet, CommandKinds.UnapplySet, CommandKinds.ProjectCreate, CommandKinds.ProjectDelete });
            Action dup = () => reg.Register(new ReplanHandler());
            dup.Should().Throw<InvalidOperationException>();
        }
    }
}
