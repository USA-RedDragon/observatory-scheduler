using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public enum CommandTiming {
        Immediate,
        AtExposureEnd
    }

    public interface ICommandHandler {
        string Kind { get; }
        CommandTiming Timing { get; }
        bool IsEdit { get; }

        string Validate(CommandEnvelope command);

        CommandResult Apply(CommandEnvelope command, ICommandContext context);
    }

    public interface ICommandContext {
        DateTime Now { get; }
        string ProfileId { get; }
        IReadOnlyCollection<string> ProfileFilters { get; }
        IReadOnlyDictionary<string, double> DefaultRuleWeights { get; }
        ISchedulerDatabase Database { get; }
        ISchedulerControl Control { get; }
    }

    public interface ISchedulerControl {

        void RequestReplan(string reason);

        void Skip(SkipPayload skip, DateTime until);

        bool Unskip(SkipPayload skip);

        void Pause(PausePayload pause, DateTime? resumeAt);

        bool Resume();

        AbortOutcome AbortExposure(string reason, int planId, DateTime startedAt);

        string AbortBlocked { get; }

        DateTime? NightEnd(DateTime now);

        void Invalidate(int? projectId, int? targetId);
    }
}
