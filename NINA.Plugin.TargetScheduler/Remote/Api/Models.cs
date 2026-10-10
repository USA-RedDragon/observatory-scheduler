using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public class RuleScore {

        [JsonPropertyName("rule")]
        public string Rule { get; set; }

        [JsonPropertyName("weight")]
        public double Weight { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }
    }

    public class StatusTarget {

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("project_guid")]
        public string ProjectGuid { get; set; }

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("target_guid")]
        public string TargetGuid { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("is_mosaic")]
        public bool IsMosaic { get; set; }

        [JsonPropertyName("ra_hours")]
        public double RaHours { get; set; }

        [JsonPropertyName("dec_degrees")]
        public double DecDegrees { get; set; }

        [JsonPropertyName("rotation")]
        public double Rotation { get; set; }

        [JsonPropertyName("picked_at")]
        public DateTime? PickedAt { get; set; }

        [JsonPropertyName("minimum_time_end")]
        public DateTime? MinimumTimeEnd { get; set; }

        [JsonPropertyName("hard_stop")]
        public DateTime? HardStop { get; set; }
    }

    public class StatusExposure {

        [JsonPropertyName("plan_id")]
        public int PlanId { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("seconds")]
        public double Seconds { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ends_at")]
        public DateTime EndsAt { get; set; }

        [JsonPropertyName("number")]
        public int Number { get; set; }
    }

    public class StatusWait {

        [JsonPropertyName("until")]
        public DateTime Until { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }
    }

    public class StatusPause {

        [JsonPropertyName("mount")]
        public string Mount { get; set; }

        [JsonPropertyName("resume_at")]
        public DateTime? ResumeAt { get; set; }
    }

    public class StatusPending {

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("received_at")]
        public DateTime ReceivedAt { get; set; }
    }

    public class Status {

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("time")]
        public DateTime Time { get; set; }

        [JsonPropertyName("api")]
        public bool Api { get; set; } = true;

        [JsonPropertyName("web_editing")]
        public bool WebEditing { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("paused")]
        public bool Paused { get; set; }

        [JsonPropertyName("pause_requested")]
        public bool PauseRequested { get; set; }

        [JsonPropertyName("pause")]
        public StatusPause Pause { get; set; }

        [JsonPropertyName("target")]
        public StatusTarget Target { get; set; }

        [JsonPropertyName("exposure")]
        public StatusExposure Exposure { get; set; }

        [JsonPropertyName("wait")]
        public StatusWait Wait { get; set; }

        [JsonPropertyName("activity")]
        public string Activity { get; set; }

        [JsonPropertyName("activity_detail")]
        public string ActivityDetail { get; set; }

        [JsonPropertyName("activity_since")]
        public DateTime? ActivitySince { get; set; }

        [JsonPropertyName("scores")]
        public List<RuleScore> Scores { get; set; }

        [JsonPropertyName("score_total")]
        public double? ScoreTotal { get; set; }

        [JsonPropertyName("skips")]
        public IList<SkipEntry> Skips { get; set; } = new List<SkipEntry>();

        [JsonPropertyName("pending")]
        public List<StatusPending> Pending { get; set; } = new List<StatusPending>();

        [JsonPropertyName("last_plan_at")]
        public DateTime? LastPlanAt { get; set; }
    }

    public class PreviewOverride {

        [JsonPropertyName("entity")]
        public string Entity { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("field")]
        public string Field { get; set; }

        [JsonPropertyName("value")]
        public double Value { get; set; }
    }

    public class PreviewRequest {

        [JsonPropertyName("start")]
        public DateTime? Start { get; set; }

        [JsonPropertyName("overrides")]
        public List<PreviewOverride> Overrides { get; set; } = new List<PreviewOverride>();
    }

    public class Twilight {

        [JsonPropertyName("sunset")]
        public DateTime? Sunset { get; set; }

        [JsonPropertyName("sunrise")]
        public DateTime? Sunrise { get; set; }

        [JsonPropertyName("civil_dusk")]
        public DateTime? CivilDusk { get; set; }

        [JsonPropertyName("nautical_dusk")]
        public DateTime? NauticalDusk { get; set; }

        [JsonPropertyName("astronomical_dusk")]
        public DateTime? AstronomicalDusk { get; set; }

        [JsonPropertyName("astronomical_dawn")]
        public DateTime? AstronomicalDawn { get; set; }

        [JsonPropertyName("nautical_dawn")]
        public DateTime? NauticalDawn { get; set; }

        [JsonPropertyName("civil_dawn")]
        public DateTime? CivilDawn { get; set; }
    }

    public class RunnerUp {

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("total")]
        public double Total { get; set; }
    }

    public class PreviewBlock {

        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("end")]
        public DateTime End { get; set; }

        [JsonPropertyName("wait")]
        public bool Wait { get; set; }

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("exposure_seconds")]
        public double ExposureSeconds { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("picked")]
        public bool Picked { get; set; }

        [JsonPropertyName("scores")]
        public List<RuleScore> Scores { get; set; }

        [JsonPropertyName("total")]
        public double? Total { get; set; }

        [JsonPropertyName("runner_up")]
        public RunnerUp RunnerUp { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }

    public class LeftOut {

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }

    public class Preview {

        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("end")]
        public DateTime End { get; set; }

        [JsonPropertyName("generated_at")]
        public DateTime GeneratedAt { get; set; }

        [JsonPropertyName("twilight")]
        public Twilight Twilight { get; set; }

        [JsonPropertyName("blocks")]
        public List<PreviewBlock> Blocks { get; set; } = new List<PreviewBlock>();

        [JsonPropertyName("left_out")]
        public List<LeftOut> LeftOut { get; set; } = new List<LeftOut>();

        [JsonPropertyName("skips")]
        public IList<SkipEntry> Skips { get; set; } = new List<SkipEntry>();
    }

    public class PlannedExposure {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool Wait { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public int TargetId { get; set; }
        public string TargetName { get; set; }
        public string Filter { get; set; }
        public double ExposureSeconds { get; set; }
        public bool NewPick { get; set; }
        public List<RuleScore> Scores { get; set; }
        public double? Total { get; set; }
        public RunnerUp RunnerUp { get; set; }
        public DateTime? TargetEnd { get; set; }
        public bool CompletesTarget { get; set; }
    }

    public interface IRemoteBackend {

        Status Status();

        CommandResult Submit(CommandEnvelope command);

        CommandResult Get(string id);

        CommandResult Cancel(string id);

        Preview Preview(PreviewRequest request);

        event Action<string, object> Pushed;
    }
}
