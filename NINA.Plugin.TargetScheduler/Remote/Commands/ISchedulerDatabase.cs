using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public enum ColumnType {
        Integer,
        Boolean,
        Real,
        Text
    }

    public class EditableColumn {
        public string Field { get; }
        public string Column { get; }
        public ColumnType Type { get; }
        public double? Min { get; }
        public double? Max { get; }
        public IReadOnlyList<double> Also { get; }

        public EditableColumn(string field, string column, ColumnType type, double? min = null, double? max = null, params double[] also) {
            Field = field;
            Column = column;
            Type = type;
            Min = min;
            Max = max;
            Also = also ?? Array.Empty<double>();
        }

        public string Check(double v) {
            if (Type == ColumnType.Boolean) {
                return v == 0 || v == 1 ? null : $"{Field} must be true or false";
            }
            if (Type == ColumnType.Integer && v != Math.Floor(v)) {
                return $"{Field} must be a whole number";
            }
            if (Also.Contains(v)) {
                return null;
            }
            if ((Min.HasValue && v < Min.Value) || (Max.HasValue && v > Max.Value)) {
                var also = Also.Count == 0 ? "" : " or " + string.Join(" or ", Also.Select(a => a.ToString(CultureInfo.InvariantCulture)));
                return $"{Field} must be between {Min} and {Max}{also}";
            }
            return null;
        }
    }

    public class EditableTable {
        public string Entity { get; }
        public string Table { get; }
        public IReadOnlyDictionary<string, EditableColumn> Columns { get; }

        public EditableTable(string entity, string table, params EditableColumn[] columns) {
            Entity = entity;
            Table = table;
            var d = new Dictionary<string, EditableColumn>();
            foreach (var c in columns) {
                d[c.Field] = c;
            }
            Columns = d;
        }
    }

    public class ColumnChange {
        public EditableColumn Column { get; set; }
        public object Before { get; set; }
        public object After { get; set; }
    }

    public class EditConflict {
        public string Field { get; set; }
        public object Expected { get; set; }
        public object Actual { get; set; }
    }

    public class EditOutcome {
        public bool Found { get; set; }
        public bool Applied { get; set; }
        public List<EditConflict> Conflicts { get; set; } = new List<EditConflict>();
        public string Invalid { get; set; }
        public int? ProjectId { get; set; }
        public int? TargetId { get; set; }
    }

    public class EditItem {
        public int Id { get; set; }
        public string Label { get; set; }
        public IList<ColumnChange> Changes { get; set; }
    }

    public class ChangeOutcome {
        public string Status { get; set; } = CommandStatus.Applied;
        public string Message { get; set; }
        public List<Dictionary<string, object>> Conflicts { get; } = new List<Dictionary<string, object>>();
        public List<(int? ProjectId, int? TargetId)> Touched { get; } = new List<(int?, int?)>();
        public bool Changed { get; set; }

        public ChangeOutcome Reject(string message) {
            Status = CommandStatus.Rejected;
            Message = message;
            return this;
        }

        public void Conflict(string what, object expected, object actual) {
            Conflicts.Add(new Dictionary<string, object> { ["field"] = what, ["expected"] = expected, ["actual"] = actual });
        }

        public void Touch(int? projectId, int? targetId) {
            if (!Touched.Contains((projectId, targetId))) {
                Touched.Add((projectId, targetId));
            }
        }
    }

    public interface ISchedulerDatabase {

        EditOutcome ApplyEdit(EditableTable table, int id, IList<ColumnChange> changes);

        IList<EditOutcome> ApplyEdits(EditableTable table, IList<EditItem> items);

        ChangeOutcome ApplyGoalEdit(GoalEditPayload payload, DateTime now);

        ChangeOutcome ApplyRuleWeightEdit(RuleWeightEditPayload payload);

        ChangeOutcome CloneTemplate(TemplateClonePayload payload);

        ChangeOutcome DeleteTemplate(TemplateDeletePayload payload);

        ChangeOutcome ApplyExposureSet(ApplySetPayload payload, bool undo);

        ChangeOutcome CreateProject(ProjectCreatePayload payload, string profileId, IReadOnlyDictionary<string, double> defaultRuleWeights, DateTime now);

        ChangeOutcome DeleteProject(ProjectCreatePayload payload);

        TargetSnapshot ReadSnapshot(int targetId);
    }
}
