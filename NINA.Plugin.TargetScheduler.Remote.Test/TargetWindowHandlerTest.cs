using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Text.Json.Nodes;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TargetWindowHandlerTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandRegistry registry;

        private const string Window = """{"start":"2026-10-13T00:30:00Z","end":"2026-10-14T12:00:00Z","priority":"normal","event_id":"mars-m-44-2026-10-13","ignore_moon":false}""";
        private const string SetWindow = """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","window":""" + Window + "}";

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

        private string Validate(string kind, string payload) {
            return registry.Get(kind).Validate(Cmd.Of(kind, payload));
        }

        private FakeControl Control => ctx.FakeControlInstance;

        private TargetWindowTable Load() {
            using var conn = db.Connect();
            return TargetWindowStore.Load(conn);
        }

        private static string CreateWith(Action<JsonNode> edit) {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            edit(payload["targets"][0]);
            return payload.ToJsonString();
        }

        private static string WindowWith(Action<JsonNode> edit) {
            return CreateWith(t => {
                t["window"] = JsonNode.Parse(Window);
                edit(t["window"]);
            });
        }

        [Test]
        public void TargetWindowIsARegisteredEdit() {
            var h = registry.Get("target.window");
            h.Should().NotBeNull();
            h.IsEdit.Should().BeTrue();
            h.Timing.Should().Be(CommandTiming.AtExposureEnd);
            Validate("target.window", SetWindow).Should().BeNull();
            Validate("target.window", """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","window":null}""").Should().BeNull();
        }

        [Test]
        public void TargetWindowValidates() {
            Validate("target.window", """{"target_id":13,"window":null}""").Should().Contain("target_guid");
            var bad = JsonNode.Parse(SetWindow);
            bad["window"]["end"] = "2026-10-13T00:30:00Z";
            Validate("target.window", bad.ToJsonString()).Should().Contain("before its end");
            bad["window"]["end"] = "2026-10-14 12:00";
            Validate("target.window", bad.ToJsonString()).Should().Contain("ISO 8601");
            bad["window"]["end"] = "2026-10-14T12:00:00Z";
            bad["window"]["priority"] = "urgent";
            Validate("target.window", bad.ToJsonString()).Should().Contain("normal or high");
        }

        [Test]
        public void ProjectCreateValidatesTheWindow() {
            Validate("project.create", WindowWith(w => { })).Should().BeNull();
            Validate("project.create", WindowWith(w => w["priority"] = "high")).Should().BeNull();
            Validate("project.create", WindowWith(w => w["priority"] = "HIGH")).Should().BeNull();
            Validate("project.create", WindowWith(w => w.AsObject().Remove("priority"))).Should().BeNull();
            Validate("project.create", WindowWith(w => w["start"] = "2026-10-13T00:30:00+02:00")).Should().BeNull();

            Validate("project.create", WindowWith(w => w["start"] = "2026-10-14T12:00:00Z")).Should().Contain("before its end");
            Validate("project.create", WindowWith(w => w["start"] = "2026-10-15T00:00:00Z")).Should().Contain("before its end");
            Validate("project.create", WindowWith(w => w["start"] = "2026-10-13T00:30:00")).Should().Contain("ISO 8601");
            Validate("project.create", WindowWith(w => w["start"] = "13 Oct 2026 00:30Z")).Should().Contain("ISO 8601");
            Validate("project.create", WindowWith(w => w["end"] = "")).Should().Contain("ISO 8601");
            Validate("project.create", WindowWith(w => w.AsObject().Remove("end"))).Should().Contain("ISO 8601");
            Validate("project.create", WindowWith(w => w["end"] = "2026-13-40T00:00:00Z")).Should().Contain("ISO 8601");
            Validate("project.create", WindowWith(w => w["end"] = "2027-11-17T00:30:00Z")).Should().BeNull();
            Validate("project.create", WindowWith(w => w["end"] = "2027-11-17T00:30:01Z")).Should().Contain("400 days");
            Validate("project.create", WindowWith(w => w["priority"] = "urgent")).Should().Contain("normal or high");
            Validate("project.create", WindowWith(w => w["event_id"] = new string('x', 201))).Should().Contain("event_id");
        }

        [Test]
        public void ProjectCreateValidatesTheKind() {
            foreach (var kind in new[] { "comet", "asteroid", "event", " Event " }) {
                Validate("project.create", CreateWith(t => { t["designation"] = "event:x"; t["kind"] = kind; })).Should().BeNull(kind);
            }
            Validate("project.create", CreateWith(t => { t["designation"] = "Mars"; t["kind"] = "planet"; })).Should().Contain("kind must be comet, asteroid or event");
            Validate("project.create", CreateWith(t => { t["designation"] = "Mars"; t["kind"] = ""; })).Should().BeNull();
        }

        [Test]
        public void ProjectCreateWritesKindAndWindowAndDeleteRemovesThem() {
            var payload = WindowWith(w => { w["priority"] = "high"; w["ignore_moon"] = true; w["start"] = "2026-10-13T02:30:00+02:00"; });
            var node = JsonNode.Parse(payload);
            node["targets"][0]["designation"] = "event:mars-m-44-2026-10-13";
            node["targets"][0]["kind"] = "event";
            payload = node.ToJsonString();

            Run("project.create", payload).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=tg|designation=event:mars-m-44-2026-10-13|kind=event|updated_at=2026-10-10T06:52:00Z");
            db.Dump("ts_target_window").Should().Be("target_guid=tg|start_utc=2026-10-13T00:30:00Z|end_utc=2026-10-14T12:00:00Z|priority=high|event_id=mars-m-44-2026-10-13|ignore_moon=1|updated_at=2026-10-10T06:52:00Z");
            var row = Load().Get("tg");
            row.StartUtc.Should().Be(new DateTime(2026, 10, 13, 0, 30, 0, DateTimeKind.Utc));
            row.IsHigh.Should().BeTrue();
            row.IgnoreMoon.Should().BeTrue();

            Run("project.delete", payload).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_target_body").Should().Be(0);
            db.Count("SELECT COUNT(*) FROM ts_target_window").Should().Be(0);
        }

        [Test]
        public void ProjectCreateWithADesignationAndNoKindStaysAComet() {
            Run("project.create", CreateWith(t => t["designation"] = "C/2025 R2")).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=tg|designation=C/2025 R2|kind=comet|updated_at=2026-10-10T06:52:00Z");
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_window'").Should().Be(0);
        }

        [Test]
        public void ProjectCreateWithoutTheNewFieldsWritesNoWindowRows() {
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('ts_target_window', 'ts_target_body')").Should().Be(0);
        }

        [Test]
        public void ProjectCreateWithAWindowOnlyWritesNoBodyRow() {
            Run("project.create", WindowWith(w => { })).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_body'").Should().Be(0);
            db.Count("SELECT COUNT(*) FROM ts_target_window").Should().Be(1);
        }

        [Test]
        public void ARejectedProjectCreateWritesNoWindow() {
            var payload = JsonNode.Parse(WindowWith(w => { }));
            payload["targets"][0]["plans"][0]["template_id"] = 999;
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Rejected);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_window'").Should().Be(0);
        }

        [Test]
        public void NewTargetKindAndWindowAreOptionalJson() {
            var t = new NewTarget { Guid = "tg", Name = "Gecko Nebula" };
            var json = System.Text.Json.JsonSerializer.Serialize(t);
            json.Should().NotContain("kind").And.NotContain("window");
            t.Kind = "event";
            t.Window = new NewTargetWindow { Start = "2026-10-13T00:30:00Z", End = "2026-10-14T12:00:00Z", Priority = "high", EventId = "e", IgnoreMoon = true };
            json = System.Text.Json.JsonSerializer.Serialize(t);
            json.Should().Contain("\"kind\":\"event\"").And.Contain("\"window\":{\"start\":\"2026-10-13T00:30:00Z\",\"end\":\"2026-10-14T12:00:00Z\",\"priority\":\"high\",\"event_id\":\"e\",\"ignore_moon\":true}");
        }

        [Test]
        public void TargetWindowSetsChangesPriorityAndClears() {
            var r = Run("target.window", SetWindow);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_window").Should().Be("target_guid=g13|start_utc=2026-10-13T00:30:00Z|end_utc=2026-10-14T12:00:00Z|priority=normal|event_id=mars-m-44-2026-10-13|ignore_moon=0|updated_at=2026-10-10T06:52:00Z");
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)13));
            Control.Replans.Should().HaveCount(1);

            var again = Run("target.window", SetWindow);
            again.Status.Should().Be(CommandStatus.Applied);
            again.Message.Should().Contain("already has this event window");
            Control.Replans.Should().HaveCount(1);

            var payload = JsonNode.Parse(SetWindow);
            payload["window"]["priority"] = "high";
            ctx.Now = ctx.Now.AddMinutes(5);
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_window").Should().Be("target_guid=g13|start_utc=2026-10-13T00:30:00Z|end_utc=2026-10-14T12:00:00Z|priority=high|event_id=mars-m-44-2026-10-13|ignore_moon=0|updated_at=2026-10-10T06:57:00Z");
            Load().Get("g13").IsHigh.Should().BeTrue();
            Control.Replans.Should().HaveCount(2);

            payload["window"] = null;
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_target_window").Should().Be(0);
            Control.Replans.Should().HaveCount(3);

            var cleared = Run("target.window", payload.ToJsonString());
            cleared.Status.Should().Be(CommandStatus.Applied);
            cleared.Message.Should().Contain("has no event window");
            Control.Replans.Should().HaveCount(3);
        }

        [Test]
        public void TheStackerUndoFieldBeforeIsIgnored() {
            var payload = JsonNode.Parse(SetWindow);
            payload["before"] = null;
            Validate("target.window", payload.ToJsonString()).Should().BeNull();
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_window").Should().Be("target_guid=g13|start_utc=2026-10-13T00:30:00Z|end_utc=2026-10-14T12:00:00Z|priority=normal|event_id=mars-m-44-2026-10-13|ignore_moon=0|updated_at=2026-10-10T06:52:00Z");

            payload["before"] = JsonNode.Parse(Window);
            payload["window"]["priority"] = "high";
            Validate("target.window", payload.ToJsonString()).Should().BeNull();
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            Load().Get("g13").IsHigh.Should().BeTrue();

            payload["before"] = payload["window"].DeepClone();
            payload["before"]["priority"] = "urgent";
            payload["before"]["start"] = "not a time";
            payload["window"] = null;
            Validate("target.window", payload.ToJsonString()).Should().BeNull();
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_target_window").Should().Be(0);
            Control.Replans.Should().HaveCount(3);
        }

        [Test]
        public void ANullIgnoreMoonMeansFalse() {
            var payload = JsonNode.Parse(SetWindow);
            payload["window"]["ignore_moon"] = null;
            Validate("target.window", payload.ToJsonString()).Should().BeNull();
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT ignore_moon FROM ts_target_window WHERE target_guid = 'g13'").Should().Be(0L);
            payload["window"].AsObject().Remove("ignore_moon");
            payload["window"]["priority"] = "high";
            Run("target.window", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            Load().Get("g13").IgnoreMoon.Should().BeFalse();
            Validate("project.create", WindowWith(w => w["ignore_moon"] = null)).Should().BeNull();
        }

        [Test]
        public void TargetBodyKeepsTheStoredKind() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["designation"] = "event:a";
            payload["targets"][0]["kind"] = "event";
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            Run("target.body", """{"target_guid":"tg","target_name":"Gecko Nebula","designation":"event:b"}""").Status.Should().Be(CommandStatus.Applied);
            db.Dump("ts_target_body").Should().Be("target_guid=tg|designation=event:b|kind=event|updated_at=2026-10-10T06:52:00Z");
        }

        [Test]
        public void ClearingWithoutTheTableChangesNothing() {
            var r = Run("target.window", """{"target_id":13,"target_guid":"g13","target_name":"Cygnis Loop Panel 2","window":null}""");
            r.Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_window'").Should().Be(0);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void TargetWindowRejectsAMissingOrMismatchedTarget() {
            var gone = Run("target.window", """{"target_guid":"nope","target_name":"Gone","window":""" + Window + "}");
            gone.Status.Should().Be(CommandStatus.Rejected);
            gone.Message.Should().Contain("no longer exists");
            var mismatch = JsonNode.Parse(SetWindow);
            mismatch["target_id"] = 12;
            Run("target.window", mismatch.ToJsonString()).Status.Should().Be(CommandStatus.Rejected);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_target_window'").Should().Be(0);
            Control.Replans.Should().BeEmpty();
        }
    }
}
