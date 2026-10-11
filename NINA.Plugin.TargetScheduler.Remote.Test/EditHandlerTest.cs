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
        public void ProjectSchedulingSettingsApply() {
            db.WithPlanning();
            db.Exec("UPDATE project SET ditherevery = 0, filterswitchfrequency = 0, flatsHandling = 0, usecustomhorizon = 0, horizonoffset = 0, meridianwindow = 0 WHERE Id = 5");
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""ditherevery"",""before"":0,""after"":2},{""field"":""filterswitchfrequency"",""before"":0,""after"":1},{""field"":""flatshandling"",""before"":0,""after"":100},{""field"":""smartexposureorder"",""before"":false,""after"":true},{""field"":""meridianwindow"",""before"":0,""after"":90},{""field"":""usecustomhorizon"",""before"":false,""after"":true},{""field"":""horizonoffset"",""before"":0,""after"":2.5},{""field"":""maximumaltitude"",""before"":0,""after"":80},{""field"":""enablegrader"",""before"":true,""after"":false}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Dump("project WHERE Id = 5", "Id").Should().Contain("flatsHandling=100").And.Contain("usecustomhorizon=1").And.Contain("horizonoffset=2.5").And.Contain("meridianwindow=90")
                .And.Contain("filterswitchfrequency=1").And.Contain("ditherevery=2").And.Contain("enablegrader=0").And.Contain("maximumAltitude=80").And.Contain("smartexposureorder=1");
        }

        [Test]
        public void ProjectFlatsHandlingTakesUseWithImmediate() {
            db.WithPlanning();
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""flatshandling"",""before"":0,""after"":200}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT flatsHandling FROM project WHERE Id = 5").Should().Be(200L);
        }

        [Test]
        public void ProjectDitherEditConflictsWhenTheObservatoryChangedIt() {
            db.WithPlanning();
            db.Exec("UPDATE project SET ditherevery = 3 WHERE Id = 5");
            var r = EditHandler.Project().Apply(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""ditherevery"",""before"":0,""after"":2}]}"), ctx);
            r.Status.Should().Be(CommandStatus.Conflict);
            db.Scalar("SELECT ditherevery FROM project WHERE Id = 5").Should().Be(3L);
        }

        [TestCase("ditherevery", "1000", "between 0 and 999")]
        [TestCase("ditherevery", "-1", "between 0 and 999")]
        [TestCase("ditherevery", "1.5", "whole number")]
        [TestCase("filterswitchfrequency", "1000", "between 0 and 999")]
        [TestCase("flatshandling", "150", "between 0 and 99 or 100 or 200")]
        [TestCase("flatshandling", "-1", "between")]
        [TestCase("flatshandling", "201", "between")]
        [TestCase("meridianwindow", "721", "between 0 and 720")]
        [TestCase("horizonoffset", "90.5", "between 0 and 90")]
        [TestCase("maximumaltitude", "-5", "between 0 and 90")]
        [TestCase("smartexposureorder", "2", "true or false")]
        [TestCase("usecustomhorizon", "\"yes\"", "true or false")]
        [TestCase("enablegrader", "1.5", "true or false")]
        public void ProjectSchedulingSettingsAreValidated(string field, string after, string reason) {
            var before = field is "smartexposureorder" or "usecustomhorizon" or "enablegrader" ? "false" : "0";
            EditHandler.Project().Validate(Cmd.Of(CommandKinds.ProjectEdit, $@"{{""id"":5,""changes"":[{{""field"":""{field}"",""before"":{before},""after"":{after}}}]}}")).Should().Contain(reason);
        }

        [TestCase("flatshandling", "14")]
        [TestCase("flatshandling", "100")]
        [TestCase("flatshandling", "200")]
        [TestCase("ditherevery", "999")]
        [TestCase("maximumaltitude", "0")]
        [TestCase("enablegrader", "1")]
        public void ProjectSchedulingSettingsAcceptValidValues(string field, string after) {
            EditHandler.Project().Validate(Cmd.Of(CommandKinds.ProjectEdit, $@"{{""id"":5,""changes"":[{{""field"":""{field}"",""before"":0,""after"":{after}}}]}}")).Should().BeNull();
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
