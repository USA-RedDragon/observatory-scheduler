using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class ManagedTemplateCommandTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandRegistry registry;

        private static readonly string[] Tables = { "project", "target", "exposureplan", "exposuretemplate", "ruleweight", "ts_goal" };

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

        private static string Create(string project, string target, string plan, string filter, int templateId = 1) {
            var filterField = filter == null ? "" : $",\"filter\":\"{filter}\"";
            return $$"""{"project":{"guid":"{{project}}","name":"{{project}}","priority":1,"state":1,"minimumtime":60,"minimumaltitude":15},"targets":[{"guid":"{{target}}","name":"{{target}}","ra_hours":5.5,"dec":-5.4,"rotation":0,"plans":[{"guid":"{{plan}}","template_id":{{templateId}}{{filterField}},"exposure":-1,"desired":40}]}]}""";
        }

        private Dictionary<string, string> Snapshot() {
            var all = Tables.ToDictionary(t => t, t => db.Dump(t));
            all["sqlite_master"] = db.Dump("sqlite_master");
            return all;
        }

        private long PlanTemplate(string guid) {
            return System.Convert.ToInt64(db.Scalar($"SELECT exposureTemplateId FROM exposureplan WHERE guid = '{guid}'"));
        }

        [Test]
        public void ProjectCreateWithAFilterCreatesOneManagedTemplateAndReusesIt() {
            var first = Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "H-a"));
            first.Status.Should().Be(CommandStatus.Applied);
            first.Message.Should().Contain("Created managed template H-a (auto).");
            var id = PlanTemplate("pla");
            id.Should().NotBe(1);
            db.Dump($"exposuretemplate WHERE Id = {id}", "Id", "guid").Should().Be(
                "profileId=p|name=H-a (auto)|filtername=H-a|gain=100|offset=-1|bin=1|readoutmode=-1|twilightlevel=2|moonavoidanceenabled=0|moonavoidanceseparation=0|moonavoidancewidth=0|maximumhumidity=0|defaultexposure=600|moonrelaxscale=0|moonrelaxmaxaltitude=5|moonrelaxminaltitude=-15|moondownenabled=0|ditherevery=-1|minutesOffset=0");
            db.Dump("ts_template_role", "changed_at", "template_guid").Should().Be($"template_id={id}|profile_id=p|filter=H-a|role=managed");

            var second = Run(CommandKinds.ProjectCreate, Create("pb", "tb", "plb", "H-a", 3));
            second.Status.Should().Be(CommandStatus.Applied);
            (second.Message ?? "").Should().NotContain("Created managed");
            PlanTemplate("plb").Should().Be(id);
            db.Count("SELECT COUNT(*) FROM exposuretemplate WHERE name = 'H-a (auto)'").Should().Be(1);
            db.Count("SELECT COUNT(*) FROM ts_template_role").Should().Be(1);
        }

        [Test]
        public void ProjectCreateWithoutAFilterKeepsTheTemplatePath() {
            Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", null, 4)).Status.Should().Be(CommandStatus.Applied);
            PlanTemplate("pla").Should().Be(4);
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE name = 'ts_template_role'").Should().Be(0);
        }

        [Test]
        public void UnknownFilterIsRejectedAndNothingIsWritten() {
            var before = Snapshot();
            var r = Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "Sii"));
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Be("Sii is not a filter of this profile. Known filters: H-a, L, O-III.");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void FilterWheelFiltersOfTheActiveProfileAreAccepted() {
            ctx.ProfileFilters = new List<string> { "Red", "Green" };
            Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "red")).Status.Should().Be(CommandStatus.Applied);
            var id = PlanTemplate("pla");
            db.Scalar($"SELECT name FROM exposuretemplate WHERE Id = {id}").Should().Be("Red (auto)");
            db.Scalar($"SELECT gain FROM exposuretemplate WHERE Id = {id}").Should().Be(0L);
            db.Scalar($"SELECT defaultexposure FROM exposuretemplate WHERE Id = {id}").Should().Be(300.0);

            ctx.ProfileId = "other";
            var r = Run(CommandKinds.ApplySet, """{"set_name":"G","mode":"add","targets":[{"target_id":29,"create":[{"guid":"g1","template_id":1,"filter":"Green","exposure":-1,"desired":10}]}]}""");
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().StartWith("Target 29: Green is not a filter of this profile.");
        }

        [Test]
        public void ApplySetWithAFilterUsesTheManagedTemplateAndUndoRemovesThePlan() {
            var payload = """{"set_name":"HOO","mode":"add","targets":[{"target_id":29,"target_guid":"g29","create":[{"guid":"s1","template_id":1,"filter":"O-III","exposure":-1,"desired":10}]}]}""";
            var r = Run(CommandKinds.ApplySet, payload);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().Be("Created managed template O-III (auto). The profile is not migrated to managed templates, but plans that name a filter use the managed template of that filter.");
            var id = PlanTemplate("s1");
            db.Scalar($"SELECT name FROM exposuretemplate WHERE Id = {id}").Should().Be("O-III (auto)");
            Run(CommandKinds.ApplySet, payload).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposureplan WHERE guid = 's1'").Should().Be(1);

            Run(CommandKinds.UnapplySet, payload).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposureplan WHERE guid = 's1'").Should().Be(0);
            db.Count($"SELECT COUNT(*) FROM exposuretemplate WHERE Id = {id}").Should().Be(1);
        }

        [Test]
        public void ManagedTemplatesCannotBeEditedClonedOrDeleted() {
            Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "L")).Status.Should().Be(CommandStatus.Applied);
            var id = PlanTemplate("pla");
            var before = Snapshot();

            var edit = Run(CommandKinds.ExposureTemplateEdit, $$"""{"id":{{id}},"name":"L (auto)","changes":[{"field":"gain","before":0,"after":100}]}""");
            edit.Status.Should().Be(CommandStatus.Rejected);
            edit.Message.Should().Be("L (auto): it is managed by the scheduler, so it cannot be edited. Nothing was applied.");

            var batch = Run(CommandKinds.ExposureTemplateBatchEdit, $$"""{"items":[{"id":3,"name":"H-a","changes":[{"field":"gain","before":100,"after":0}]},{"id":{{id}},"name":"L (auto)","changes":[{"field":"gain","before":0,"after":100}]}]}""");
            batch.Status.Should().Be(CommandStatus.Rejected);
            batch.Message.Should().Contain("L (auto): it is managed by the scheduler, so it cannot be edited");

            var clone = Run(CommandKinds.ExposureTemplateClone, $$"""{"source_id":{{id}},"source_name":"L (auto)","guid":"c1","name":"L copy"}""");
            clone.Status.Should().Be(CommandStatus.Rejected);
            clone.Message.Should().Be("L (auto): it is managed by the scheduler, so it cannot be cloned.");

            var guid = db.Scalar($"SELECT guid FROM exposuretemplate WHERE Id = {id}");
            var delete = Run(CommandKinds.ExposureTemplateDelete, $$"""{"guid":"{{guid}}","name":"L (auto)"}""");
            delete.Status.Should().Be(CommandStatus.Rejected);
            delete.Message.Should().Be("L (auto): it is managed by the scheduler, so it cannot be deleted.");

            Snapshot().Should().Equal(before);
            Run(CommandKinds.ExposureTemplateEdit, """{"id":3,"name":"H-a","changes":[{"field":"gain","before":100,"after":0}]}""").Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void FilterSpellingFollowsTheWheelAndManagedLookupIgnoresCase() {
            ctx.ProfileFilters = new List<string> { "Red ", "H-a" };
            Run(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "red")).Status.Should().Be(CommandStatus.Applied);
            db.Scalar($"SELECT filtername FROM exposuretemplate WHERE Id = {PlanTemplate("pla")}").Should().Be("Red ");

            Run(CommandKinds.ProjectCreate, Create("pb", "tb", "plb", "H-a")).Status.Should().Be(CommandStatus.Applied);
            ctx.ProfileFilters = new List<string>();
            db.Exec("UPDATE exposuretemplate SET filtername = 'h-a' WHERE Id = 3");
            Run(CommandKinds.ProjectCreate, Create("pc", "tc", "plc", "h-a")).Status.Should().Be(CommandStatus.Applied);
            PlanTemplate("plc").Should().Be(PlanTemplate("plb"));
            db.Count("SELECT COUNT(*) FROM ts_template_role WHERE role = 'managed'").Should().Be(2);
        }

        [Test]
        public void GoalsFollowTheResolvedFilter() {
            var plan = Create("pa", "ta", "pla", "h-a");
            var payload = plan.Substring(0, plan.Length - 1) + ",\"goals\":[{\"target_guid\":\"ta\",\"filter\":\"h-a\",\"setting\":{\"kind\":0,\"snr_goal\":10,\"plateau_stop\":true}}]}";
            Run(CommandKinds.ProjectCreate, payload).Status.Should().Be(CommandStatus.Applied);
            db.Scalar($"SELECT filtername FROM exposuretemplate WHERE Id = {PlanTemplate("pla")}").Should().Be("H-a");
            db.Count("SELECT COUNT(*) FROM ts_goal WHERE target_guid = 'ta' AND filter = 'H-a'").Should().Be(1);
        }

        [Test]
        public void PlansNeedATemplateIdOrAFilter() {
            var h = registry.Get(CommandKinds.ProjectCreate);
            h.Validate(Cmd.Of(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", "H-a", 0))).Should().BeNull();
            h.Validate(Cmd.Of(CommandKinds.ProjectCreate, Create("pa", "ta", "pla", null, 0))).Should().Be("a new plan needs guid, template_id or filter, and desired");
            registry.Get(CommandKinds.ApplySet).Validate(Cmd.Of(CommandKinds.ApplySet, """{"mode":"add","targets":[{"target_id":29,"create":[{"guid":"x","filter":" ","desired":1}]}]}"""))
                .Should().Be("a new plan needs guid, template_id or filter, and desired");
        }

        [Test]
        public void ManagedSettingsFollowTheFilterKind() {
            ManagedTemplateSpec.Name("Red").Should().Be("Red (auto)");
            foreach (var f in new[] { "Luminance", "Red", "Green", "Blue", "L", "UV/IR cut" }) {
                ManagedTemplateSpec.Gain(f).Should().Be(0, f);
                ManagedTemplateSpec.DefaultExposure(f).Should().Be(300, f);
                ManagedTemplateSpec.TwilightLevel(f).Should().Be(1, f);
            }
            foreach (var f in new[] { "H-a", "O-III", "S-II", "Ha 3nm" }) {
                ManagedTemplateSpec.Gain(f).Should().Be(100, f);
                ManagedTemplateSpec.DefaultExposure(f).Should().Be(600, f);
                ManagedTemplateSpec.TwilightLevel(f).Should().Be(2, f);
            }
        }
    }
}
