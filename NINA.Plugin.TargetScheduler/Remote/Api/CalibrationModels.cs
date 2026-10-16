using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public class StatusCalibrationCamera {

        [JsonPropertyName("connected")]
        public bool Connected { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("setpoint")]
        public double? Setpoint { get; set; }

        [JsonPropertyName("cooler_power")]
        public double? CoolerPower { get; set; }
    }

    public class StatusCalibrationCooling {

        [JsonPropertyName("target")]
        public double Target { get; set; }

        [JsonPropertyName("from")]
        public double? From { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ends_at")]
        public DateTime? EndsAt { get; set; }

        [JsonPropertyName("rate_c_per_min")]
        public double? RateCPerMin { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }
    }

    public class StatusCalibrationSet {

        [JsonPropertyName("combo_key")]
        public string ComboKey { get; set; }

        [JsonPropertyName("exposure")]
        public double? Exposure { get; set; }

        [JsonPropertyName("gain")]
        public double? Gain { get; set; }

        [JsonPropertyName("offset")]
        public double? Offset { get; set; }

        [JsonPropertyName("binning")]
        public double? Binning { get; set; }

        [JsonPropertyName("readout_mode")]
        public string ReadoutMode { get; set; }

        [JsonPropertyName("set_temp")]
        public double? SetTemp { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("target_id")]
        public int? TargetId { get; set; }

        [JsonPropertyName("session_date")]
        public DateTime? SessionDate { get; set; }

        [JsonPropertyName("rotation")]
        public double? Rotation { get; set; }

        [JsonPropertyName("panel_brightness")]
        public int? PanelBrightness { get; set; }

        [JsonPropertyName("frames_total")]
        public int FramesTotal { get; set; }

        [JsonPropertyName("frames_before")]
        public int FramesBefore { get; set; }

        [JsonPropertyName("frames_done")]
        public int FramesDone { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ends_at")]
        public DateTime? EndsAt { get; set; }
    }

    public class StatusCalibrationFrame {

        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("seconds")]
        public double Seconds { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ends_at")]
        public DateTime EndsAt { get; set; }
    }

    public class StatusCalibrationQueueItem {

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("combo_key")]
        public string ComboKey { get; set; }

        [JsonPropertyName("exposure")]
        public double? Exposure { get; set; }

        [JsonPropertyName("gain")]
        public double? Gain { get; set; }

        [JsonPropertyName("offset")]
        public double? Offset { get; set; }

        [JsonPropertyName("binning")]
        public double? Binning { get; set; }

        [JsonPropertyName("set_temp")]
        public double? SetTemp { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("frames_total")]
        public int FramesTotal { get; set; }

        [JsonPropertyName("frames_have")]
        public int FramesHave { get; set; }

        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }

        [JsonPropertyName("lights_blocked")]
        public int? LightsBlocked { get; set; }

        [JsonPropertyName("lights_scaled")]
        public int? LightsScaled { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("reason_code")]
        public string ReasonCode { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        [JsonPropertyName("planned_start")]
        public DateTime? PlannedStart { get; set; }

        [JsonPropertyName("planned_end")]
        public DateTime? PlannedEnd { get; set; }

        [JsonPropertyName("fit_frames")]
        public int? FitFrames { get; set; }

        public StatusCalibrationQueueItem Copy() => (StatusCalibrationQueueItem)MemberwiseClone();
    }

    public class StatusCalibrationTaken {

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("combo_key")]
        public string ComboKey { get; set; }

        [JsonPropertyName("exposure")]
        public double? Exposure { get; set; }

        [JsonPropertyName("gain")]
        public double? Gain { get; set; }

        [JsonPropertyName("offset")]
        public double? Offset { get; set; }

        [JsonPropertyName("binning")]
        public double? Binning { get; set; }

        [JsonPropertyName("set_temp")]
        public double? SetTemp { get; set; }

        [JsonPropertyName("filter")]
        public string Filter { get; set; }

        [JsonPropertyName("target_name")]
        public string TargetName { get; set; }

        [JsonPropertyName("panel_brightness")]
        public int? PanelBrightness { get; set; }

        [JsonPropertyName("frames")]
        public int Frames { get; set; }

        [JsonPropertyName("started_at")]
        public DateTime StartedAt { get; set; }

        [JsonPropertyName("ended_at")]
        public DateTime EndedAt { get; set; }

        public StatusCalibrationTaken Copy() => (StatusCalibrationTaken)MemberwiseClone();
    }

    public class StatusCalibrationStop {

        [JsonPropertyName("at")]
        public DateTime At { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }

    public class StatusCalibrationLog {

        [JsonPropertyName("at")]
        public DateTime At { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class StatusCalibration {

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("placement")]
        public string Placement { get; set; }

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; }

        [JsonPropertyName("since")]
        public DateTime? Since { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("camera")]
        public StatusCalibrationCamera Camera { get; set; }

        [JsonPropertyName("ambient")]
        public double? Ambient { get; set; }

        [JsonPropertyName("sun_limit_at")]
        public DateTime? SunLimitAt { get; set; }

        [JsonPropertyName("sun_limit_degrees")]
        public double SunLimitDegrees { get; set; }

        [JsonPropertyName("cooling")]
        public StatusCalibrationCooling Cooling { get; set; }

        [JsonPropertyName("set")]
        public StatusCalibrationSet Set { get; set; }

        [JsonPropertyName("frame")]
        public StatusCalibrationFrame Frame { get; set; }

        [JsonPropertyName("queue")]
        public List<StatusCalibrationQueueItem> Queue { get; set; } = new List<StatusCalibrationQueueItem>();

        [JsonPropertyName("taken")]
        public List<StatusCalibrationTaken> Taken { get; set; } = new List<StatusCalibrationTaken>();

        [JsonPropertyName("stop")]
        public StatusCalibrationStop Stop { get; set; }

        [JsonPropertyName("log")]
        public List<StatusCalibrationLog> Log { get; set; } = new List<StatusCalibrationLog>();
    }
}
