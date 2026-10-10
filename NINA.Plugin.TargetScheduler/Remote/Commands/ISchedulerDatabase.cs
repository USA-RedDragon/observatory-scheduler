using System.Collections.Generic;

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

        public EditableColumn(string field, string column, ColumnType type, double? min = null, double? max = null) {
            Field = field;
            Column = column;
            Type = type;
            Min = min;
            Max = max;
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
        public int? ProjectId { get; set; }
        public int? TargetId { get; set; }
    }

    public interface ISchedulerDatabase {

        EditOutcome ApplyEdit(EditableTable table, int id, IList<ColumnChange> changes);
    }
}
