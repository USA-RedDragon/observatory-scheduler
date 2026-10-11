using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class CommandProcessorTest {
        private TestDb db;
        private FakeContext ctx;
        private SqlCommandStore store;
        private bool webEditing;
        private bool inPlan;
        private List<CommandResult> events;
        private CommandProcessor proc;

        private const string PriorityEdit = @"{""id"":5,""name"":""Cygnis Loop"",""changes"":[{""field"":""priority"",""before"":1,""after"":0}]}";

        [SetUp]
        public void SetUp() {
            db = new TestDb();
            ctx = new FakeContext { Database = new SqlSchedulerDatabase(db.Connect) };
            store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            webEditing = true;
            inPlan = true;
            events = new List<CommandResult>();
            proc = new CommandProcessor(CommandRegistry.Builtin(), store, ctx, () => webEditing, () => inPlan, () => PendingTiming.At(ctx.Now.AddMinutes(4), PendingTiming.WhenExposureEnds));
            proc.ResultChanged += r => events.Add(r);
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        [Test]
        public void AbortAppliesMidPlanWithoutWaitingForTheBoundary() {
            ctx.FakeControlInstance.Exposing = new CaptureKey { PlanId = 40, StartedAt = ctx.Now.AddMinutes(-3) };
            var r = proc.Submit(Cmd.Of(CommandKinds.AbortExposure, @"{""plan_id"":40,""started_at"":""2026-10-10T06:49:00Z""}", "abort"), "api");
            r.Status.Should().Be(CommandStatus.Applied);
            proc.Pending.Should().BeEmpty();
            store.GetResult("abort").Status.Should().Be(CommandStatus.Applied);
            ctx.FakeControlInstance.Aborts.Should().HaveCount(1);
        }

        [Test]
        public void EditsWaitForThePlanBoundary() {
            var r = proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "a"), "api");
            r.Status.Should().Be(CommandStatus.Pending);
            r.AppliesAt.Should().Be(ctx.Now.AddMinutes(4));
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(1L);
            store.GetResult("a").Status.Should().Be(CommandStatus.Pending);
            proc.Pending.Should().HaveCount(1);

            proc.ApplyPending().Should().Be(1);
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(0L);
            store.GetResult("a").Status.Should().Be(CommandStatus.Applied);
            proc.Pending.Should().BeEmpty();
            events.Should().HaveCount(2);
        }

        [Test]
        public void PendingCommandsApplyInCreationOrderWhateverTheArrivalOrder() {
            var later = Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""state"",""before"":2,""after"":3}]}", "later");
            later.CreatedAt = ctx.Now.AddSeconds(10);
            var earlier = Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""state"",""before"":1,""after"":2}]}", "earlier");
            earlier.CreatedAt = ctx.Now;
            proc.Submit(later, "api");
            proc.Submit(earlier, "queue");
            proc.ApplyPending();
            store.GetResult("earlier").Status.Should().Be(CommandStatus.Applied);
            store.GetResult("later").Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT state FROM project WHERE Id = 5").Should().Be(3L);
        }

        [Test]
        public void EditsApplyAtOnceWhenNotImaging() {
            inPlan = false;
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit), "api").Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void ResumeIsImmediateEvenWhileImaging() {
            proc.Submit(Cmd.Of(CommandKinds.Resume, "{}"), "api").Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void SameIdIsIdempotentAcrossTransports() {
            inPlan = false;
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "x"), "api").Status.Should().Be(CommandStatus.Applied);
            db.Exec("UPDATE project SET priority = 1 WHERE Id = 5");
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "x"), "queue").Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(1L);
            proc.Poll().Should().Be(0);
        }

        [Test]
        public void PendingIdSubmittedTwiceStaysOnce() {
            proc.Submit(Cmd.Of(CommandKinds.Replan, "{}", "r"), "api");
            proc.Submit(Cmd.Of(CommandKinds.Replan, "{}", "r"), "queue").Status.Should().Be(CommandStatus.Pending);
            proc.Pending.Should().HaveCount(1);
        }

        [Test]
        public void EditsAreRejectedWhenWebEditingIsOff() {
            webEditing = false;
            var r = proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit), "api");
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("Web editing is off");
            proc.Submit(Cmd.Of(CommandKinds.Skip, @"{""scope"":""target"",""target_id"":13}"), "api").Status.Should().Be(CommandStatus.Pending);
        }

        [Test]
        public void UnknownKindIsRejected() {
            proc.Submit(Cmd.Of("telescope.dance", "{}"), "api").Status.Should().Be(CommandStatus.Rejected);
        }

        [Test]
        public void CancelPending() {
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "c"), "api");
            proc.Cancel("c").Status.Should().Be(CommandStatus.Cancelled);
            proc.ApplyPending().Should().Be(0);
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(1L);
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "c"), "queue").Status.Should().Be(CommandStatus.Cancelled);
        }

        [Test]
        public void CancelAfterApplyReportsApplied() {
            inPlan = false;
            proc.Submit(Cmd.Of(CommandKinds.Replan, "{}", "d"), "api");
            proc.Cancel("d").Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void CancelBeforeArrivalBlocksTheLateCopy() {
            proc.Cancel("early").Status.Should().Be(CommandStatus.Cancelled);
            proc.Submit(Cmd.Of(CommandKinds.Replan, "{}", "early"), "queue").Status.Should().Be(CommandStatus.Cancelled);
        }

        [Test]
        public void PollPicksUpQueuedCommandsAndCancellations() {
            db.Exec(@"INSERT INTO ts_command (id, kind, payload, author, created_at, cancelled) VALUES
                ('q1', 'project.edit', '" + PriorityEdit + @"', 'web', '2026-10-10 06:00:00', 0),
                ('q2', 'scheduler.replan', '{}', 'web', '2026-10-10 06:01:00', 1)");
            proc.Poll().Should().Be(2);
            store.GetResult("q1").Status.Should().Be(CommandStatus.Pending);
            store.GetResult("q2").Status.Should().Be(CommandStatus.Cancelled);
            proc.Poll().Should().Be(0);

            db.Exec("UPDATE ts_command SET cancelled = 1 WHERE id = 'q1'");
            proc.Poll().Should().Be(1);
            store.GetResult("q1").Status.Should().Be(CommandStatus.Cancelled);
            proc.Pending.Should().BeEmpty();
        }

        [Test]
        public void PendingCommandsSurviveARestart() {
            proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, PriorityEdit, "keep"), "api");
            var restarted = new CommandProcessor(CommandRegistry.Builtin(), store, ctx, () => true, () => true, () => null);
            restarted.Poll().Should().Be(1);
            restarted.Pending.Should().ContainSingle();
            restarted.ApplyPending();
            store.GetResult("keep").Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void HandlerExceptionsBecomeFailures() {
            var reg = new CommandRegistry(new ICommandHandler[] { new Throwing() });
            var p = new CommandProcessor(reg, store, ctx, () => true, () => false, () => null);
            var r = p.Submit(Cmd.Of("boom", "{}"), "api");
            r.Status.Should().Be(CommandStatus.Failed);
            r.Message.Should().Be("kaput");
        }

        [Test]
        public void ResultsRoundTrip() {
            store.SaveResult(new CommandResult { Id = "z", Status = CommandStatus.Conflict, Message = "m", Detail = new { field = "priority" }, UpdatedAt = DateTime.UtcNow });
            var r = store.GetResult("z");
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Detail.ToString().Should().Contain("priority");
            store.RecentResults(5).Should().ContainSingle();
        }

        private class Throwing : ICommandHandler {
            public string Kind => "boom";
            public CommandTiming Timing => CommandTiming.Immediate;
            public bool IsEdit => false;

            public string Validate(CommandEnvelope command) => null;

            public CommandResult Apply(CommandEnvelope command, ICommandContext context) => throw new InvalidOperationException("kaput");
        }
    }
}
