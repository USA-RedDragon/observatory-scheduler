using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TemplateMigrationTest {
        private const string Profile = "2794f735-8a27-4d3a-ad20-9fe7ae159276";
        private static readonly DateTime Now = new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
        private static readonly string[] Tables = { "project", "target", "exposureplan", "exposuretemplate", "acquiredimage", "filtercadenceitem", "overrideexposureorderitem" };

        private TestDb db;
        private SqlSchedulerDatabase database;
        private FakeContext ctx;

        [SetUp]
        public void SetUp() {
            db = TestDb.RealSchema();
            db.Exec($@"INSERT INTO project (Id, profileId, name, state, priority, createdate, minimumtime, minimumaltitude, isMosaic, flatsHandling, guid) VALUES
  (1, '{Profile}', 'M31', 1, 1, 1700000000, 60, 20, 0, 0, 'pg1'),
  (2, '{Profile}', 'Heart', 1, 1, 1700000000, 60, 20, 0, 0, 'pg2');
INSERT INTO target (Id, projectid, name, active, ra, dec, epochcode, rotation, roi, guid) VALUES
  (101, 1, 'M31', 1, 0.712, 41.27, 2, 0, 100, 'tg101'),
  (102, 2, 'Heart', 1, 2.55, 61.45, 2, 0, 100, 'tg102');
INSERT INTO exposuretemplate (Id, profileId, name, filtername, gain, ""offset"", bin, readoutmode, twilightlevel, moonavoidanceenabled, moonavoidanceseparation, moonavoidancewidth, maximumhumidity, defaultexposure, moonrelaxscale, moonrelaxmaxaltitude, moonrelaxminaltitude, moondownenabled, ditherevery, minutesOffset, guid) VALUES
  (9, '{Profile}', 'Luminance', 'Luminance', 0, -1, 1, -1, 0, 1, 30, 7, 0, 300, 1, 5, 0, 1, -1, 0, 'tt9'),
  (10, '{Profile}', 'Red', 'Red', 0, -1, 1, -1, 0, 1, 30, 7, 0, 300, 1, 5, 0, 1, -1, 0, 'tt10'),
  (12, '{Profile}', 'Blue', 'Blue', 0, -1, 1, -1, 0, 1, 30, 7, 0, 300, 1, 5, 0, 1, -1, 0, 'tt12'),
  (14, '{Profile}', 'H-a', 'H-a', 100, -1, 1, -1, 2, 1, 30, 7, 0, 600, 0, 5, -15, 0, -1, 0, 'tt14'),
  (15, '{Profile}', 'O-III', 'O-III', 100, -1, 1, -1, 2, 1, 60, 7, 0, 600, 0, 5, -15, 0, -1, 0, 'tt15'),
  (20, '{Profile}', 'HA Low', 'H-a', 0, -1, 1, -1, 2, 1, 30, 7, 0, 120, 0, 5, -15, 0, -1, 0, 'tt20'),
  (21, '{Profile}', 'R 600', 'Red', 0, -1, 1, -1, 0, 1, 60, 7, 0, 600, 1, 5, 0, 1, -1, 0, 'tt21'),
  (25, '{Profile}', 'Red nautical', 'Red', 0, -1, 1, -1, 2, 1, 30, 7, 0, 300, 1, 5, 0, 1, -1, 0, 'tt25'),
  (31, '{Profile}', 'Unnamed', '', 0, -1, 1, -1, 0, 0, 0, 0, 0, 60, 0, 5, -15, 0, -1, 0, 'tt31');
INSERT INTO exposureplan (Id, profileId, targetid, exposureTemplateId, exposure, desired, acquired, accepted, enabled, guid) VALUES
  (1001, '{Profile}', 101, 10, -1, 50, 40, 31, 1, 'ep1001'),
  (1002, '{Profile}', 101, 21, -1, 20, 12, 9, 1, 'ep1002'),
  (1003, '{Profile}', 101, 9, 180, 80, 33, 30, 1, 'ep1003'),
  (1004, '{Profile}', 101, 14, -1, 60, 10, 10, 1, 'ep1004'),
  (1005, '{Profile}', 102, 20, -1, 10, 0, 0, 0, 'ep1005'),
  (1006, '{Profile}', 102, 12, -1, 40, 5, 5, 1, 'ep1006'),
  (1007, '{Profile}', 102, 999, -1, 10, 2, 2, 1, 'ep1007'),
  (1008, '{Profile}', 102, 31, -1, 10, 0, 0, 1, 'ep1008');
INSERT INTO acquiredimage (Id, profileId, projectId, targetId, exposureId, acquireddate, filtername, gradingStatus, metadata, guid) VALUES
  (1, '{Profile}', 1, 101, 1001, 1760000000, 'Red', 1, '{{}}', 'ai1'),
  (2, '{Profile}', 1, 101, 1002, 1760000300, 'Red', 2, '{{}}', 'ai2'),
  (3, '{Profile}', 1, 101, 1003, 1760000600, 'Luminance', 1, '{{}}', 'ai3'),
  (4, '{Profile}', 2, 102, 1006, 1760000900, 'Blue', 0, '{{}}', 'ai4'),
  (5, '{Profile}', 2, 102, 1007, 1760001200, 'H-a', 1, '{{}}', 'ai5');
INSERT INTO filtercadenceitem (targetid, ""order"", next, action, referenceIdx) VALUES (101, 1, 1, 0, 0), (101, 2, 0, 0, 1);
INSERT INTO overrideexposureorderitem (targetid, ""order"", action, referenceIdx) VALUES (101, 1, 0, 2);");
            database = new SqlSchedulerDatabase(db.Connect);
            ctx = new FakeContext { Database = database, ProfileId = Profile, Now = Now };
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private Dictionary<string, string> Snapshot() {
            var all = Tables.ToDictionary(t => t, t => db.Dump(t));
            all["sqlite_master"] = db.Dump("sqlite_master");
            foreach (var t in new[] { TemplateRoleStore.RoleTable, TemplateRoleStore.MigrationTable }) {
                if (db.Count($"SELECT COUNT(*) FROM sqlite_master WHERE name = '{t}'") > 0) {
                    all[t] = db.Dump(t);
                }
            }
            return all;
        }

        private long Managed(string filter) {
            return Convert.ToInt64(db.Scalar($"SELECT template_id FROM ts_template_role WHERE role = 'managed' AND filter = '{filter}'"));
        }

        private static Dictionary<string, object> Detail(ChangeOutcome o) {
            return (Dictionary<string, object>)o.Detail;
        }

        [Test]
        public void MigrationRemapsPlansAndKeepsEverythingElse() {
            var plansBefore = db.Dump("exposureplan", "exposureTemplateId");
            var images = db.Dump("acquiredimage");
            var oldTemplates = db.Dump("exposuretemplate");
            var cadence = db.Dump("filtercadenceitem") + db.Dump("overrideexposureorderitem");

            var o = database.MigrateTemplates(false, Now);

            o.Status.Should().Be(CommandStatus.Applied);
            o.Changed.Should().BeTrue();
            db.Dump("exposureplan", "exposureTemplateId").Should().Be(plansBefore);
            db.Dump("acquiredimage").Should().Be(images);
            db.Dump("exposuretemplate WHERE Id < 1000 AND Id IN (9, 10, 12, 14, 15, 20, 21, 25, 31)").Should().Be(oldTemplates);
            (db.Dump("filtercadenceitem") + db.Dump("overrideexposureorderitem")).Should().Be(cadence);

            db.Count("SELECT COUNT(*) FROM ts_template_role WHERE role = 'managed'").Should().Be(4);
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1001").Should().Be(Managed("Red"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1002").Should().Be(Managed("Red"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1003").Should().Be(Managed("Luminance"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1004").Should().Be(Managed("H-a"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1005").Should().Be(Managed("H-a"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1006").Should().Be(Managed("Blue"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1007").Should().Be(999L);
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1008").Should().Be(31L);
            db.Scalar("SELECT exposure FROM exposureplan WHERE Id = 1003").Should().Be(180.0);
            db.Scalar("SELECT enabled FROM exposureplan WHERE Id = 1005").Should().Be(0L);

            db.Dump($"exposuretemplate WHERE Id = {Managed("Red")}", "Id", "guid").Should().Be(
                $"profileId={Profile}|name=Red (auto)|filtername=Red|gain=0|offset=-1|bin=1|readoutmode=-1|twilightlevel=1|moonavoidanceenabled=0|moonavoidanceseparation=0|moonavoidancewidth=0|maximumhumidity=0|defaultexposure=300|moonrelaxscale=0|moonrelaxmaxaltitude=5|moonrelaxminaltitude=-15|moondownenabled=0|ditherevery=-1|minutesOffset=0");
            db.Scalar($"SELECT gain FROM exposuretemplate WHERE Id = {Managed("H-a")}").Should().Be(100L);
            db.Scalar($"SELECT defaultexposure FROM exposuretemplate WHERE Id = {Managed("H-a")}").Should().Be(600.0);
            db.Scalar($"SELECT name FROM exposuretemplate WHERE Id = {Managed("H-a")}").Should().Be("H-a (auto)");

            db.Dump("ts_template_migration", "migrated_at").Should().Be(string.Join("\n", new[] {
                $"plan_id=1001|plan_guid=ep1001|target_id=101|profile_id={Profile}|filter=Red|old_template_id=10|old_template_guid=tt10|old_template_name=Red|new_template_id={Managed("Red")}|reverted_at=null",
                $"plan_id=1002|plan_guid=ep1002|target_id=101|profile_id={Profile}|filter=Red|old_template_id=21|old_template_guid=tt21|old_template_name=R 600|new_template_id={Managed("Red")}|reverted_at=null",
                $"plan_id=1003|plan_guid=ep1003|target_id=101|profile_id={Profile}|filter=Luminance|old_template_id=9|old_template_guid=tt9|old_template_name=Luminance|new_template_id={Managed("Luminance")}|reverted_at=null",
                $"plan_id=1004|plan_guid=ep1004|target_id=101|profile_id={Profile}|filter=H-a|old_template_id=14|old_template_guid=tt14|old_template_name=H-a|new_template_id={Managed("H-a")}|reverted_at=null",
                $"plan_id=1005|plan_guid=ep1005|target_id=102|profile_id={Profile}|filter=H-a|old_template_id=20|old_template_guid=tt20|old_template_name=HA Low|new_template_id={Managed("H-a")}|reverted_at=null",
                $"plan_id=1006|plan_guid=ep1006|target_id=102|profile_id={Profile}|filter=Blue|old_template_id=12|old_template_guid=tt12|old_template_name=Blue|new_template_id={Managed("Blue")}|reverted_at=null",
            }));
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE migrated_at = '2026-10-12T18:00:00Z'").Should().Be(6);

            db.Dump("ts_template_role WHERE role = 'retired'", "changed_at", "template_guid").Should().Be(string.Join("\n", new[] {
                $"template_id=10|profile_id={Profile}|filter=Red|role=retired", $"template_id=12|profile_id={Profile}|filter=Blue|role=retired",
                $"template_id=14|profile_id={Profile}|filter=H-a|role=retired", $"template_id=15|profile_id={Profile}|filter=O-III|role=retired",
                $"template_id=20|profile_id={Profile}|filter=H-a|role=retired", $"template_id=21|profile_id={Profile}|filter=Red|role=retired",
                $"template_id=25|profile_id={Profile}|filter=Red|role=retired", $"template_id=31|profile_id={Profile}|filter=|role=retired",
                $"template_id=9|profile_id={Profile}|filter=Luminance|role=retired" }));

            var d = Detail(o);
            d["plans_remapped"].Should().Be(6);
            ((List<Dictionary<string, object>>)d["managed_created"]).Select(c => c["name"]).Should().BeEquivalentTo(new[] { "Red (auto)", "Luminance (auto)", "H-a (auto)", "Blue (auto)" });
            d["templates_retired"].Should().Be(9);
            var dup = ((List<Dictionary<string, object>>)d["duplicates"]).Should().ContainSingle().Subject;
            dup["target_id"].Should().Be(101L);
            dup["filter"].Should().Be("Red");
            ((List<long>)dup["plan_ids"]).Should().Equal(1001L, 1002L);
            ((List<Dictionary<string, object>>)d["skipped"]).Select(s => (s["plan_id"], s["reason"])).Should().BeEquivalentTo(new[] {
                ((object)1007L, (object)"template missing"), ((object)1008L, (object)"template has no filter") });
            ((List<Dictionary<string, object>>)d["per_old_template"]).Select(p => (p["template_id"], p["plans"])).Should().BeEquivalentTo(new[] {
                ((object)9L, (object)1), (10L, 1), (12L, 1), (14L, 1), (20L, 1), (21L, 1) });
            o.Touched.Should().BeEquivalentTo(new[] { ((int?)1, (int?)101), ((int?)2, (int?)102) });
        }

        [Test]
        public void SecondMigrationIsANoOp() {
            database.MigrateTemplates(false, Now);
            var after = Snapshot();

            var o = database.MigrateTemplates(false, Now.AddHours(1));

            o.Changed.Should().BeFalse();
            o.Message.Should().StartWith("Nothing to migrate: 6 plans are already on managed templates");
            Detail(o)["plans_remapped"].Should().Be(0);
            Detail(o)["templates_already_retired"].Should().Be(9);
            Snapshot().Should().Equal(after);
        }

        [Test]
        public void DryRunWritesNothing() {
            var before = Snapshot();

            var o = database.MigrateTemplates(true, Now);

            Snapshot().Should().Equal(before);
            o.Changed.Should().BeFalse();
            o.Message.Should().StartWith("Dry run, nothing was written: would remap 6 plans, create 4 managed templates and retire 9 templates.");
            Detail(o)["plans_remapped"].Should().Be(6);
            ((List<Dictionary<string, object>>)Detail(o)["managed_created"]).Should().OnlyContain(c => c["template_id"] == null);

            database.MigrateTemplates(false, Now);
            var migrated = Snapshot();
            var r = database.RevertTemplates(true, Now);
            Snapshot().Should().Equal(migrated);
            Detail(r)["plans_reverted"].Should().Be(6);
            Detail(r)["retired_roles_removed"].Should().Be(9L);
        }

        [Test]
        public void RevertRestoresThePlansExactly() {
            var before = db.Dump("exposureplan");
            var images = db.Dump("acquiredimage");
            database.MigrateTemplates(false, Now);
            var managedTemplates = db.Dump("exposuretemplate");

            var r = database.RevertTemplates(false, Now.AddDays(1));

            r.Changed.Should().BeTrue();
            db.Dump("exposureplan").Should().Be(before);
            db.Dump("acquiredimage").Should().Be(images);
            db.Dump("exposuretemplate").Should().Be(managedTemplates);
            db.Count("SELECT COUNT(*) FROM ts_template_role WHERE role = 'retired'").Should().Be(0);
            db.Count("SELECT COUNT(*) FROM ts_template_role WHERE role = 'managed'").Should().Be(4);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE reverted_at = '2026-10-13T18:00:00Z'").Should().Be(6);
            Detail(r)["plans_reverted"].Should().Be(6);

            var after = Snapshot();
            var again = database.RevertTemplates(false, Now.AddDays(2));
            again.Changed.Should().BeFalse();
            again.Message.Should().StartWith("Nothing to revert");
            Snapshot().Should().Equal(after);
        }

        [Test]
        public void MigrationHistoryIsKeptAcrossRevertAndRemigrate() {
            database.MigrateTemplates(false, Now);
            database.RevertTemplates(false, Now.AddHours(1));
            database.MigrateTemplates(false, Now.AddHours(2)).Changed.Should().BeTrue();

            db.Count("SELECT COUNT(*) FROM ts_template_migration").Should().Be(12);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE migrated_at = '2026-10-12T18:00:00Z' AND reverted_at = '2026-10-12T19:00:00Z'").Should().Be(6);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE migrated_at = '2026-10-12T20:00:00Z' AND reverted_at IS NULL").Should().Be(6);

            database.RevertTemplates(false, Now.AddHours(3));
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE migrated_at = '2026-10-12T18:00:00Z' AND reverted_at = '2026-10-12T19:00:00Z'").Should().Be(6);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE migrated_at = '2026-10-12T20:00:00Z' AND reverted_at = '2026-10-12T21:00:00Z'").Should().Be(6);
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1002").Should().Be(21L);
        }

        [Test]
        public void RevertSkipsPlansThatMovedOn() {
            database.MigrateTemplates(false, Now);
            db.Exec("UPDATE exposureplan SET exposureTemplateId = 25 WHERE Id = 1001");
            db.Exec("DELETE FROM exposureplan WHERE Id = 1006");
            db.Exec("DELETE FROM exposuretemplate WHERE Id = 9");

            var r = database.RevertTemplates(false, Now);

            Detail(r)["plans_reverted"].Should().Be(3);
            ((List<Dictionary<string, object>>)Detail(r)["skipped"]).Select(s => (s["plan_id"], s["reason"])).Should().BeEquivalentTo(new[] {
                ((object)1001L, (object)"plan no longer uses the managed template"), (1003L, "old template no longer exists"), (1006L, "plan no longer exists") });
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1001").Should().Be(25L);
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1002").Should().Be(21L);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE reverted_at IS NULL").Should().Be(3);
        }

        [Test]
        public void RetiredTemplatesNeededForARevertCannotBeDeleted() {
            database.MigrateTemplates(false, Now);
            var r = database.DeleteTemplate(new TemplateDeletePayload { Guid = "tt21", Name = "R 600" });
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Be("R 600 is needed to revert the template migration, so it was not removed.");
            database.DeleteTemplate(new TemplateDeletePayload { Guid = "tt15", Name = "O-III" }).Status.Should().Be(CommandStatus.Applied);
            database.RevertTemplates(false, Now);
            database.DeleteTemplate(new TemplateDeletePayload { Guid = "tt21", Name = "R 600" }).Status.Should().Be(CommandStatus.Rejected);
            db.Exec("DELETE FROM exposureplan WHERE Id = 1002");
            database.DeleteTemplate(new TemplateDeletePayload { Guid = "tt21", Name = "R 600" }).Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void ConcurrentCountChangeIsNotLost() {
            database.AfterMigrationRead = exec => {
                exec("UPDATE exposureplan SET accepted = 32, acquired = 41 WHERE Id = 1001");
                exec("UPDATE exposureplan SET exposureTemplateId = 25 WHERE Id = 1002");
            };

            var o = database.MigrateTemplates(false, Now);

            db.Dump("exposureplan WHERE Id = 1001", "exposureTemplateId").Should().Be("Id=1001|profileId=2794f735-8a27-4d3a-ad20-9fe7ae159276|exposure=-1|desired=50|acquired=41|accepted=32|targetid=101|enabled=1|guid=ep1001");
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1001").Should().Be(Managed("Red"));
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE Id = 1002").Should().Be(25L);
            db.Count("SELECT COUNT(*) FROM ts_template_migration WHERE plan_id = 1002").Should().Be(0);
            ((List<Dictionary<string, object>>)Detail(o)["skipped"]).Should().Contain(s => (long)s["plan_id"] == 1002L && (string)s["reason"] == "plan changed during the migration");
        }

        [Test]
        public void PlansMadeOnManagedTemplatesSurviveARevert() {
            database.MigrateTemplates(false, Now);
            var payload = """{"targets":[{"target_id":102,"target_guid":"tg102","create":[{"guid":"new1","template_id":14,"filter":"O-III","exposure":-1,"desired":30}]}],"mode":"add","set_name":"OIII"}""";
            var r = CommandRegistry.Builtin().Get(CommandKinds.ApplySet).Apply(Cmd.Of(CommandKinds.ApplySet, payload), ctx);
            r.Status.Should().Be(CommandStatus.Applied);
            var oiii = Managed("O-III");
            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE guid = 'new1'").Should().Be(oiii);

            database.RevertTemplates(false, Now);

            db.Scalar("SELECT exposureTemplateId FROM exposureplan WHERE guid = 'new1'").Should().Be(oiii);
            db.Scalar($"SELECT role FROM ts_template_role WHERE template_id = {oiii}").Should().Be("managed");
        }

        [Test]
        public void CommandsRunThroughThePlanningHandler() {
            var registry = CommandRegistry.Builtin();
            var migrate = registry.Get(CommandKinds.TemplatesMigrate);
            migrate.IsEdit.Should().BeTrue();
            migrate.Timing.Should().Be(CommandTiming.AtExposureEnd);
            registry.Get(CommandKinds.TemplatesRevert).IsEdit.Should().BeTrue();

            var dry = migrate.Apply(Cmd.Of(CommandKinds.TemplatesMigrate, """{"dry_run":true}"""), ctx);
            dry.Status.Should().Be(CommandStatus.Applied);
            dry.Detail.Should().NotBeNull();
            ctx.FakeControlInstance.Replans.Should().BeEmpty();

            var real = migrate.Apply(Cmd.Of(CommandKinds.TemplatesMigrate, """{"dry_run":false}"""), ctx);
            real.Status.Should().Be(CommandStatus.Applied);
            real.Message.Should().StartWith("Remapped 6 plans, created 4 managed templates and retired 9 templates.");
            ctx.FakeControlInstance.Replans.Should().ContainSingle();
            ctx.FakeControlInstance.Invalidated.Should().HaveCount(2);

            var revert = registry.Get(CommandKinds.TemplatesRevert).Apply(Cmd.Of(CommandKinds.TemplatesRevert, "{}"), ctx);
            revert.Status.Should().Be(CommandStatus.Applied);
            revert.Message.Should().StartWith("Reverted 6 plans and un-retired 9 templates");
            ctx.FakeControlInstance.Replans.Should().HaveCount(2);

            migrate.Validate(Cmd.Of(CommandKinds.TemplatesMigrate, """{"dry_run":"yes"}""")).Should().StartWith("invalid payload");
        }

        [Test]
        public void StoreLoadsTheRoles() {
            using (var conn = db.Connect()) {
                TemplateRoleStore.Load(conn).Should().BeSameAs(ManagedTemplateTable.Empty);
            }
            database.MigrateTemplates(false, Now);
            ManagedTemplateTable table;
            using (var conn = db.Connect()) {
                TemplateRoleStore.Ensure(conn);
                TemplateRoleStore.Ensure(conn);
                table = TemplateRoleStore.Load(conn);
            }
            table.ManagedCount.Should().Be(4);
            table.IsManaged((int)Managed("Red")).Should().BeTrue();
            table.IsRetired(10).Should().BeTrue();
            table.IsManaged(10).Should().BeFalse();
            table.HasManaged(Profile).Should().BeTrue();
            table.HasManaged("other").Should().BeFalse();
        }
    }
}
