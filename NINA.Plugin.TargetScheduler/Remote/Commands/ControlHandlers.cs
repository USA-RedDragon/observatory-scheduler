using System;
using System.Globalization;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public abstract class ControlHandler<T> : ICommandHandler {
        public abstract string Kind { get; }
        public virtual CommandTiming Timing => CommandTiming.AtExposureEnd;
        public virtual bool IsEdit => false;

        protected virtual string Check(T payload) {
            return null;
        }

        protected abstract CommandResult Run(CommandEnvelope command, T payload, ICommandContext context);

        public string Validate(CommandEnvelope command) {
            try {
                var p = command.PayloadAs<T>();
                return p == null ? "missing payload" : Check(p);
            } catch (JsonException e) {
                return $"invalid payload: {e.Message}";
            }
        }

        public CommandResult Apply(CommandEnvelope command, ICommandContext context) {
            var error = Validate(command);
            if (error != null) {
                return CommandResult.Of(command.Id, CommandStatus.Rejected, error);
            }
            return Run(command, command.PayloadAs<T>(), context);
        }
    }

    public class EmptyPayload {
    }

    public class SkipHandler : ControlHandler<SkipPayload> {
        public override string Kind => CommandKinds.Skip;

        protected override string Check(SkipPayload p) {
            return CheckSkip(p);
        }

        public static string CheckSkip(SkipPayload p) {
            if (p.Scope == "target" && p.TargetId <= 0) {
                return "missing target_id";
            }
            if (p.Scope == "project" && p.ProjectId <= 0) {
                return "missing project_id";
            }
            if (p.Scope != "target" && p.Scope != "project") {
                return "scope must be target or project";
            }
            if (p.Minutes < 0) {
                return "negative minutes";
            }
            return null;
        }

        /// <summary>
        /// When a skip applied at now ends: after its minutes, or else at the end of the night.
        /// </summary>
        public static DateTime Until(SkipPayload p, DateTime now, Func<DateTime, DateTime?> nightEnd) {
            if (p.Minutes > 0) {
                return now.AddMinutes(p.Minutes);
            }
            return nightEnd?.Invoke(now) ?? now.AddHours(12);
        }

        protected override CommandResult Run(CommandEnvelope command, SkipPayload p, ICommandContext context) {
            DateTime until = Until(p, context.Now, context.Control.NightEnd);
            context.Control.Skip(p, until);
            context.Control.RequestReplan($"skip {p.Scope} by {command.Author}");
            var name = p.Scope == "project" ? p.ProjectName : p.TargetName;
            return CommandResult.Of(command.Id, CommandStatus.Applied, $"Skipping {name} until {until.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}");
        }
    }

    public class UnskipHandler : ControlHandler<SkipPayload> {
        public override string Kind => CommandKinds.Unskip;

        protected override string Check(SkipPayload p) {
            return SkipHandler.CheckSkip(p);
        }

        protected override CommandResult Run(CommandEnvelope command, SkipPayload p, ICommandContext context) {
            var had = context.Control.Unskip(p);
            if (had) {
                context.Control.RequestReplan($"unskip {p.Scope} by {command.Author}");
            }
            return CommandResult.Of(command.Id, CommandStatus.Applied, had ? null : "It was no longer skipped.");
        }
    }

    public class PauseHandler : ControlHandler<PausePayload> {
        public override string Kind => CommandKinds.Pause;

        protected override string Check(PausePayload p) {
            if (p.Mount != "track" && p.Mount != "park") {
                return "mount must be track or park";
            }
            if (p.ResumeAfterMinutes < 0) {
                return "negative resume_after_minutes";
            }
            if (!string.IsNullOrEmpty(p.ResumeAt) && !TryParseResumeAt(p.ResumeAt, DateTime.Now, out _)) {
                return "resume_at must be HH:mm or an ISO time";
            }
            return null;
        }

        public static bool TryParseResumeAt(string value, DateTime nowLocal, out DateTime atLocal) {
            atLocal = default;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto) && value.Contains("T")) {
                atLocal = dto.LocalDateTime;
                return true;
            }
            if (TimeSpan.TryParseExact(value, new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var ts)) {
                var at = nowLocal.Date + ts;
                if (at <= nowLocal) {
                    at = at.AddDays(1);
                }
                atLocal = at;
                return true;
            }
            return false;
        }

        protected override CommandResult Run(CommandEnvelope command, PausePayload p, ICommandContext context) {
            DateTime? resumeAt = null;
            if (p.ResumeAfterMinutes > 0) {
                resumeAt = context.Now.AddMinutes(p.ResumeAfterMinutes);
            } else if (!string.IsNullOrEmpty(p.ResumeAt) && TryParseResumeAt(p.ResumeAt, context.Now.ToLocalTime(), out var at)) {
                resumeAt = at.ToUniversalTime();
            }
            context.Control.Pause(p, resumeAt);
            return CommandResult.Of(command.Id, CommandStatus.Applied, resumeAt.HasValue ? $"Paused until {resumeAt.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}" : "Paused");
        }
    }

    public class ResumeHandler : ControlHandler<EmptyPayload> {
        public override string Kind => CommandKinds.Resume;
        public override CommandTiming Timing => CommandTiming.Immediate;

        protected override CommandResult Run(CommandEnvelope command, EmptyPayload p, ICommandContext context) {
            var was = context.Control.Resume();
            return CommandResult.Of(command.Id, CommandStatus.Applied, was ? null : "It was not paused.");
        }
    }

    public class AbortExposureHandler : ControlHandler<AbortExposurePayload> {
        public override string Kind => CommandKinds.AbortExposure;
        public override CommandTiming Timing => CommandTiming.Immediate;

        protected override string Check(AbortExposurePayload p) {
            if (p.PlanId <= 0) {
                return "missing plan_id";
            }
            if (p.StartedAt == null || p.StartedAt.Value == default) {
                return "missing started_at";
            }
            return null;
        }

        protected override CommandResult Run(CommandEnvelope command, AbortExposurePayload p, ICommandContext context) {
            var outcome = context.Control.AbortExposure($"exposure aborted by {command.Author}", p.PlanId, p.StartedAt.Value);
            switch (outcome) {
                case AbortOutcome.Aborted:
                    return CommandResult.Of(command.Id, CommandStatus.Applied, "Stopping the exposure and discarding the partial frame. The scheduler re-plans once the camera has stopped.");
                case AbortOutcome.AlreadyAborting:
                    return CommandResult.Of(command.Id, CommandStatus.Applied, "An abort of this exposure is already in progress.");
                case AbortOutcome.Blocked:
                    return CommandResult.Of(command.Id, CommandStatus.Rejected, context.Control.AbortBlocked ?? "This exposure cannot be aborted from the web.");
                case AbortOutcome.OtherExposure:
                    return CommandResult.Of(command.Id, CommandStatus.Applied, "That exposure already ended. The exposure running now was left alone.");
                default:
                    return CommandResult.Of(command.Id, CommandStatus.Applied, "Nothing was exposing, so nothing was aborted.");
            }
        }
    }

    public class ReplanHandler : ControlHandler<EmptyPayload> {
        public override string Kind => CommandKinds.Replan;

        protected override CommandResult Run(CommandEnvelope command, EmptyPayload p, ICommandContext context) {
            context.Control.RequestReplan($"replan by {command.Author}");
            return CommandResult.Of(command.Id, CommandStatus.Applied, null);
        }
    }
}
