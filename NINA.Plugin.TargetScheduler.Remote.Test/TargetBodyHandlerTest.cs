using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System.Linq;
using System.Text.Json.Nodes;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TargetBodyHandlerTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandRegistry registry;

        private const string SetComet = """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","designation":" C/2025 R2 "}""";

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
            var h = registry.Get(kind);
            h.Should().NotBeNull(kind);
            return h.Apply(Cmd.Of(kind, payload), ctx);
        }

        private FakeControl Control => ctx.FakeControlInstance;

        private TargetBodyTable Load() {
            using var conn = db.Connect();
            return TargetBodyStore.Load(conn);
        }

        [Test]
        public void TargetBodyIsARegisteredEdit() {
            registry.Get("target.body").Should().NotBeNull();
            registry.Get("target.body").IsEdit.Should().BeTrue();
            registry.Get("target.body").Timing.Should().Be(CommandTiming.AtExposureEnd);
            registry.Get("target.body").Validate(Cmd.Of("target.body", SetComet)).Should().BeNull();
        }

        [Test]
        public void TargetBodyValidates() {
            registry.Get("target.body").Validate(Cmd.Of("target.body", """{"target_id":13,"designation":"C/2025 R2"}""")).Should().Contain("target_guid");
            registry.Get("target.body").Validate(Cmd.Of("target.body", $$"""{"target_guid":"g13","designation":"{{new string('x', 201)}}"}""")).Should().Contain("designation");
        }

        [Test]
        public void TargetBodySetsChangesAndClearsTheDesignation() {
            var r = Run("target.body", SetComet);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=g13|designation=C/2025 R2|kind=comet|updated_at=2026-10-10T06:52:00Z");
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)13));
            Control.Replans.Should().HaveCount(1);
            Load().Designation("g13").Should().Be("C/2025 R2");

            var again = Run("target.body", SetComet);
            again.Status.Should().Be(CommandStatus.Applied);
            again.Message.Should().Contain("already follows C/2025 R2");
            Control.Replans.Should().HaveCount(1);

            var payload = JsonNode.Parse(SetComet);
            payload["designation"] = "  c/2025 r2";
            Run("target.body", payload.ToJsonString()).Message.Should().Contain("already follows");
            db.Dump("ts_target_body").Should().Contain("designation=C/2025 R2|");
            Control.Replans.Should().HaveCount(1);

            payload["designation"] = "DES=2P;CAP;NOFRAG";
            Run("target.body", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=g13|designation=DES=2P;CAP;NOFRAG|kind=comet|updated_at=2026-10-10T06:52:00Z");
            Control.Replans.Should().HaveCount(2);

            payload["designation"] = "  ";
            Run("target.body", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_target_body").Should().Be(0);
            Control.Replans.Should().HaveCount(3);

            payload["designation"] = null;
            var cleared = Run("target.body", payload.ToJsonString());
            cleared.Status.Should().Be(CommandStatus.Applied);
            cleared.Message.Should().Contain("already a fixed target");
            Control.Replans.Should().HaveCount(3);
        }

        [Test]
        public void ClearingWithoutTheTableChangesNothing() {
            var r = Run("target.body", """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","designation":null}""");
            r.Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_body'").Should().Be(0);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void TargetBodyRejectsAMissingOrMismatchedTarget() {
            Run("target.body", """{"target_guid":"nope","target_name":"Gone","designation":"C/2025 R2"}""").Status.Should().Be(CommandStatus.Rejected);
            var mismatch = Run("target.body", """{"target_id":12,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","designation":"C/2025 R2"}""");
            mismatch.Status.Should().Be(CommandStatus.Rejected);
            mismatch.Message.Should().Contain("no longer exists");
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_body'").Should().Be(0);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void ProjectCreateWritesTheDesignationAndDeleteRemovesIt() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["designation"] = "C/2025 R2";
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=tg|designation=C/2025 R2|kind=comet|updated_at=2026-10-10T06:52:00Z");
            Load().Designation("tg").Should().Be("C/2025 R2");

            Run("project.delete", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_target_body").Should().Be(0);
        }

        [Test]
        public void ProjectCreateWithoutADesignationLeavesTheTableAlone() {
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_body'").Should().Be(0);
        }

        [Test]
        public void ProjectCreateRejectsAnOverlongDesignation() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["designation"] = new string('x', 201);
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().Contain("designation");
        }

        [Test]
        public void NewTargetDesignationIsOptionalJson() {
            var t = new NewTarget { Guid = "tg", Name = "Gecko Nebula" };
            System.Text.Json.JsonSerializer.Serialize(t).Should().NotContain("designation");
            t.Designation = "C/2025 R2";
            System.Text.Json.JsonSerializer.Serialize(t).Should().Contain("\"designation\":\"C/2025 R2\"");
        }
    }
}
