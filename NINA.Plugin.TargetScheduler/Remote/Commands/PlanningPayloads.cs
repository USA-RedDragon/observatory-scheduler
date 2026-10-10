using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class GoalSetting {

        [JsonPropertyName("kind")]
        public int Kind { get; set; }

        [JsonPropertyName("snr_goal")]
        public double SnrGoal { get; set; }

        [JsonPropertyName("depth_goal")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? DepthGoal { get; set; }

        [JsonPropertyName("plateau_stop")]
        public bool PlateauStop { get; set; }

        [JsonPropertyName("region")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Region { get; set; }
    }

    public class GoalChange {

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_guid")]
        public string TargetGuid { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("before")]
        public GoalSetting Before { get; set; }

        [JsonPropertyName("after")]
        public GoalSetting After { get; set; }
    }

    public class GoalEditPayload {

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("goals")]
        public List<GoalChange> Goals { get; set; } = new List<GoalChange>();
    }

    public class RuleWeightChange {

        [JsonPropertyName("rule")]
        public string Rule { get; set; }

        [JsonPropertyName("before")]
        public double? Before { get; set; }

        [JsonPropertyName("after")]
        public double? After { get; set; }
    }

    public class RuleWeightEditPayload {

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("project_guid")]
        public string ProjectGuid { get; set; }

        [JsonPropertyName("project_name")]
        public string ProjectName { get; set; }

        [JsonPropertyName("changes")]
        public List<RuleWeightChange> Changes { get; set; } = new List<RuleWeightChange>();
    }

    public class BatchEditPayload {

        [JsonPropertyName("items")]
        public List<EditPayload> Items { get; set; } = new List<EditPayload>();
    }

    public class TemplateClonePayload {

        [JsonPropertyName("source_id")]
        public int SourceId { get; set; }

        [JsonPropertyName("source_name")]
        public string SourceName { get; set; }

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("defaultexposure")]
        public double? DefaultExposure { get; set; }

        [JsonPropertyName("gain")]
        public int? Gain { get; set; }
    }

    public class TemplateDeletePayload {

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("clone")]
        public TemplateClonePayload Clone { get; set; }
    }

    public class NewPlan {

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("template_id")]
        public int TemplateId { get; set; }

        [JsonPropertyName("template_name")]
        public string TemplateName { get; set; }

        [JsonPropertyName("exposure")]
        public double Exposure { get; set; }

        [JsonPropertyName("desired")]
        public int Desired { get; set; }
    }

    public class PlanRef {

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("template_name")]
        public string TemplateName { get; set; }
    }

    public class ApplySetTarget {

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("target_guid")]
        public string TargetGuid { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("project")]
        public string Project { get; set; }

        [JsonPropertyName("create")]
        public List<NewPlan> Create { get; set; } = new List<NewPlan>();

        [JsonPropertyName("disable")]
        public List<PlanRef> Disable { get; set; } = new List<PlanRef>();

        [JsonPropertyName("enable")]
        public List<PlanRef> Enable { get; set; } = new List<PlanRef>();
    }

    public class ApplySetPayload {

        [JsonPropertyName("set_id")]
        public string SetId { get; set; }

        [JsonPropertyName("set_name")]
        public string SetName { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; }

        [JsonPropertyName("targets")]
        public List<ApplySetTarget> Targets { get; set; } = new List<ApplySetTarget>();
    }

    public class NewProject {

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        [JsonPropertyName("state")]
        public int State { get; set; }

        [JsonPropertyName("minimumtime")]
        public int MinimumTime { get; set; }

        [JsonPropertyName("minimumaltitude")]
        public double MinimumAltitude { get; set; }

        [JsonPropertyName("is_mosaic")]
        public bool IsMosaic { get; set; }
    }

    public class NewTarget {

        [JsonPropertyName("guid")]
        public string Guid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("ra_hours")]
        public double RaHours { get; set; }

        [JsonPropertyName("dec")]
        public double Dec { get; set; }

        [JsonPropertyName("rotation")]
        public double Rotation { get; set; }

        [JsonPropertyName("plans")]
        public List<NewPlan> Plans { get; set; } = new List<NewPlan>();
    }

    public class NewGoal {

        [JsonPropertyName("target_guid")]
        public string TargetGuid { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("setting")]
        public GoalSetting Setting { get; set; }
    }

    public class ProjectCreatePayload {

        [JsonPropertyName("project")]
        public NewProject Project { get; set; }

        [JsonPropertyName("targets")]
        public List<NewTarget> Targets { get; set; } = new List<NewTarget>();

        [JsonPropertyName("goals")]
        public List<NewGoal> Goals { get; set; } = new List<NewGoal>();

        [JsonPropertyName("rule_weights")]
        public Dictionary<string, double> RuleWeights { get; set; } = new Dictionary<string, double>();

        [JsonPropertyName("catalog")]
        public string Catalog { get; set; }

        [JsonPropertyName("match")]
        public string Match { get; set; }

        [JsonPropertyName("match_with")]
        public string MatchWith { get; set; }
    }
}
