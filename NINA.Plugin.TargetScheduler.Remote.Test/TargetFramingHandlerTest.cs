using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TargetFramingHandlerTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandRegistry registry;

        private const string SetComet = """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","designation":"C/2025 R2"}""";
        private const string Tail = """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","framing":{"rotation_deg":84.06,"offset_frac":-0.25}}""";
        private const string Clear = """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","framing":null}""";

        [SetUp]
        public void SetUp() {
            db = new TestDb().WithPlanning();
            ctx = new FakeContext { Database = new SqlSchedulerDatabase(db.Connect) };
            registry = CommandRegistry.Builtin();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private CommandResult Run(string kind, string payload) {
            return registry.Get(kind).Apply(Cmd.Of(kind, payload), ctx);
        }

        private FakeControl Control => ctx.FakeControlInstance;

        private TargetBodyRow Row(string guid) {
            using var conn = db.Connect();
            return TargetBodyStore.Load(conn).Get(guid);
        }

        [Test]
        public void TargetFramingIsARegisteredEdit() {
            var h = registry.Get("target.framing");
            h.Should().NotBeNull();
            h.IsEdit.Should().BeTrue();
            h.Timing.Should().Be(CommandTiming.AtExposureEnd);
            h.Validate(Cmd.Of("target.framing", Tail)).Should().BeNull();
            h.Validate(Cmd.Of("target.framing", Clear)).Should().BeNull();
        }

        [Test]
        public void TargetFramingValidates() {
            var h = registry.Get("target.framing");
            h.Validate(Cmd.Of("target.framing", """{"framing":null}""")).Should().Contain("target_guid");
            h.Validate(Cmd.Of("target.framing", """{"target_guid":"g13","framing":{}}""")).Should().Contain("framing");
            h.Validate(Cmd.Of("target.framing", """{"target_guid":"g13","framing":{"rotation_deg":84}}""")).Should().Contain("together");
            h.Validate(Cmd.Of("target.framing", """{"target_guid":"g13","framing":{"rotation_deg":360,"offset_frac":0}}""")).Should().Contain("rotation_deg");
            h.Validate(Cmd.Of("target.framing", """{"target_guid":"g13","framing":{"rotation_deg":10,"offset_frac":0.6}}""")).Should().Contain("offset_frac");
        }

        [Test]
        public void SetsChangesAndClearsTheFramingOfABody() {
            Run("target.body", SetComet).Status.Should().Be(CommandStatus.Applied);
            Row("g13").Frame.Should().BeNull();
            int replans = Control.Replans.Count;

            var r = Run("target.framing", Tail);
            r.Status.Should().Be(CommandStatus.Applied);
            Row("g13").RotationDeg.Should().Be(84.06);
            Row("g13").OffsetFrac.Should().Be(-0.25);
            Row("g13").Designation.Should().Be("C/2025 R2");
            Control.Invalidated.Should().Contain(((int?)5, (int?)13));
            Control.Replans.Should().HaveCount(replans + 1);

            Run("target.framing", Tail).Message.Should().Contain("already has this framing");
            Control.Replans.Should().HaveCount(replans + 1);

            var custom = JsonNode.Parse(Tail);
            custom["framing"]["offset_frac"] = 0.1;
            Run("target.framing", custom.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            Row("g13").OffsetFrac.Should().Be(0.1);

            Run("target.framing", Clear).Status.Should().Be(CommandStatus.Applied);
            Row("g13").RotationDeg.Should().BeNull();
            Row("g13").OffsetFrac.Should().BeNull();
            Row("g13").Designation.Should().Be("C/2025 R2");
            Run("target.framing", Clear).Message.Should().Contain("already has no framing");
        }

        [Test]
        public void RejectsAFixedOrMissingTarget() {
            var fixedTarget = Run("target.framing", Tail);
            fixedTarget.Status.Should().Be(CommandStatus.Rejected);
            fixedTarget.Message.Should().Contain("not a moving body");
            Run("target.body", """{"target_id":12,"target_guid":"g12","designation":"2P"}""");
            Run("target.framing", Tail).Status.Should().Be(CommandStatus.Rejected);
            Run("target.framing", """{"target_guid":"nope","framing":null}""").Message.Should().Contain("no longer exists");
            Run("target.framing", """{"target_id":12,"target_guid":"g13","framing":null}""").Message.Should().Contain("no longer exists");
        }

        [Test]
        public void ADesignationChangeClearsTheFramingAndTheSameDesignationKeepsIt() {
            Run("target.body", SetComet);
            Run("target.framing", Tail);
            Run("target.body", SetComet).Message.Should().Contain("already follows");
            Row("g13").RotationDeg.Should().Be(84.06);

            var other = JsonNode.Parse(SetComet);
            other["designation"] = "161P";
            Run("target.body", other.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            Row("g13").Designation.Should().Be("161P");
            Row("g13").Frame.Should().BeNull();
        }

        [Test]
        public void ProjectCreateWritesTheFraming() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["designation"] = "161P";
            payload["targets"][0]["rotation_deg"] = 84.06;
            payload["targets"][0]["offset_frac"] = -0.25;
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().BeNull();
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            var row = Row("tg");
            row.Designation.Should().Be("161P");
            row.Frame.RotationDeg.Should().Be(84.06);
            row.Frame.OffsetFrac.Should().Be(-0.25);
        }

        [Test]
        public void ProjectCreateValidatesTheFraming() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["rotation_deg"] = 84.06;
            payload["targets"][0]["offset_frac"] = -0.25;
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().Contain("no designation");
            payload["targets"][0]["designation"] = "161P";
            payload["targets"][0]["offset_frac"] = 0.9;
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().Contain("offset_frac");
            payload["targets"][0]["offset_frac"] = null;
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().Contain("together");
        }

        [Test]
        public void NewTargetFramingIsOptionalJson() {
            var json = System.Text.Json.JsonSerializer.Serialize(new NewTarget { Guid = "tg", Name = "x" });
            json.Should().NotContain("rotation_deg").And.NotContain("offset_frac");
        }

        [Test]
        public void ALegacyTableGetsTheColumnsOnTheFirstFramingWrite() {
            db.Exec(@"CREATE TABLE ts_target_body (target_guid TEXT NOT NULL PRIMARY KEY, designation TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'comet', updated_at TEXT);
INSERT INTO ts_target_body (target_guid, designation) VALUES ('g13', 'C/2025 R2')");
            Row("g13").Frame.Should().BeNull();
            Run("target.framing", Tail).Status.Should().Be(CommandStatus.Applied);
            Row("g13").Frame.RotationDeg.Should().Be(84.06);
        }
    }
}
