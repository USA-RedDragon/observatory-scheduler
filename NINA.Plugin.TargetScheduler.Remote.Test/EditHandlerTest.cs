using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class EditHandlerTest {
        private TestDb db;
        private FakeContext ctx;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
            ctx = new FakeContext { Database = new SqlSchedulerDatabase(db.Connect) };
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        [Test]
        public void ProjectPriorityApplies() {
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""name"":""Cygnis Loop"",""changes"":[{""field"":""priority"",""before"":1,""after"":0}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(0L);
            ctx.FakeControlInstance.Replans.Should().HaveCount(1);
            ctx.FakeControlInstance.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)null));
        }

        [Test]
        public void StaleBeforeIsAConflictAndChangesNothing() {
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""name"":""Cygnis Loop"",""changes"":[{""field"":""state"",""before"":1,""after"":2},{""field"":""priority"",""before"":2,""after"":0}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("priority is 1, not 2");
            db.Scalar("SELECT state FROM project WHERE Id = 5").Should().Be(1L);
            db.Scalar("SELECT priority FROM project WHERE Id = 5").Should().Be(1L);
            ctx.FakeControlInstance.Replans.Should().BeEmpty();
        }

        [Test]
        public void TargetActiveAcceptsBooleansAndReportsItsProject() {
            var r = EditHandler.Target().Apply(Cmd.Of(CommandKinds.TargetEdit, @"{""id"":13,""changes"":[{""field"":""active"",""before"":true,""after"":false}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT active FROM target WHERE Id = 13").Should().Be(0L);
            ctx.FakeControlInstance.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)13));
        }

        [Test]
        public void ExposurePlanLinksToTargetAndProject() {
            var r = EditHandler.ExposurePlan().Apply(Cmd.Of(CommandKinds.ExposurePlanEdit, @"{""id"":40,""changes"":[{""field"":""desired"",""before"":300,""after"":200},{""field"":""enabled"",""before"":1,""after"":0}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT desired FROM exposureplan WHERE Id = 40").Should().Be(200L);
            db.Scalar("SELECT enabled FROM exposureplan WHERE Id = 40").Should().Be(0L);
            db.Scalar("SELECT accepted FROM exposureplan WHERE Id = 40").Should().Be(180L);
            ctx.FakeControlInstance.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)13));
        }

        [Test]
        public void ProjectMinimumAltitudeIsReal() {
            db.Exec("ALTER TABLE project ADD COLUMN minimumaltitude REAL; UPDATE project SET minimumaltitude = 20 WHERE Id = 5");
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""minimumaltitude"",""before"":20,""after"":27.5}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT minimumaltitude FROM project WHERE Id = 5").Should().Be(27.5);
        }

        [Test]
        public void MissingRowIsRejected() {
            var r = EditHandler.Target().Apply(Cmd.Of(CommandKinds.TargetEdit, @"{""id"":999,""changes"":[{""field"":""active"",""before"":true,""after"":false}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Rejected);
        }

        [TestCase(@"{""id"":5,""changes"":[{""field"":""name"",""before"":""a"",""after"":""b""}]}", "not editable")]
        [TestCase(@"{""id"":5,""changes"":[]}", "no changes")]
        [TestCase(@"{""changes"":[{""field"":""priority"",""before"":1,""after"":0}]}", "missing id")]
        [TestCase(@"{""id"":5,""changes"":[{""field"":""priority"",""before"":1,""after"":7}]}", "between")]
        [TestCase(@"{""id"":5,""changes"":[{""field"":""priority"",""before"":1,""after"":""High""}]}", "whole number")]
        [TestCase(@"{""id"":5,""entity"":""target"",""changes"":[{""field"":""priority"",""before"":1,""after"":0}]}", "does not match")]
        [TestCase(@"{""id"":5,""changes"":[{""field"":""priority"",""before"":1,""after"":0},{""field"":""priority"",""before"":1,""after"":2}]}", "twice")]
        public void InvalidEditsAreRejected(string payload, string reason) {
            EditHandler.Project().Validate(Cmd.Of(CommandKinds.ProjectEdit, payload)).Should().Contain(reason);
        }
    }
}
