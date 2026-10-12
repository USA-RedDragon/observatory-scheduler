using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public static class CommandStatus {
        public const string Queued = "queued";
        public const string Pending = "pending";
        public const string Applied = "applied";
        public const string Conflict = "conflict";
        public const string Rejected = "rejected";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";

        public static bool IsFinal(string status) {
            return status == Applied || status == Conflict || status == Rejected || status == Failed || status == Cancelled;
        }
    }

    public static class CommandKinds {
        public const string ProjectEdit = "project.edit";
        public const string TargetEdit = "target.edit";
        public const string ExposurePlanEdit = "exposureplan.edit";
        public const string Skip = "scheduler.skip";
        public const string Unskip = "scheduler.unskip";
        public const string Pause = "scheduler.pause";
        public const string Resume = "scheduler.resume";
        public const string Replan = "scheduler.replan";
        public const string AbortExposure = "exposure.abort";
        public const string ExposureTemplateEdit = "exposuretemplate.edit";
        public const string ProjectBatchEdit = "project.batchedit";
        public const string ExposurePlanBatchEdit = "exposureplan.batchedit";
        public const string ExposureTemplateBatchEdit = "exposuretemplate.batchedit";
        public const string ExposureTemplateClone = "exposuretemplate.clone";
        public const string ExposureTemplateDelete = "exposuretemplate.delete";
        public const string GoalEdit = "goal.edit";
        public const string RuleWeightEdit = "ruleweight.edit";
        public const string ApplySet = "exposureplan.applyset";
        public const string UnapplySet = "exposureplan.unapplyset";
        public const string ProjectCreate = "project.create";
        public const string ProjectDelete = "project.delete";
        public const string TargetBody = "target.body";
        public const string TargetWindow = "target.window";
    }

    public class CommandEnvelope {

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement Payload { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("undo_of")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string UndoOf { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        public T PayloadAs<T>() {
            if (Payload.ValueKind == JsonValueKind.Undefined || Payload.ValueKind == JsonValueKind.Null) {
                return JsonSerializer.Deserialize<T>("{}", CommandJson.Options);
            }
            return Payload.Deserialize<T>(CommandJson.Options);
        }
    }

    public class CommandResult {

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("message")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Message { get; set; }

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Detail { get; set; }

        [JsonPropertyName("applies_at")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? AppliesAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        public static CommandResult Of(string id, string status, string message = null, object detail = null) {
            return new CommandResult { Id = id, Status = status, Message = message, Detail = detail, UpdatedAt = DateTime.UtcNow };
        }
    }

    public class FieldChange {

        [JsonPropertyName("field")]
        public string Field { get; set; }

        [JsonPropertyName("before")]
        public JsonElement Before { get; set; }

        [JsonPropertyName("after")]
        public JsonElement After { get; set; }
    }

    public class EditPayload {

        [JsonPropertyName("entity")]
        public string Entity { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("parent")]
        public string Parent { get; set; }

        [JsonPropertyName("changes")]
        public List<FieldChange> Changes { get; set; } = new List<FieldChange>();
    }

    public class SkipPayload {

        [JsonPropertyName("scope")]
        public string Scope { get; set; }

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("minutes")]
        public int Minutes { get; set; }
    }

    public class PausePayload {

        [JsonPropertyName("mount")]
        public string Mount { get; set; } = "track";

        [JsonPropertyName("resume_after_minutes")]
        public int ResumeAfterMinutes { get; set; }

        [JsonPropertyName("resume_at")]
        public string ResumeAt { get; set; }
    }

    public class AbortExposurePayload {

        [JsonPropertyName("plan_id")]
        public int PlanId { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime? StartedAt { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }
    }

    public static class CommandJson {

        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
    }
}
