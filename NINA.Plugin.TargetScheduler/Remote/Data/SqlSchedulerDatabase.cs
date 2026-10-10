using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlSchedulerDatabase : ISchedulerDatabase {
        private readonly Func<DbConnection> connect;

        public SqlSchedulerDatabase(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public EditOutcome ApplyEdit(EditableTable table, int id, IList<ColumnChange> changes) {
            var outcome = new EditOutcome();
            using var conn = connect();
            using var tx = conn.BeginTransaction();
            var cols = changes.Select(c => Quote(c.Column.Column)).ToList();
            string link = table.Table switch {
                "project" => "Id",
                "target" => "projectid",
                "exposureplan" => "targetid",
                _ => "NULL"
            };
            using (var sel = conn.CreateCommand()) {
                sel.Transaction = tx;
                sel.CommandText = $"SELECT {link}, {string.Join(", ", cols)} FROM {Quote(table.Table)} WHERE Id = @id";
                SqlCommandStore.Add(sel, "@id", id);
                using var r = sel.ExecuteReader();
                if (!r.Read()) {
                    return outcome;
                }
                outcome.Found = true;
                object linkValue = r.IsDBNull(0) ? null : r.GetValue(0);
                for (int i = 0; i < changes.Count; i++) {
                    var actual = Normalize(changes[i].Column, r.IsDBNull(i + 1) ? null : r.GetValue(i + 1));
                    var expected = Normalize(changes[i].Column, changes[i].Before);
                    if (!Equals(actual, expected)) {
                        outcome.Conflicts.Add(new EditConflict { Field = changes[i].Column.Field, Expected = expected, Actual = actual });
                    }
                }
                switch (table.Table) {
                    case "project":
                        outcome.ProjectId = id;
                        break;

                    case "target":
                        outcome.TargetId = id;
                        outcome.ProjectId = ToInt(linkValue);
                        break;

                    case "exposureplan":
                        outcome.TargetId = ToInt(linkValue);
                        break;
                }
            }
            if (outcome.Conflicts.Count > 0) {
                tx.Rollback();
                return outcome;
            }
            if (table.Table == "exposureplan" && outcome.TargetId.HasValue) {
                using var q = conn.CreateCommand();
                q.Transaction = tx;
                q.CommandText = "SELECT projectid FROM target WHERE Id = @id";
                SqlCommandStore.Add(q, "@id", outcome.TargetId.Value);
                outcome.ProjectId = ToInt(q.ExecuteScalar());
            }
            using (var upd = conn.CreateCommand()) {
                upd.Transaction = tx;
                var sets = changes.Select((c, i) => $"{Quote(c.Column.Column)} = @v{i}");
                upd.CommandText = $"UPDATE {Quote(table.Table)} SET {string.Join(", ", sets)} WHERE Id = @id";
                for (int i = 0; i < changes.Count; i++) {
                    SqlCommandStore.Add(upd, $"@v{i}", Normalize(changes[i].Column, changes[i].After));
                }
                SqlCommandStore.Add(upd, "@id", id);
                upd.ExecuteNonQuery();
            }
            tx.Commit();
            outcome.Applied = true;
            return outcome;
        }

        private static int? ToInt(object v) {
            if (v == null || v is DBNull) {
                return null;
            }
            return Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        public static object Normalize(EditableColumn col, object v) {
            if (v == null || v is DBNull) {
                return null;
            }
            switch (col.Type) {
                case ColumnType.Boolean:
                    if (v is bool b) {
                        return b ? 1L : 0L;
                    }
                    return Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0 ? 1L : 0L;

                case ColumnType.Integer:
                    return Convert.ToInt64(v, CultureInfo.InvariantCulture);

                case ColumnType.Real:
                    return Convert.ToDouble(v, CultureInfo.InvariantCulture);

                default:
                    return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        private static string Quote(string identifier) {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
