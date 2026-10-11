using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class EditHandler : ICommandHandler {
        public string Kind { get; }
        public CommandTiming Timing => CommandTiming.AtExposureEnd;
        public bool IsEdit => true;
        public EditableTable Table { get; }

        public EditHandler(string kind, EditableTable table) {
            Kind = kind;
            Table = table;
        }

        public static EditHandler Project() {
            return new EditHandler(CommandKinds.ProjectEdit, new EditableTable("project", "project", new[] {
                new EditableColumn("state", "state", ColumnType.Integer, 0, 3),
                new EditableColumn("priority", "priority", ColumnType.Integer, 0, 2),
                new EditableColumn("minimumtime", "minimumtime", ColumnType.Integer, 1, 1440),
                new EditableColumn("minimumaltitude", "minimumaltitude", ColumnType.Real, 0, 90)
            }.Concat(ProjectSettings.Columns).ToArray()));
        }

        public static EditHandler Target() {
            return new EditHandler(CommandKinds.TargetEdit, new EditableTable("target", "target",
                new EditableColumn("active", "active", ColumnType.Boolean)));
        }

        public static EditHandler ExposurePlan() {
            return new EditHandler(CommandKinds.ExposurePlanEdit, new EditableTable("exposureplan", "exposureplan",
                new EditableColumn("enabled", "enabled", ColumnType.Boolean),
                new EditableColumn("desired", "desired", ColumnType.Integer, 0, 100000)));
        }

        public static EditHandler ExposureTemplate() {
            return new EditHandler(CommandKinds.ExposureTemplateEdit, new EditableTable("exposuretemplate", "exposuretemplate",
                new EditableColumn("defaultexposure", "defaultexposure", ColumnType.Real, 0.001, 86400),
                new EditableColumn("gain", "gain", ColumnType.Integer, -1, 100000),
                new EditableColumn("offset", "offset", ColumnType.Integer, -1, 100000),
                new EditableColumn("moonavoidanceseparation", "moonavoidanceseparation", ColumnType.Real, 0, 180),
                new EditableColumn("moonavoidancewidth", "moonavoidancewidth", ColumnType.Integer, 0, 14),
                new EditableColumn("moonavoidanceenabled", "moonavoidanceenabled", ColumnType.Boolean),
                new EditableColumn("maximumhumidity", "maximumhumidity", ColumnType.Real, 0, 100),
                new EditableColumn("twilightlevel", "twilightlevel", ColumnType.Integer, 0, 3)));
        }

        public string Validate(CommandEnvelope command) {
            EditPayload p;
            try {
                p = command.PayloadAs<EditPayload>();
            } catch (JsonException e) {
                return $"invalid payload: {e.Message}";
            }
            return ValidatePayload(p);
        }

        public string ValidatePayload(EditPayload p) {
            if (p == null || p.Id <= 0) {
                return "missing id";
            }
            if (!string.IsNullOrEmpty(p.Entity) && p.Entity != Table.Entity) {
                return $"entity {p.Entity} does not match {Table.Entity}";
            }
            if (p.Changes == null || p.Changes.Count == 0) {
                return "no changes";
            }
            var seen = new HashSet<string>();
            foreach (var c in p.Changes) {
                if (c.Field == null || !Table.Columns.TryGetValue(c.Field, out var col)) {
                    return $"{Table.Entity}.{c.Field} is not editable";
                }
                if (!seen.Add(c.Field)) {
                    return $"{c.Field} changed twice";
                }
                string err = null;
                if ((c.Before.ValueKind != JsonValueKind.Null && !TryConvert(col, c.Before, out _, out err)) || !TryConvert(col, c.After, out var after, out err)) {
                    return $"{c.Field}: {err}";
                }
                if (col.Type == ColumnType.Integer || col.Type == ColumnType.Real) {
                    var rangeError = col.Check(Convert.ToDouble(after, CultureInfo.InvariantCulture));
                    if (rangeError != null) {
                        return rangeError;
                    }
                }
            }
            return Table.Table == "project" ? AltitudeError(p) : null;
        }

        public const string MaximumAboveMinimum = "maximum altitude must be above the minimum altitude, or 0 for off";

        public static string AltitudeError(double minimum, double maximum) {
            return maximum > 0 && maximum <= minimum ? MaximumAboveMinimum : null;
        }

        private string AltitudeError(EditPayload p) {
            double? minimum = null, maximum = null;
            foreach (var c in p.Changes) {
                if (c.Field == "minimumaltitude" && c.After.ValueKind == JsonValueKind.Number) {
                    minimum = c.After.GetDouble();
                } else if (c.Field == ProjectSettings.MaximumAltitude && c.After.ValueKind == JsonValueKind.Number) {
                    maximum = c.After.GetDouble();
                }
            }
            return minimum.HasValue && maximum.HasValue ? AltitudeError(minimum.Value, maximum.Value) : null;
        }

        public CommandResult Apply(CommandEnvelope command, ICommandContext context) {
            var error = Validate(command);
            if (error != null) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, error);
            }
            var p = command.PayloadAs<EditPayload>();
            var changes = ColumnChanges(p);

            var outcome = context.Database.ApplyEdit(Table, p.Id, changes);
            if (!outcome.Found) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, $"{Table.Entity} {p.Id} no longer exists");
            }
            if (outcome.Invalid != null) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, $"{Label(p)}: {outcome.Invalid}. Nothing was applied.");
            }
            if (outcome.Conflicts.Count > 0) {
                var parts = outcome.Conflicts.Select(c => $"{c.Field} is {Show(c.Actual)}, not {Show(c.Expected)}");
                return CommandResult.Of(command.Id, CommandStatus.Conflict,
                    $"{Label(p)} changed on the observatory: {string.Join("; ", parts)}. Nothing was applied.",
                    outcome.Conflicts.Select(c => new Dictionary<string, object> { ["field"] = c.Field, ["expected"] = c.Expected, ["actual"] = c.Actual }).ToList());
            }
            context.Control.Invalidate(outcome.ProjectId, outcome.TargetId);
            context.Control.RequestReplan($"{Kind} {p.Id} by {command.Author}");
            return CommandResult.Of(command.Id, CommandStatus.Applied, null);
        }

        public List<ColumnChange> ColumnChanges(EditPayload p) {
            return p.Changes.Select(c => {
                var col = Table.Columns[c.Field];
                TryConvert(col, c.Before, out var before, out _);
                TryConvert(col, c.After, out var after, out _);
                return new ColumnChange { Column = col, Before = before, After = after };
            }).ToList();
        }

        public static string Label(EditPayload p) {
            return string.IsNullOrEmpty(p.Name) ? $"{p.Entity} {p.Id}" : p.Name;
        }

        public static string Show(object v) {
            return v == null ? "empty" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool TryConvert(EditableColumn col, JsonElement e, out object value, out string error) {
            value = null;
            error = null;
            switch (col.Type) {
                case ColumnType.Boolean:
                    if (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) {
                        value = e.GetBoolean() ? 1L : 0L;
                        return true;
                    }
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var b) && (b == 0 || b == 1)) {
                        value = b;
                        return true;
                    }
                    error = "expected true or false";
                    return false;

                case ColumnType.Integer:
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var i)) {
                        value = i;
                        return true;
                    }
                    error = "expected a whole number";
                    return false;

                case ColumnType.Real:
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d)) {
                        value = d;
                        return true;
                    }
                    error = "expected a number";
                    return false;

                case ColumnType.Text:
                    if (e.ValueKind == JsonValueKind.String) {
                        value = e.GetString();
                        return true;
                    }
                    if (e.ValueKind == JsonValueKind.Null) {
                        return true;
                    }
                    error = "expected text";
                    return false;
            }
            error = "unsupported column";
            return false;
        }
    }
}
