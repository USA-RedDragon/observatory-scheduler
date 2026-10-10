using System;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class PendingTiming {
        public const string WhenExposureEnds = "Applies when the current exposure ends.";
        public const string AfterNextExposure = "Applies after the next exposure ends.";
        public const string AfterSaving = "Applies once the current exposure is downloaded and saved.";
        public const string BeforeNextExposure = "Applies before the next exposure starts.";

        public DateTime? AppliesAt { get; set; }
        public string Message { get; set; }

        public static PendingTiming At(DateTime? appliesAt, string message) {
            return new PendingTiming { AppliesAt = appliesAt, Message = message };
        }
    }
}
