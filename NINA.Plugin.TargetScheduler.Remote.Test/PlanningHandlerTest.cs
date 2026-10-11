using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class PlanningHandlerTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandRegistry registry;

        private static readonly string[] AllTables = {
            "project", "target", "exposureplan", "exposuretemplate", "ruleweight", "ts_goal", "acquiredimage", "filtercadenceitem", "overrideexposureorderitem", "flathistory"
        };

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

        private Dictionary<string, string> Snapshot(params string[] skip) {
            return AllTables.ToDictionary(t => t, t => db.Dump(t, skip));
        }

        private static string Swap(string json) {
            var node = JsonNode.Parse(json);
            SwapNode(node);
            return node.ToJsonString();
        }

        private static void SwapNode(JsonNode node) {
            if (node is JsonObject o) {
                if (o.ContainsKey("before") && o.ContainsKey("after")) {
                    var b = o["before"]?.DeepClone();
                    var a = o["after"]?.DeepClone();
                    o["before"] = a;
                    o["after"] = b;
                }
                foreach (var kv in o.ToList()) {
                    if (kv.Key != "before" && kv.Key != "after") {
                        SwapNode(kv.Value);
                    }
                }
            } else if (node is JsonArray arr) {
                foreach (var e in arr) {
                    SwapNode(e);
                }
            }
        }

        private FakeControl Control => ctx.FakeControlInstance;

        [Test]
        public void EveryGoKindIsRegistered() {
            var kinds = new[] {
                "exposuretemplate.edit", "exposuretemplate.batchedit", "project.batchedit", "exposureplan.batchedit", "goal.edit", "ruleweight.edit",
                "exposuretemplate.clone", "exposuretemplate.delete", "exposureplan.applyset", "exposureplan.unapplyset", "project.create", "project.delete"
            };
            foreach (var k in kinds) {
                registry.Get(k).Should().NotBeNull(k);
                registry.Get(k).IsEdit.Should().BeTrue(k);
            }
            registry.Get("exposuretemplate.clone").Timing.Should().Be(CommandTiming.Immediate);
            registry.Get("exposuretemplate.delete").Timing.Should().Be(CommandTiming.Immediate);
            registry.Get("project.create").Timing.Should().Be(CommandTiming.AtExposureEnd);
            registry.Get("goal.edit").Timing.Should().Be(CommandTiming.AtExposureEnd);
        }

        [Test]
        public void SamplesValidate() {
            foreach (var (kind, payload) in new[] {
                ("goal.edit", Samples.GoalEdit), ("ruleweight.edit", Samples.RuleWeightEdit), ("exposuretemplate.edit", Samples.TemplateEdit),
                ("exposuretemplate.batchedit", Samples.TemplateBatchEdit), ("project.batchedit", Samples.ProjectBatchEdit), ("exposureplan.batchedit", Samples.ExposurePlanBatchEdit),
                ("exposuretemplate.clone", Samples.TemplateClone), ("exposuretemplate.delete", Samples.TemplateDelete), ("exposureplan.applyset", Samples.ApplySet),
                ("exposureplan.unapplyset", Samples.ApplySet), ("project.create", Samples.ProjectCreate), ("project.delete", Samples.ProjectCreate) }) {
                registry.Get(kind).Validate(Cmd.Of(kind, payload)).Should().BeNull(kind);
            }
        }

        [Test]
        public void TemplateEditApplies() {
            var r = Run("exposuretemplate.edit", Samples.TemplateEdit);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT moonavoidanceseparation FROM exposuretemplate WHERE Id = 3").Should().Be(60.0);
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)null, (int?)null));
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void TemplateEditConflictChangesNothing() {
            db.Exec("UPDATE exposuretemplate SET moonavoidanceseparation = 50 WHERE Id = 3");
            var before = Snapshot();
            var r = Run("exposuretemplate.edit", Samples.TemplateEdit);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("moonavoidanceseparation is 50, not 45");
            Snapshot().Should().Equal(before);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void TemplateEditRoundTrip() {
            var before = Snapshot();
            Run("exposuretemplate.edit", Samples.TemplateEdit).Status.Should().Be(CommandStatus.Applied);
            Run("exposuretemplate.edit", Swap(Samples.TemplateEdit)).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
        }

        [TestCase(@"{""id"":3,""changes"":[{""field"":""filtername"",""before"":""a"",""after"":""b""}]}", "not editable")]
        [TestCase(@"{""id"":3,""changes"":[{""field"":""twilightlevel"",""before"":0,""after"":4}]}", "between")]
        [TestCase(@"{""id"":3,""changes"":[{""field"":""moonavoidancewidth"",""before"":7,""after"":15}]}", "between")]
        [TestCase(@"{""id"":3,""changes"":[{""field"":""defaultexposure"",""before"":600,""after"":0}]}", "between")]
        [TestCase(@"{""id"":3,""changes"":[{""field"":""maximumhumidity"",""before"":0,""after"":101}]}", "between")]
        public void TemplateEditRejectsBadValues(string payload, string reason) {
            registry.Get("exposuretemplate.edit").Validate(Cmd.Of("exposuretemplate.edit", payload)).Should().Contain(reason);
        }

        [Test]
        public void TemplateBatchAppliesAll() {
            var r = Run("exposuretemplate.batchedit", Samples.TemplateBatchEdit);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposuretemplate WHERE moonavoidanceseparation = 60").Should().Be(2);
            Control.Replans.Should().HaveCount(1);
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)null, (int?)null));
        }

        [Test]
        public void TemplateBatchConflictOnOneItemAppliesNothing() {
            db.Exec("UPDATE exposuretemplate SET moonavoidanceseparation = 30 WHERE Id = 4");
            var before = Snapshot();
            var r = Run("exposuretemplate.batchedit", Samples.TemplateBatchEdit);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("O-III moonavoidanceseparation is 30, not 45");
            r.Detail.Should().BeAssignableTo<IEnumerable<Dictionary<string, object>>>().Which.Should().ContainSingle().Which["id"].Should().Be(4);
            Snapshot().Should().Equal(before);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void TemplateBatchMissingItemAppliesNothing() {
            db.Exec("DELETE FROM exposuretemplate WHERE Id = 4");
            var before = Snapshot();
            var r = Run("exposuretemplate.batchedit", Samples.TemplateBatchEdit);
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("O-III no longer exists");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void TemplateBatchRoundTrip() {
            var before = Snapshot();
            Run("exposuretemplate.batchedit", Samples.TemplateBatchEdit).Status.Should().Be(CommandStatus.Applied);
            Run("exposuretemplate.batchedit", Swap(Samples.TemplateBatchEdit)).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void TemplateBatchRejectsDuplicatesAndBadItems() {
            var h = registry.Get("exposuretemplate.batchedit");
            h.Validate(Cmd.Of(h.Kind, @"{""items"":[]}")).Should().Contain("no items");
            h.Validate(Cmd.Of(h.Kind, @"{""items"":[{""id"":3,""changes"":[{""field"":""gain"",""before"":1,""after"":2}]},{""id"":3,""changes"":[{""field"":""offset"",""before"":1,""after"":2}]}]}")).Should().Contain("twice");
            h.Validate(Cmd.Of(h.Kind, @"{""items"":[{""id"":3,""name"":""H-a"",""changes"":[{""field"":""filtername"",""before"":""a"",""after"":""b""}]}]}")).Should().Contain("H-a: exposuretemplate.filtername is not editable");
        }

        [Test]
        public void ProjectBatchSampleConflictsUntilBeforeMatches() {
            var before = Snapshot();
            var r = Run("project.batchedit", Samples.ProjectBatchEdit);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("Cygnis Loop priority is 1, not 0");
            Snapshot().Should().Equal(before);

            db.Exec("UPDATE project SET priority = 0 WHERE Id = 5");
            Run("project.batchedit", Samples.ProjectBatchEdit).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM project WHERE priority = 2").Should().Be(2);
            Control.Invalidated.Should().BeEquivalentTo(new[] { ((int?)5, (int?)null), ((int?)7, (int?)null) });
            Control.Replans.Should().HaveCount(1);

            Run("project.batchedit", Swap(Samples.ProjectBatchEdit)).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM project WHERE priority = 0 AND Id IN (5, 7)").Should().Be(2);
        }

        [Test]
        public void ExposurePlanBatchAppliesAndRoundTrips() {
            var before = Snapshot();
            Run("exposureplan.batchedit", Samples.ExposurePlanBatchEdit).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT desired FROM exposureplan WHERE Id = 40").Should().Be(400L);
            db.Scalar("SELECT enabled FROM exposureplan WHERE Id = 41").Should().Be(0L);
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)13));
            Run("exposureplan.batchedit", Swap(Samples.ExposurePlanBatchEdit)).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void GoalEditApplies() {
            var r = Run("goal.edit", Samples.GoalEdit);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM ts_goal").Should().Be(2);
            db.Scalar("SELECT snr_goal FROM ts_goal WHERE target_guid = 'g12' AND filter = 'H-a'").Should().Be(10.0);
            db.Scalar("SELECT plateau_stop FROM ts_goal WHERE target_guid = 'g12'").Should().Be(1L);
            db.Scalar("SELECT depth_goal FROM ts_goal WHERE target_guid = 'g12'").Should().Be(System.DBNull.Value);
            db.Scalar("SELECT snr_goal FROM ts_goal WHERE target_guid = 'g13'").Should().Be(12.0);
            db.Scalar("SELECT region FROM ts_goal WHERE target_guid = 'g13'").Should().Be(@"[{""x"":0.1,""y"":0.1},{""x"":0.5,""y"":0.1},{""x"":0.3,""y"":0.6}]");
            db.Scalar("SELECT updated_at FROM ts_goal WHERE target_guid = 'g13'").Should().Be("2026-10-10T06:52:00Z");
            Control.Invalidated.Should().BeEquivalentTo(new[] { ((int?)5, (int?)12), ((int?)5, (int?)13) });
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void GoalEditConflictChangesNothing() {
            db.Exec("UPDATE ts_goal SET snr_goal = 11 WHERE target_guid = 'g13'");
            var before = Snapshot();
            var r = Run("goal.edit", Samples.GoalEdit);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("Cygnis Loop Panel 2 H-a goal is SNR 11, not SNR 10");
            Snapshot().Should().Equal(before);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void GoalEditBeforeNullConflictsWithAnExistingRow() {
            db.Exec("INSERT INTO ts_goal (target_guid, filter, kind, snr_goal, plateau_stop) VALUES ('g12', 'H-a', 0, 15, 1)");
            var before = Snapshot();
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Conflict);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void GoalEditRoundTrip() {
            var before = Snapshot("updated_at");
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Applied);
            Run("goal.edit", Swap(Samples.GoalEdit)).Status.Should().Be(CommandStatus.Applied);
            Snapshot("updated_at").Should().Equal(before);
            db.Count("SELECT COUNT(*) FROM ts_goal WHERE target_guid = 'g12'").Should().Be(0);
        }

        [Test]
        public void GoalEditComparesRegionsByValue() {
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Applied);
            db.Exec(@"UPDATE ts_goal SET region = '[ {""y"":0.10004, ""x"":0.1}, {""x"":0.50000, ""y"":0.1}, {""x"":0.3, ""y"":0.6} ]' WHERE target_guid = 'g13'");
            Run("goal.edit", Swap(Samples.GoalEdit)).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT region FROM ts_goal WHERE target_guid = 'g13'").Should().Be(System.DBNull.Value);
        }

        [TestCase(null, "", true)]
        [TestCase("[]", null, false)]
        [TestCase(@"[{""x"":0.1,""y"":0.2},{""x"":0.3,""y"":0.4},{""x"":0.5,""y"":0.6}]", @"[{""x"":0.10009,""y"":0.2},{""x"":0.3,""y"":0.4},{""x"":0.5,""y"":0.6}]", true)]
        [TestCase(@"[{""x"":0.1,""y"":0.2},{""x"":0.3,""y"":0.4},{""x"":0.5,""y"":0.6}]", @"[{""x"":0.1002,""y"":0.2},{""x"":0.3,""y"":0.4},{""x"":0.5,""y"":0.6}]", false)]
        [TestCase(@"[{""x"":0.1,""y"":0.2},{""x"":0.3,""y"":0.4},{""x"":0.5,""y"":0.6}]", @"[{""x"":0.1,""y"":0.2},{""x"":0.3,""y"":0.4}]", false)]
        public void RegionEquality(string a, string b, bool same) {
            SqlSchedulerDatabase.RegionEquals(a, b).Should().Be(same);
        }

        [Test]
        public void GoalEditToleratesTinyNumericDifferences() {
            db.Exec("UPDATE ts_goal SET snr_goal = 10.0000001 WHERE target_guid = 'g13'");
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void GoalEditWithoutTheGoalTableIsRejected() {
            db.Exec("DROP TABLE ts_goal");
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Rejected);
        }

        [Test]
        public void GoalEditMissingTargetIsAConflict() {
            db.Exec("UPDATE target SET guid = 'other' WHERE Id = 12");
            var before = Snapshot();
            Run("goal.edit", Samples.GoalEdit).Status.Should().Be(CommandStatus.Conflict);
            Snapshot().Should().Equal(before);
        }

        [TestCase(@"{""goals"":[]}")]
        [TestCase(@"{""goals"":[{""target_guid"":""g"",""filter"":""H-a"",""before"":null,""after"":{""kind"":0,""snr_goal"":0}}]}")]
        [TestCase(@"{""goals"":[{""target_guid"":""g"",""filter"":""H-a"",""before"":null,""after"":{""kind"":1,""snr_goal"":10}}]}")]
        [TestCase(@"{""goals"":[{""target_guid"":""g"",""filter"":""H-a"",""before"":null,""after"":null}]}")]
        [TestCase(@"{""goals"":[{""target_guid"":"""",""filter"":""H-a"",""before"":null,""after"":{""kind"":0,""snr_goal"":10}}]}")]
        public void GoalEditRejectsBadPayloads(string payload) {
            registry.Get("goal.edit").Validate(Cmd.Of("goal.edit", payload)).Should().NotBeNull();
        }

        [Test]
        public void RuleWeightEditApplies() {
            var r = Run("ruleweight.edit", Samples.RuleWeightEdit);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT weight FROM ruleweight WHERE projectid = 5 AND name = 'Novelty'").Should().Be(10.0);
            db.Scalar("SELECT weight FROM ruleweight WHERE projectid = 5 AND name = 'Target Switch Penalty'").Should().Be(40.0);
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)5, (int?)null));
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void RuleWeightEditConflictChangesNothing() {
            db.Exec("UPDATE ruleweight SET weight = 60 WHERE projectid = 5 AND name = 'Target Switch Penalty'");
            var before = Snapshot();
            var r = Run("ruleweight.edit", Samples.RuleWeightEdit);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("Target Switch Penalty weight is 60, not 67");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void RuleWeightEditToleratesRounding() {
            db.Exec("UPDATE ruleweight SET weight = 67.0004 WHERE projectid = 5 AND name = 'Target Switch Penalty'");
            Run("ruleweight.edit", Samples.RuleWeightEdit).Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void RuleWeightEditRoundTrip() {
            var before = Snapshot();
            Run("ruleweight.edit", Samples.RuleWeightEdit).Status.Should().Be(CommandStatus.Applied);
            Run("ruleweight.edit", Swap(Samples.RuleWeightEdit)).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void RuleWeightEditWillNotRemoveABuiltInRule() {
            var before = Snapshot();
            var r = Run("ruleweight.edit", @"{""project_id"":5,""project_name"":""Cygnis Loop"",""changes"":[{""rule"":""Project Priority"",""before"":50,""after"":null}]}");
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("built-in");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void RuleWeightEditOnAMissingProjectIsRejected() {
            Run("ruleweight.edit", @"{""project_id"":99,""project_name"":""Gone"",""changes"":[{""rule"":""Novelty"",""before"":null,""after"":10}]}").Status.Should().Be(CommandStatus.Rejected);
        }

        [Test]
        public void CloneCopiesEveryColumn() {
            var r = Run("exposuretemplate.clone", Samples.TemplateClone);
            r.Status.Should().Be(CommandStatus.Applied);
            var clone = db.Dump("exposuretemplate WHERE guid = 't-new'", "Id", "guid", "name", "defaultexposure", "gain");
            var source = db.Dump("exposuretemplate WHERE Id = 3", "Id", "guid", "name", "defaultexposure", "gain");
            clone.Should().Be(source);
            db.Scalar("SELECT name FROM exposuretemplate WHERE guid = 't-new'").Should().Be("H-a 300");
            db.Scalar("SELECT defaultexposure FROM exposuretemplate WHERE guid = 't-new'").Should().Be(300.0);
            db.Scalar("SELECT gain FROM exposuretemplate WHERE guid = 't-new'").Should().Be(100L);
            Control.Replans.Should().BeEmpty();
        }

        [Test]
        public void CloneIsIdempotentByGuid() {
            Run("exposuretemplate.clone", Samples.TemplateClone).Status.Should().Be(CommandStatus.Applied);
            var r = Run("exposuretemplate.clone", Samples.TemplateClone);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().Contain("already exists");
            db.Count("SELECT COUNT(*) FROM exposuretemplate WHERE guid = 't-new'").Should().Be(1);
        }

        [Test]
        public void CloneRejectsADuplicateName() {
            db.Exec("UPDATE exposuretemplate SET name = 'H-a 300' WHERE Id = 4");
            var before = Snapshot();
            Run("exposuretemplate.clone", Samples.TemplateClone).Status.Should().Be(CommandStatus.Rejected);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void CloneThenDeleteRoundTrip() {
            var before = Snapshot();
            Run("exposuretemplate.clone", Samples.TemplateClone).Status.Should().Be(CommandStatus.Applied);
            var inverse = $@"{{""guid"":""t-new"",""name"":""H-a 300"",""clone"":{Samples.TemplateClone}}}";
            Run("exposuretemplate.delete", inverse).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
            Run("exposuretemplate.clone", Samples.TemplateClone).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposuretemplate WHERE guid = 't-new'").Should().Be(1);
        }

        [Test]
        public void DeleteSampleRemovesAndIsIdempotent() {
            Run("exposuretemplate.clone", Samples.TemplateClone);
            Run("exposuretemplate.delete", Samples.TemplateDelete).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposuretemplate WHERE guid = 't-new'").Should().Be(0);
            var again = Run("exposuretemplate.delete", Samples.TemplateDelete);
            again.Status.Should().Be(CommandStatus.Applied);
            again.Message.Should().Contain("already gone");
        }

        [Test]
        public void DeleteRefusesATemplateInUse() {
            db.Exec("UPDATE exposureplan SET exposureTemplateId = 3 WHERE Id = 40");
            var before = Snapshot();
            var r = Run("exposuretemplate.delete", @"{""guid"":""t3"",""name"":""H-a""}");
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("used by 1 exposure plan");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void ApplySetApplies() {
            var r = Run("exposureplan.applyset", Samples.ApplySet);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Dump("exposureplan WHERE guid = 'p1'", "Id").Should().Be("profileId=p|exposure=600|desired=300|acquired=0|accepted=0|targetid=29|exposureTemplateId=3|enabled=1|guid=p1");
            db.Scalar("SELECT enabled FROM exposureplan WHERE Id = 90").Should().Be(0L);
            db.Scalar("SELECT acquired FROM exposureplan WHERE Id = 90").Should().Be(20L);
            db.Count("SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 29").Should().Be(0);
            db.Count("SELECT COUNT(*) FROM overrideexposureorderitem WHERE targetid = 29").Should().Be(0);
            db.Count("SELECT COUNT(*) FROM filtercadenceitem WHERE targetid = 13").Should().Be(1);
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)9, (int?)29));
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void PlansWithoutALengthKeepTheTemplateMarker() {
            Run("exposureplan.applyset", Samples.ApplySet.Replace("\"exposure\":600", "\"exposure\":-1")).Status.Should().Be(CommandStatus.Applied);
            Run("project.create", Samples.ProjectCreate.Replace("\"exposure\":600", "\"exposure\":-1")).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT exposure FROM exposureplan WHERE guid = 'p1'").Should().Be(-1.0);
            db.Scalar("SELECT exposure FROM exposureplan WHERE guid = 'pp'").Should().Be(-1.0);
        }

        [Test]
        public void ApplySetIsIdempotent() {
            Run("exposureplan.applyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            var after = Snapshot();
            Run("exposureplan.applyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(after);
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void ApplySetMissingTargetIsAConflict() {
            db.Exec("DELETE FROM exposureplan WHERE targetid = 29; DELETE FROM target WHERE Id = 29");
            var before = Snapshot();
            var r = Run("exposureplan.applyset", Samples.ApplySet);
            r.Status.Should().Be(CommandStatus.Conflict);
            r.Message.Should().Contain("Triangulum is removed");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void ApplySetPlanMovedToAnotherTargetIsAConflict() {
            db.Exec("UPDATE exposureplan SET targetid = 13 WHERE Id = 90");
            var before = Snapshot();
            Run("exposureplan.applyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Conflict);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void ApplySetAlreadyDisabledIsFine() {
            db.Exec("UPDATE exposureplan SET enabled = 0 WHERE Id = 90");
            Run("exposureplan.applyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM exposureplan WHERE guid = 'p1'").Should().Be(1);
        }

        [Test]
        public void UnapplySetRoundTrip() {
            var before = Snapshot();
            Run("exposureplan.applyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            Run("exposureplan.unapplyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            foreach (var t in AllTables.Except(new[] { "filtercadenceitem", "overrideexposureorderitem" })) {
                db.Dump(t).Should().Be(before[t], t);
            }
            Run("exposureplan.unapplyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            db.Dump("exposureplan").Should().Be(before["exposureplan"]);
        }

        [Test]
        public void UnapplySetKeepsAPlanWithSubs() {
            Run("exposureplan.applyset", Samples.ApplySet);
            var id = db.Scalar("SELECT Id FROM exposureplan WHERE guid = 'p1'");
            db.Exec($"INSERT INTO acquiredimage (projectId, targetId, filtername, gradingStatus, metadata, exposureId) VALUES (9, 29, 'H-a', 0, '{{}}', {id})");
            Run("exposureplan.unapplyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT enabled FROM exposureplan WHERE guid = 'p1'").Should().Be(0L);
            db.Scalar("SELECT enabled FROM exposureplan WHERE Id = 90").Should().Be(1L);
        }

        [Test]
        public void UnapplySetKeepsAPlanWithAcquiredCounts() {
            Run("exposureplan.applyset", Samples.ApplySet);
            db.Exec("UPDATE exposureplan SET acquired = 3 WHERE guid = 'p1'");
            Run("exposureplan.unapplyset", Samples.ApplySet).Status.Should().Be(CommandStatus.Applied);
            db.Scalar("SELECT enabled FROM exposureplan WHERE guid = 'p1'").Should().Be(0L);
        }

        [TestCase(@"{""mode"":""merge"",""targets"":[{""target_id"":29,""disable"":[{""id"":90}]}]}", "mode")]
        [TestCase(@"{""mode"":""add"",""targets"":[]}", "no targets")]
        [TestCase(@"{""mode"":""add"",""targets"":[{""target_id"":29}]}", "nothing")]
        [TestCase(@"{""mode"":""add"",""targets"":[{""target_id"":29,""create"":[{""guid"":"""",""template_id"":3,""desired"":1}]}]}", "guid")]
        public void ApplySetRejectsBadPayloads(string payload, string reason) {
            registry.Get("exposureplan.applyset").Validate(Cmd.Of("exposureplan.applyset", payload)).Should().Contain(reason);
        }

        [Test]
        public void ProjectCreateApplies() {
            var r = Run("project.create", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Applied);
            var pid = db.Scalar("SELECT Id FROM project WHERE guid = 'pg'");
            db.Dump("project WHERE guid = 'pg'", "Id").Should().Be(
                "profileId=p|name=Gecko Nebula|state=1|priority=1|minimumtime=60|isMosaic=0|flatsHandling=0|description=null|createdate=1791615120|activedate=null|inactivedate=null|minimumaltitude=15|usecustomhorizon=0|horizonoffset=0|meridianwindow=0|filterswitchfrequency=0|ditherevery=0|enablegrader=1|maximumAltitude=0|smartexposureorder=0|guid=pg");
            db.Dump("target WHERE guid = 'tg'", "Id").Should().Be($"name=Gecko Nebula|active=1|ra=22.53|dec=40.83|epochcode=2|projectid={pid}|rotation=0|roi=100|unusedOEO=null|guid=tg");
            var tid = db.Scalar("SELECT Id FROM target WHERE guid = 'tg'");
            db.Dump("exposureplan WHERE guid = 'pp'", "Id").Should().Be($"profileId=p|exposure=600|desired=300|acquired=0|accepted=0|targetid={tid}|exposureTemplateId=3|enabled=1|guid=pp");
            db.Count($"SELECT COUNT(*) FROM ruleweight WHERE projectid = {pid}").Should().Be(8);
            db.Scalar($"SELECT weight FROM ruleweight WHERE projectid = {pid} AND name = 'Target Switch Penalty'").Should().Be(67.0);
            db.Dump("ts_goal WHERE target_guid = 'tg'").Should().Be("target_guid=tg|filter=H-a|kind=0|snr_goal=10|depth_goal=null|plateau_stop=1|region=null|updated_at=2026-10-10T06:52:00Z");
            Control.Invalidated.Should().ContainSingle().Which.Should().Be(((int?)System.Convert.ToInt32(pid), (int?)null));
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void ProjectCreateFillsMissingSettingsWithTheProfilesMostCommonValues() {
            db.Exec(@"UPDATE project SET ditherevery = 2, filterswitchfrequency = 1, flatsHandling = 1, meridianwindow = 60, enablegrader = 1 WHERE Id = 5;
UPDATE project SET ditherevery = 2, filterswitchfrequency = 1, flatsHandling = 1, meridianwindow = 30, enablegrader = 1 WHERE Id = 7;
UPDATE project SET ditherevery = 1, filterswitchfrequency = 0, flatsHandling = 0, enablegrader = 0 WHERE Id = 9;
INSERT INTO project (Id, profileId, name, state, priority, minimumtime, ditherevery, filterswitchfrequency, flatsHandling, meridianwindow) VALUES (20, 'other', 'Elsewhere', 1, 1, 60, 5, 5, 7, 5), (21, 'other', 'Elsewhere 2', 1, 1, 60, 5, 5, 7, 5);");
            var r = Run("project.create", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Applied);
            db.Dump("project WHERE guid = 'pg'", "Id").Should().Contain("|flatsHandling=1|").And.Contain("|filterswitchfrequency=1|").And.Contain("|ditherevery=2|")
                .And.Contain("|meridianwindow=30|").And.Contain("|enablegrader=1|").And.Contain("|maximumAltitude=0|").And.Contain("|usecustomhorizon=0|");
            r.Message.Should().Be("Most common in the profile's 3 projects: ditherevery 2, filterswitchfrequency 1, flatshandling 1, smartexposureorder 0, meridianwindow 30, usecustomhorizon 0, horizonoffset 0, maximumaltitude 0, enablegrader 1.");
        }

        [Test]
        public void ProjectCreateKeepsTheSettingsItIsGiven() {
            db.Exec("UPDATE project SET ditherevery = 2, flatsHandling = 1");
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["project"]["ditherevery"] = 3;
            payload["project"]["filterswitchfrequency"] = 4;
            payload["project"]["flatshandling"] = 200;
            payload["project"]["smartexposureorder"] = true;
            payload["project"]["meridianwindow"] = 120;
            payload["project"]["usecustomhorizon"] = true;
            payload["project"]["horizonoffset"] = 2.5;
            payload["project"]["maximumaltitude"] = 80;
            payload["project"]["enablegrader"] = false;
            var r = Run("project.create", payload.ToJsonString());
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().BeNull();
            db.Dump("project WHERE guid = 'pg'", "Id").Should().Be(
                "profileId=p|name=Gecko Nebula|state=1|priority=1|minimumtime=60|isMosaic=0|flatsHandling=200|description=null|createdate=1791615120|activedate=null|inactivedate=null|minimumaltitude=15|usecustomhorizon=1|horizonoffset=2.5|meridianwindow=120|filterswitchfrequency=4|ditherevery=3|enablegrader=0|maximumAltitude=80|smartexposureorder=1|guid=pg");
        }

        [TestCase("ditherevery", "1000")]
        [TestCase("ditherevery", "-1")]
        [TestCase("filterswitchfrequency", "1000")]
        [TestCase("flatshandling", "150")]
        [TestCase("meridianwindow", "721")]
        [TestCase("horizonoffset", "91")]
        [TestCase("maximumaltitude", "95")]
        public void ProjectCreateRejectsSettingsOutOfRange(string field, string value) {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["project"][field] = JsonNode.Parse(value);
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload.ToJsonString())).Should().Contain(field);
        }

        [Test]
        public void ProjectCreateUsesThePayloadRuleWeightsAndFillsBuiltIns() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["rule_weights"] = JsonNode.Parse(@"{""Novelty"":10,""Rarity"":20,""Target Switch Penalty"":40}");
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            var pid = db.Scalar("SELECT Id FROM project WHERE guid = 'pg'");
            db.Count($"SELECT COUNT(*) FROM ruleweight WHERE projectid = {pid}").Should().Be(10);
            db.Scalar($"SELECT weight FROM ruleweight WHERE projectid = {pid} AND name = 'Target Switch Penalty'").Should().Be(40.0);
            db.Scalar($"SELECT weight FROM ruleweight WHERE projectid = {pid} AND name = 'Rarity'").Should().Be(20.0);
        }

        [Test]
        public void ProjectCreateIsIdempotentByGuid() {
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            var after = Snapshot();
            var r = Run("project.create", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().Contain("already exists");
            Snapshot().Should().Equal(after);
            Control.Replans.Should().HaveCount(1);
        }

        [Test]
        public void ProjectCreateRejectsADuplicateNameUnlessKeptSeparate() {
            db.Exec("UPDATE project SET name = 'Gecko Nebula' WHERE Id = 7");
            var before = Snapshot();
            var r = Run("project.create", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("already exists");
            Snapshot().Should().Equal(before);

            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["match"] = "separate";
            payload["match_with"] = "Gecko Nebula";
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
        }

        [Test]
        public void ProjectCreateWithAMissingTemplateIsRejected() {
            db.Exec("DELETE FROM exposuretemplate WHERE Id = 3");
            var before = Snapshot();
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Rejected);
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void ProjectCreateOnAnEmptyDatabaseTakesTheTemplateProfile() {
            ctx.ProfileId = null;
            db.Exec("DELETE FROM exposureplan; DELETE FROM target; DELETE FROM ruleweight; DELETE FROM project; UPDATE exposuretemplate SET profileId = 'q'");
            var r = Run("project.create", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Applied);
            r.Message.Should().StartWith("Target Scheduler defaults, as the profile has no projects: ditherevery 0, filterswitchfrequency 0, flatshandling 0,");
            db.Scalar("SELECT enablegrader FROM project WHERE guid = 'pg'").Should().Be(1L);
            db.Scalar("SELECT profileId FROM project WHERE guid = 'pg'").Should().Be("q");
            db.Scalar("SELECT profileId FROM exposureplan WHERE guid = 'pp'").Should().Be("q");
        }

        [Test]
        public void ProjectCreateRejectsATemplateFromAnotherProfile() {
            db.Exec("UPDATE exposuretemplate SET profileId = 'other' WHERE Id = 3");
            Run("project.create", Samples.ProjectCreate).Message.Should().Contain("another profile");
        }

        [Test]
        public void ProjectCreateThenDeleteRoundTrip() {
            var before = Snapshot();
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            var tid = db.Scalar("SELECT Id FROM target WHERE guid = 'tg'");
            db.Exec($"INSERT INTO filtercadenceitem (targetid, \"order\", next, action, referenceIdx) VALUES ({tid}, 1, 1, 0, 0)");
            db.Exec($"INSERT INTO overrideexposureorderitem (targetid, \"order\", action, referenceIdx) VALUES ({tid}, 1, 0, 0)");
            db.Exec($"INSERT INTO flathistory (profileId, targetId, lightSessionId) VALUES ('p', {tid}, 1)");
            Run("project.delete", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            Snapshot().Should().Equal(before);
            Control.Replans.Should().HaveCount(2);
            var again = Run("project.delete", Samples.ProjectCreate);
            again.Status.Should().Be(CommandStatus.Applied);
            again.Message.Should().Contain("already gone");
        }

        [Test]
        public void ProjectDeleteIsRefusedWithAcquiredImages() {
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            var pid = db.Scalar("SELECT Id FROM project WHERE guid = 'pg'");
            var tid = db.Scalar("SELECT Id FROM target WHERE guid = 'tg'");
            db.Exec($"INSERT INTO acquiredimage (projectId, targetId, filtername, gradingStatus, metadata, exposureId) VALUES ({pid}, {tid}, 'H-a', 0, '{{}}', 0)");
            var before = Snapshot();
            var r = Run("project.delete", Samples.ProjectCreate);
            r.Status.Should().Be(CommandStatus.Rejected);
            r.Message.Should().Contain("already has 1 sub");
            Snapshot().Should().Equal(before);
        }

        [Test]
        public void ProjectDeleteIsRefusedWithAcquiredCounts() {
            Run("project.create", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Applied);
            db.Exec("UPDATE exposureplan SET acquired = 2 WHERE guid = 'pp'");
            Run("project.delete", Samples.ProjectCreate).Status.Should().Be(CommandStatus.Rejected);
            db.Count("SELECT COUNT(*) FROM project WHERE guid = 'pg'").Should().Be(1);
        }

        [TestCase(@"{""project"":{""guid"":""p"",""name"":""x""},""targets"":[]}")]
        [TestCase(@"{""project"":{""guid"":""p"",""name"":""x""},""targets"":[{""guid"":""p"",""name"":""t"",""ra_hours"":1,""dec"":1}]}")]
        [TestCase(@"{""project"":{""guid"":""p"",""name"":""x""},""targets"":[{""guid"":""t"",""name"":""t"",""ra_hours"":25,""dec"":1}]}")]
        [TestCase(@"{""project"":{""guid"":""p"",""name"":""x""},""targets"":[{""guid"":""t"",""name"":""t"",""ra_hours"":1,""dec"":1}],""goals"":[{""target_guid"":""zz"",""filter"":""H-a"",""setting"":{""kind"":0,""snr_goal"":10}}]}")]
        [TestCase(@"{""project"":{""guid"":""p"",""name"":""x"",""priority"":3},""targets"":[{""guid"":""t"",""name"":""t"",""ra_hours"":1,""dec"":1}]}")]
        public void ProjectCreateRejectsBadPayloads(string payload) {
            registry.Get("project.create").Validate(Cmd.Of("project.create", payload)).Should().NotBeNull();
        }

        [Test]
        public void ProjectCreateAcceptsNullPlans() {
            var payload = JsonNode.Parse(Samples.ProjectCreate);
            payload["targets"][0]["plans"] = null;
            payload["goals"] = null;
            Run("project.create", payload.ToJsonString()).Status.Should().Be(CommandStatus.Applied);
            db.Count("SELECT COUNT(*) FROM target WHERE guid = 'tg'").Should().Be(1);
        }
    }

    [TestFixture]
    public class PlanningProcessorTest {
        private TestDb db;
        private FakeContext ctx;
        private CommandProcessor proc;

        [SetUp]
        public void SetUp() {
            db = new TestDb().WithPlanning();
            ctx = new FakeContext { Database = new SqlSchedulerDatabase(db.Connect) };
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            proc = new CommandProcessor(CommandRegistry.Builtin(), store, ctx, () => true, () => false, () => null);
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        [TestCase("exposuretemplate.edit")]
        [TestCase("exposuretemplate.batchedit")]
        [TestCase("exposureplan.batchedit")]
        [TestCase("goal.edit")]
        [TestCase("ruleweight.edit")]
        [TestCase("exposureplan.applyset")]
        [TestCase("project.create")]
        [TestCase("exposuretemplate.clone")]
        public void ResubmittingTheSameCommandAppliesOnce(string kind) {
            var payload = kind switch {
                "exposuretemplate.edit" => Samples.TemplateEdit,
                "exposuretemplate.batchedit" => Samples.TemplateBatchEdit,
                "exposureplan.batchedit" => Samples.ExposurePlanBatchEdit,
                "goal.edit" => Samples.GoalEdit,
                "ruleweight.edit" => Samples.RuleWeightEdit,
                "exposureplan.applyset" => Samples.ApplySet,
                "project.create" => Samples.ProjectCreate,
                _ => Samples.TemplateClone,
            };
            proc.Submit(Cmd.Of(kind, payload, "same"), "api").Status.Should().Be(CommandStatus.Applied);
            var after = new[] { "project", "target", "exposureplan", "exposuretemplate", "ruleweight", "ts_goal" }.Select(t => db.Dump(t)).ToList();
            var replans = ctx.FakeControlInstance.Replans.Count;
            proc.Submit(Cmd.Of(kind, payload, "same"), "queue").Status.Should().Be(CommandStatus.Applied);
            new[] { "project", "target", "exposureplan", "exposuretemplate", "ruleweight", "ts_goal" }.Select(t => db.Dump(t)).Should().Equal(after);
            ctx.FakeControlInstance.Replans.Should().HaveCount(replans);
        }
    }
}
