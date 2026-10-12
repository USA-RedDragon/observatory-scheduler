using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class BatchEditHandler : ICommandHandler {
        public string Kind { get; }
        public CommandTiming Timing => CommandTiming.AtExposureEnd;
        public bool IsEdit => true;
        public EditHandler Item { get; }

        public BatchEditHandler(string kind, EditHandler item) {
            Kind = kind;
            Item = item;
        }

        public static BatchEditHandler ExposureTemplates() {
            return new BatchEditHandler(CommandKinds.ExposureTemplateBatchEdit, EditHandler.ExposureTemplate());
        }

        public static BatchEditHandler Projects() {
            return new BatchEditHandler(CommandKinds.ProjectBatchEdit, EditHandler.Project());
        }

        public static BatchEditHandler ExposurePlans() {
            return new BatchEditHandler(CommandKinds.ExposurePlanBatchEdit, EditHandler.ExposurePlan());
        }

        public string Validate(CommandEnvelope command) {
            BatchEditPayload p;
            try {
                p = command.PayloadAs<BatchEditPayload>();
            } catch (JsonException e) {
                return $"invalid payload: {e.Message}";
            }
            if (p?.Items == null || p.Items.Count == 0) {
                return "no items";
            }
            var ids = new HashSet<int>();
            foreach (var it in p.Items) {
                var error = Item.ValidatePayload(it);
                if (error != null) {
                    return it == null ? error : $"{EditHandler.Label(it)}: {error}";
                }
                if (!ids.Add(it.Id)) {
                    return $"{Item.Table.Entity} {it.Id} appears twice";
                }
            }
            return null;
        }

        public CommandResult Apply(CommandEnvelope command, ICommandContext context) {
            var error = Validate(command);
            if (error != null) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, error);
            }
            var p = command.PayloadAs<BatchEditPayload>();
            var items = p.Items.Select(it => new EditItem { Id = it.Id, Label = EditHandler.Label(it), Changes = Item.ColumnChanges(it) }).ToList();
            var outcomes = context.Database.ApplyEdits(Item.Table, items);

            var missing = items.Where((it, i) => !outcomes[i].Found).Select(it => it.Label).ToList();
            if (missing.Count > 0) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, $"{string.Join(", ", missing)} no longer {(missing.Count == 1 ? "exists" : "exist")}. Nothing was applied.");
            }
            var invalid = items.Select((it, i) => (it, outcomes[i].Invalid)).Where(x => x.Invalid != null).Select(x => $"{x.it.Label}: {x.Invalid}").ToList();
            if (invalid.Count > 0) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, $"{string.Join("; ", invalid)}. Nothing was applied.");
            }
            var conflicts = items.SelectMany((it, i) => outcomes[i].Conflicts.Select(c => (it, c))).ToList();
            if (conflicts.Count > 0) {
                var parts = conflicts.Select(x => $"{x.it.Label} {x.c.Field} is {EditHandler.Show(x.c.Actual)}, not {EditHandler.Show(x.c.Expected)}");
                return CommandResult.Of(command.Id, CommandStatus.Conflict,
                    $"Changed on the observatory: {string.Join("; ", parts)}. Nothing was applied.",
                    conflicts.Select(x => new Dictionary<string, object> { ["id"] = x.it.Id, ["name"] = x.it.Label, ["field"] = x.c.Field, ["expected"] = x.c.Expected, ["actual"] = x.c.Actual }).ToList());
            }
            foreach (var link in outcomes.Select(o => (o.ProjectId, o.TargetId)).Distinct()) {
                context.Control.Invalidate(link.ProjectId, link.TargetId);
            }
            context.Control.RequestReplan($"{Kind} of {items.Count} by {command.Author}");
            return CommandResult.Of(command.Id, CommandStatus.Applied, null);
        }
    }

    public abstract class PlanningHandler<T> : ControlHandler<T> {
        public override bool IsEdit => true;
        protected virtual bool Replans => true;

        protected abstract string Label(T payload);

        protected abstract ChangeOutcome Change(T payload, ICommandContext context);

        protected override CommandResult Run(CommandEnvelope command, T payload, ICommandContext context) {
            Check(payload);
            var o = Change(payload, context);
            if (o.Conflicts.Count > 0) {
                var parts = o.Conflicts.Select(c => $"{c["field"]} is {Show(c["actual"])}, not {Show(c["expected"])}");
                return CommandResult.Of(command.Id, CommandStatus.Conflict,
                    $"{Label(payload)} changed on the observatory: {string.Join("; ", parts)}. Nothing was applied.", o.Conflicts);
            }
            if (o.Status != CommandStatus.Applied) {
                return CommandResult.Of(command.Id, o.Status, o.Message);
            }
            if (o.Changed) {
                foreach (var (project, target) in o.Touched) {
                    context.Control.Invalidate(project, target);
                }
                if (Replans) {
                    context.Control.RequestReplan($"{Kind} by {command.Author}");
                }
            }
            return CommandResult.Of(command.Id, CommandStatus.Applied, o.Message);
        }

        private static string Show(object v) {
            return v == null ? "not set" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static string CheckGoal(GoalSetting g) {
            if (g == null) {
                return null;
            }
            if (g.Kind != 0 && g.Kind != 1) {
                return "goal kind must be 0 or 1";
            }
            if (g.Kind == 0 && (g.SnrGoal < 1 || g.SnrGoal > 1000)) {
                return "SNR goal must be between 1 and 1000";
            }
            if (g.Kind == 1 && (!g.DepthGoal.HasValue || g.DepthGoal < 15 || g.DepthGoal > 32)) {
                return "depth goal must be between 15 and 32";
            }
            return null;
        }

        public static string CheckPlan(NewPlan pl) {
            if (pl == null || string.IsNullOrEmpty(pl.Guid) || pl.TemplateId <= 0 || pl.Desired < 0) {
                return "a new plan needs guid, template_id and desired";
            }
            return null;
        }
    }

    public class GoalEditHandler : PlanningHandler<GoalEditPayload> {
        public override string Kind => CommandKinds.GoalEdit;

        protected override string Check(GoalEditPayload p) {
            if (p.Goals == null || p.Goals.Count == 0) {
                return "no goals";
            }
            var seen = new HashSet<(string, string)>();
            foreach (var g in p.Goals) {
                if (g == null || string.IsNullOrEmpty(g.TargetGuid) || string.IsNullOrWhiteSpace(g.Filter)) {
                    return "each goal needs target_guid and filter";
                }
                if (!seen.Add((g.TargetGuid, g.Filter))) {
                    return $"{g.TargetName} {g.Filter} changed twice";
                }
                if (g.Before == null && g.After == null) {
                    return $"{g.TargetName} {g.Filter} is unchanged";
                }
                var error = CheckGoal(g.Before) ?? CheckGoal(g.After);
                if (error != null) {
                    return error;
                }
            }
            return null;
        }

        protected override string Label(GoalEditPayload p) {
            return string.IsNullOrEmpty(p.ProjectName) ? "Goals" : $"{p.ProjectName} goals";
        }

        protected override ChangeOutcome Change(GoalEditPayload p, ICommandContext context) {
            return context.Database.ApplyGoalEdit(p, context.Now);
        }
    }

    public class RuleWeightEditHandler : PlanningHandler<RuleWeightEditPayload> {
        public override string Kind => CommandKinds.RuleWeightEdit;

        protected override string Check(RuleWeightEditPayload p) {
            if (p.ProjectId <= 0) {
                return "missing project_id";
            }
            if (p.Changes == null || p.Changes.Count == 0) {
                return "no changes";
            }
            var seen = new HashSet<string>();
            foreach (var c in p.Changes) {
                if (c == null || string.IsNullOrEmpty(c.Rule) || !seen.Add(c.Rule)) {
                    return "rule names must be set and unique";
                }
                if ((c.Before.HasValue && (c.Before < 0 || c.Before > 100)) || (c.After.HasValue && (c.After < 0 || c.After > 100))) {
                    return "weights run from 0 to 100";
                }
                if (c.Before == c.After) {
                    return $"{c.Rule} is unchanged";
                }
            }
            return null;
        }

        protected override string Label(RuleWeightEditPayload p) {
            return string.IsNullOrEmpty(p.ProjectName) ? $"Project {p.ProjectId} rule weights" : $"{p.ProjectName} rule weights";
        }

        protected override ChangeOutcome Change(RuleWeightEditPayload p, ICommandContext context) {
            var builtin = p.Changes.FirstOrDefault(c => !c.After.HasValue && context.DefaultRuleWeights != null && context.DefaultRuleWeights.ContainsKey(c.Rule));
            if (builtin != null) {
                return new ChangeOutcome().Reject($"{builtin.Rule} is a built-in rule, so its weight can be changed but not removed.");
            }
            return context.Database.ApplyRuleWeightEdit(p);
        }
    }

    public class TemplateCloneHandler : PlanningHandler<TemplateClonePayload> {
        public override string Kind => CommandKinds.ExposureTemplateClone;
        public override CommandTiming Timing => CommandTiming.Immediate;
        protected override bool Replans => false;

        protected override string Check(TemplateClonePayload p) {
            if (p.SourceId <= 0 || string.IsNullOrEmpty(p.Guid) || string.IsNullOrWhiteSpace(p.Name)) {
                return "clone needs source_id, guid and name";
            }
            if (p.DefaultExposure.HasValue && p.DefaultExposure <= 0) {
                return "defaultexposure must be positive";
            }
            return null;
        }

        protected override string Label(TemplateClonePayload p) {
            return p.Name;
        }

        protected override ChangeOutcome Change(TemplateClonePayload p, ICommandContext context) {
            return context.Database.CloneTemplate(p);
        }
    }

    public class TemplateDeleteHandler : PlanningHandler<TemplateDeletePayload> {
        public override string Kind => CommandKinds.ExposureTemplateDelete;
        public override CommandTiming Timing => CommandTiming.Immediate;
        protected override bool Replans => false;

        protected override string Check(TemplateDeletePayload p) {
            return string.IsNullOrEmpty(p.Guid) ? "missing guid" : null;
        }

        protected override string Label(TemplateDeletePayload p) {
            return p.Name ?? p.Guid;
        }

        protected override ChangeOutcome Change(TemplateDeletePayload p, ICommandContext context) {
            return context.Database.DeleteTemplate(p);
        }
    }

    public class ExposureSetHandler : PlanningHandler<ApplySetPayload> {
        private readonly bool undo;

        public ExposureSetHandler(bool undo) {
            this.undo = undo;
        }

        public override string Kind => undo ? CommandKinds.UnapplySet : CommandKinds.ApplySet;

        protected override string Check(ApplySetPayload p) {
            if (p.Mode != "add" && p.Mode != "replace") {
                return "mode must be add or replace";
            }
            if (p.Targets == null || p.Targets.Count == 0) {
                return "no targets";
            }
            bool any = false;
            foreach (var t in p.Targets) {
                if (t == null || t.TargetId <= 0) {
                    return "missing target_id";
                }
                t.Create ??= new List<NewPlan>();
                t.Disable ??= new List<PlanRef>();
                t.Enable ??= new List<PlanRef>();
                var error = t.Create.Select(CheckPlan).FirstOrDefault(e => e != null);
                if (error != null) {
                    return error;
                }
                if (t.Disable.Concat(t.Enable).Any(r => r == null || r.Id <= 0)) {
                    return "a plan reference needs an id";
                }
                any |= t.Create.Count + t.Disable.Count + t.Enable.Count > 0;
            }
            return any ? null : "nothing to change";
        }

        protected override string Label(ApplySetPayload p) {
            return p.SetName ?? p.SetId;
        }

        protected override ChangeOutcome Change(ApplySetPayload p, ICommandContext context) {
            return context.Database.ApplyExposureSet(p, undo);
        }
    }

    public class ProjectCreateHandler : PlanningHandler<ProjectCreatePayload> {
        private readonly bool delete;

        public ProjectCreateHandler(bool delete) {
            this.delete = delete;
        }

        public override string Kind => delete ? CommandKinds.ProjectDelete : CommandKinds.ProjectCreate;

        protected override string Check(ProjectCreatePayload p) {
            if (p.Project == null || string.IsNullOrEmpty(p.Project.Guid) || string.IsNullOrWhiteSpace(p.Project.Name)) {
                return "the project needs a guid and a name";
            }
            if (p.Project.Priority < 0 || p.Project.Priority > 2 || p.Project.State < 0 || p.Project.State > 3) {
                return "priority or state out of range";
            }
            var settingsError = ProjectSettings.Check(p.Project);
            if (settingsError != null) {
                return settingsError;
            }
            if (p.Targets == null || p.Targets.Count == 0) {
                return "at least one target";
            }
            p.Goals ??= new List<NewGoal>();
            p.RuleWeights ??= new Dictionary<string, double>();
            var guids = new HashSet<string> { p.Project.Guid };
            var targets = new HashSet<string>();
            foreach (var t in p.Targets) {
                if (t == null || string.IsNullOrEmpty(t.Guid) || string.IsNullOrWhiteSpace(t.Name) || !guids.Add(t.Guid)) {
                    return "each target needs a unique guid and a name";
                }
                targets.Add(t.Guid);
                if (t.RaHours < 0 || t.RaHours >= 24 || t.Dec < -90 || t.Dec > 90) {
                    return $"{t.Name} coordinates out of range";
                }
                var designationError = TargetBodyHandler.CheckDesignation(t.Designation);
                if (designationError != null) {
                    return $"{t.Name} {designationError}";
                }
                t.Plans ??= new List<NewPlan>();
                foreach (var pl in t.Plans) {
                    var error = CheckPlan(pl);
                    if (error != null) {
                        return error;
                    }
                    if (!guids.Add(pl.Guid)) {
                        return "each plan needs a unique guid";
                    }
                }
            }
            var goals = new HashSet<(string, string)>();
            foreach (var g in p.Goals) {
                if (g == null || !targets.Contains(g.TargetGuid) || string.IsNullOrEmpty(g.Filter)) {
                    return "a goal names an unknown target";
                }
                if (!goals.Add((g.TargetGuid, g.Filter))) {
                    return "a goal is listed twice";
                }
                var error = g.Setting == null ? "a goal needs a setting" : CheckGoal(g.Setting);
                if (error != null) {
                    return error;
                }
            }
            if (p.RuleWeights.Any(kv => string.IsNullOrEmpty(kv.Key) || kv.Value < 0 || kv.Value > 100)) {
                return "weights run from 0 to 100";
            }
            return null;
        }

        protected override string Label(ProjectCreatePayload p) {
            return p.Project.Name;
        }

        protected override ChangeOutcome Change(ProjectCreatePayload p, ICommandContext context) {
            return delete ? context.Database.DeleteProject(p) : context.Database.CreateProject(p, context.ProfileId, context.DefaultRuleWeights, context.Now);
        }
    }

    public class TargetBodyHandler : PlanningHandler<TargetBodyPayload> {
        public const int MaxDesignationLength = 200;

        public override string Kind => CommandKinds.TargetBody;

        public static string CheckDesignation(string designation) {
            if (designation != null && designation.Trim().Length > MaxDesignationLength) {
                return $"designation is longer than {MaxDesignationLength} characters";
            }
            return null;
        }

        protected override string Check(TargetBodyPayload p) {
            if (string.IsNullOrWhiteSpace(p.TargetGuid)) {
                return "missing target_guid";
            }
            return CheckDesignation(p.Designation);
        }

        protected override string Label(TargetBodyPayload p) {
            return p.TargetName ?? p.TargetGuid;
        }

        protected override ChangeOutcome Change(TargetBodyPayload p, ICommandContext context) {
            return context.Database.SetTargetBody(p, context.Now);
        }
    }
}
