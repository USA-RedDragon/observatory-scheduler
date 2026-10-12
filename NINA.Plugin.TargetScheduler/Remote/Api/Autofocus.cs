using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class AutofocusStates {
        public const string Running = "running";
        public const string Finished = "finished";
        public const string Failed = "failed";
    }

    public class AutofocusPoint {

        [JsonPropertyName("position")]
        public double Position { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }

        [JsonPropertyName("error")]
        public double? Error { get; set; }
    }

    public class AutofocusResult {
        public string Filter { get; set; }
        public double? Temperature { get; set; }
        public string Autofocuser { get; set; }
        public string Method { get; set; }
        public string Fitting { get; set; }
        public double? DurationSeconds { get; set; }
        public double? InitialPosition { get; set; }
        public double? ChosenPosition { get; set; }
        public double? ChosenValue { get; set; }
        public List<AutofocusPoint> Points { get; set; } = new List<AutofocusPoint>();
        public string Fit { get; set; }
    }

    public class StatusAutofocus {

        [JsonPropertyName("run_id")]
        public string RunId { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonPropertyName("finished_at")]
        public DateTime? FinishedAt { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("autofocuser")]
        public string Autofocuser { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; }

        [JsonPropertyName("fitting")]
        public string Fitting { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("initial_position")]
        public double? InitialPosition { get; set; }

        [JsonPropertyName("previous_position")]
        public double? PreviousPosition { get; set; }

        [JsonPropertyName("previous_value")]
        public double? PreviousValue { get; set; }

        [JsonPropertyName("chosen_position")]
        public double? ChosenPosition { get; set; }

        [JsonPropertyName("chosen_value")]
        public double? ChosenValue { get; set; }

        [JsonPropertyName("points")]
        public List<AutofocusPoint> Points { get; set; } = new List<AutofocusPoint>();

        [JsonPropertyName("fit")]
        public JsonElement? Fit { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }
    }

    public class AutofocusRun {
        public string Id { get; set; }
        public string ProfileId { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public double? DurationSeconds { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Filter { get; set; }
        public double? Temperature { get; set; }
        public string Autofocuser { get; set; }
        public string Method { get; set; }
        public string Fitting { get; set; }
        public double? InitialPosition { get; set; }
        public double? PreviousPosition { get; set; }
        public double? PreviousValue { get; set; }
        public double? ChosenPosition { get; set; }
        public double? ChosenValue { get; set; }
        public List<AutofocusPoint> Points { get; set; } = new List<AutofocusPoint>();
        public string Fit { get; set; }
        public string TargetName { get; set; }
        public string TargetGuid { get; set; }
        public string Source { get; set; }
    }

    public static class AutofocusReport {
        public const string SourceEvent = "event";
        public const string SourceReport = "report";

        public static AutofocusResult Parse(string json) {
            if (string.IsNullOrWhiteSpace(json)) {
                return null;
            }
            JsonNode root;
            try {
                root = JsonNode.Parse(json);
            } catch (JsonException) {
                return null;
            }
            if (root is not JsonObject o) {
                return null;
            }
            var r = new AutofocusResult {
                Filter = Text(o, "Filter"),
                Temperature = Number(o, "Temperature"),
                Autofocuser = Text(o, "AutoFocuserName"),
                Method = Text(o, "Method"),
                Fitting = Text(o, "Fitting"),
                DurationSeconds = Duration(o["Duration"]),
                InitialPosition = Number(o["InitialFocusPoint"] as JsonObject, "Position"),
                ChosenPosition = Number(o["CalculatedFocusPoint"] as JsonObject, "Position"),
                ChosenValue = Number(o["CalculatedFocusPoint"] as JsonObject, "Value"),
            };
            if (o["MeasurePoints"] is JsonArray points) {
                foreach (var p in points.OfType<JsonObject>()) {
                    var pos = Number(p, "Position");
                    var val = Number(p, "Value");
                    if (pos.HasValue && val.HasValue) {
                        r.Points.Add(new AutofocusPoint { Position = pos.Value, Value = val.Value, Error = Number(p, "Error") });
                    }
                }
            }
            var fit = new JsonObject();
            foreach (var (from, to) in new[] { ("Fittings", "fittings"), ("RSquares", "r_squared"), ("Intersections", "intersections") }) {
                if (Clean(o[from]) is JsonObject section && section.Count > 0) {
                    fit[to] = section;
                }
            }
            r.Fit = fit.Count == 0 ? null : fit.ToJsonString();
            return r;
        }

        private static JsonNode Clean(JsonNode n) {
            switch (n) {
                case null:
                    return null;
                case JsonObject obj: {
                        var copy = new JsonObject();
                        foreach (var kv in obj) {
                            var c = Clean(kv.Value);
                            if (c != null) {
                                copy[kv.Key] = c;
                            }
                        }
                        return copy.Count == 0 ? null : copy;
                    }
                case JsonArray arr: {
                        var copy = new JsonArray();
                        foreach (var item in arr) {
                            var c = Clean(item);
                            if (c != null) {
                                copy.Add(c);
                            }
                        }
                        return copy;
                    }
                case JsonValue v:
                    if (v.TryGetValue(out string s)) {
                        if (string.IsNullOrWhiteSpace(s)) {
                            return null;
                        }
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && (double.IsNaN(d) || double.IsInfinity(d))) {
                            return null;
                        }
                    }
                    return v.DeepClone();
                default:
                    return null;
            }
        }

        private static string Text(JsonObject o, string name) {
            if (o == null || o[name] is not JsonValue v) {
                return null;
            }
            if (v.TryGetValue(out string s)) {
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            return v.ToJsonString();
        }

        private static double? Number(JsonObject o, string name) {
            if (o == null || o[name] is not JsonValue v) {
                return null;
            }
            double d;
            if (v.TryGetValue(out double x)) {
                d = x;
            } else if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) {
                d = y;
            } else {
                return null;
            }
            return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        }

        private static double? Duration(JsonNode n) {
            if (n is not JsonValue v) {
                return null;
            }
            if (v.TryGetValue(out string s) && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts)) {
                return ts.TotalSeconds;
            }
            if (v.TryGetValue(out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) {
                return d;
            }
            return null;
        }
    }

    public class AutofocusCompletion {
        public string RunId { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public DateTime FinishedAt { get; set; }
        public string Filter { get; set; }
        public double? Temperature { get; set; }
        public double? Position { get; set; }
        public string ProfileId { get; set; }
        public string TargetName { get; set; }
        public string TargetGuid { get; set; }
    }

    public static class AutofocusRuns {
        public const int MaxPoints = 500;

        public static bool IsReanalysis(AutofocusResult report) {
            return report?.DurationSeconds is double d && d <= 0;
        }

        public static AutofocusRun Build(AutofocusCompletion done, AutofocusResult report, AutofocusRun previous = null) {
            var finished = DateTime.SpecifyKind(done.FinishedAt, DateTimeKind.Utc);
            var duration = Finite(report?.DurationSeconds);
            var run = new AutofocusRun {
                Id = string.IsNullOrEmpty(done.RunId) ? Guid.NewGuid().ToString() : done.RunId,
                ProfileId = done.ProfileId,
                FinishedAt = finished,
                StartedAt = duration.HasValue && duration.Value >= 0 ? finished.AddSeconds(-duration.Value) : null,
                DurationSeconds = duration,
                Success = done.Success,
                Error = done.Success ? null : done.Error,
                Filter = report?.Filter ?? done.Filter,
                Temperature = Finite(report?.Temperature) ?? Finite(done.Temperature),
                Autofocuser = report?.Autofocuser,
                Method = report?.Method,
                Fitting = report?.Fitting,
                InitialPosition = Finite(report?.InitialPosition),
                PreviousPosition = Finite(previous?.ChosenPosition),
                PreviousValue = Finite(previous?.ChosenValue),
                Fit = report?.Fit,
                TargetName = done.TargetName,
                TargetGuid = done.TargetGuid,
                Source = report == null ? AutofocusReport.SourceEvent : AutofocusReport.SourceReport,
            };
            if (report != null) {
                run.Points.AddRange(report.Points.Take(MaxPoints));
            }
            if (done.Success) {
                run.ChosenPosition = Finite(report?.ChosenPosition) ?? Finite(done.Position);
                run.ChosenValue = Finite(report?.ChosenValue);
            }
            return run;
        }

        public static StatusAutofocus Status(AutofocusRun run) {
            if (run == null) {
                return null;
            }
            return new StatusAutofocus {
                RunId = run.Id,
                State = run.Success ? AutofocusStates.Finished : AutofocusStates.Failed,
                StartedAt = run.StartedAt,
                FinishedAt = run.FinishedAt,
                Filter = run.Filter,
                Temperature = run.Temperature,
                Autofocuser = run.Autofocuser,
                Method = run.Method,
                Fitting = run.Fitting,
                TargetName = run.TargetName,
                InitialPosition = run.InitialPosition,
                PreviousPosition = run.PreviousPosition,
                PreviousValue = run.PreviousValue,
                ChosenPosition = run.ChosenPosition,
                ChosenValue = run.ChosenValue,
                Points = run.Points.ToList(),
                Fit = ParseFit(run.Fit),
                Error = run.Error,
            };
        }

        private static JsonElement? ParseFit(string fit) {
            if (string.IsNullOrWhiteSpace(fit)) {
                return null;
            }
            try {
                using var doc = JsonDocument.Parse(fit);
                return doc.RootElement.Clone();
            } catch (JsonException) {
                return null;
            }
        }

        private static double? Finite(double? d) {
            return d.HasValue && !double.IsNaN(d.Value) && !double.IsInfinity(d.Value) ? d : null;
        }
    }

    public class AutofocusReports {
        private readonly object sync = new object();
        private readonly Dictionary<string, string> claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ProfileOf(string path) {
            var name = Path.GetFileNameWithoutExtension(path ?? "");
            var cut = name.LastIndexOf("--", StringComparison.Ordinal);
            return cut >= 0 && Guid.TryParse(name.Substring(cut + 2), out var g) ? g.ToString() : null;
        }

        public static bool Mine(string path, string profileId) {
            if (string.IsNullOrEmpty(profileId)) {
                return true;
            }
            var owner = ProfileOf(path);
            return owner != null && string.Equals(owner, profileId, StringComparison.OrdinalIgnoreCase);
        }

        public string ClaimForFailure(string path) {
            if (string.IsNullOrEmpty(path)) {
                return null;
            }
            lock (sync) {
                if (claimed.ContainsKey(path)) {
                    return null;
                }
                var id = Guid.NewGuid().ToString();
                claimed[path] = id;
                return id;
            }
        }

        public (string Path, string FailedRunId) ClaimForSuccess(IEnumerable<(string Path, DateTime WrittenUtc)> files, DateTime nowUtc, TimeSpan window, string profileId) {
            lock (sync) {
                var newest = files
                    .Where(f => (nowUtc - f.WrittenUtc).Duration() <= window)
                    .Where(f => ProfileOf(f.Path) == null || Mine(f.Path, profileId))
                    .OrderByDescending(f => f.WrittenUtc)
                    .Select(f => f.Path)
                    .FirstOrDefault();
                if (newest == null) {
                    return (null, null);
                }
                if (!claimed.TryGetValue(newest, out var id)) {
                    claimed[newest] = "";
                    return (newest, null);
                }
                if (id.Length > 0) {
                    claimed[newest] = "";
                    return (newest, id);
                }
                return (null, null);
            }
        }
    }

    public class AutofocusLatest {
        private readonly object sync = new object();
        private AutofocusRun last;

        public event Action Changed;

        public void Record(AutofocusRun run) {
            lock (sync) {
                last = run;
            }
            try {
                Changed?.Invoke();
            } catch (Exception) {
            }
        }

        public StatusAutofocus Snapshot() {
            lock (sync) {
                return AutofocusRuns.Status(last);
            }
        }
    }
}
