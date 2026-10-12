using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    public class FakeControl : ISchedulerControl {
        public List<string> Replans { get; } = new List<string>();
        public List<(SkipPayload skip, DateTime until)> Skips { get; } = new List<(SkipPayload, DateTime)>();
        public List<(int? project, int? target)> Invalidated { get; } = new List<(int?, int?)>();
        public PausePayload Paused { get; private set; }
        public DateTime? PausedUntil { get; private set; }
        public DateTime? Night { get; set; }
        public CaptureKey Exposing { get; set; }
        public string AbortBlocked { get; set; }
        public List<string> Aborts { get; } = new List<string>();

        public void RequestReplan(string reason) => Replans.Add(reason);

        public void Skip(SkipPayload skip, DateTime until) => Skips.Add((skip, until));

        public bool Unskip(SkipPayload skip) => Skips.RemoveAll(s => s.skip.Scope == skip.Scope && s.skip.TargetId == skip.TargetId && s.skip.ProjectId == skip.ProjectId) > 0;

        public void Pause(PausePayload pause, DateTime? resumeAt) {
            Paused = pause;
            PausedUntil = resumeAt;
        }

        public bool Resume() {
            var was = Paused != null;
            Paused = null;
            PausedUntil = null;
            return was;
        }

        public AbortOutcome AbortExposure(string reason, int planId, DateTime startedAt) {
            if (Exposing == null) {
                return AbortBlocked != null ? AbortOutcome.Blocked : AbortOutcome.NotCapturing;
            }
            if (!Exposing.Matches(planId, startedAt)) {
                return AbortOutcome.OtherExposure;
            }
            Exposing = null;
            Aborts.Add(reason);
            return AbortOutcome.Aborted;
        }

        public DateTime? NightEnd(DateTime now) => Night;

        public void Invalidate(int? projectId, int? targetId) => Invalidated.Add((projectId, targetId));
    }

    public class FakeContext : ICommandContext {
        public DateTime Now { get; set; } = new DateTime(2026, 10, 10, 6, 52, 0, DateTimeKind.Utc);
        public string ProfileId { get; set; } = "p";
        public IReadOnlyCollection<string> ProfileFilters { get; set; } = new List<string>();
        public IReadOnlyDictionary<string, double> DefaultRuleWeights { get; set; } = new Dictionary<string, double> {
            ["Meridian Flip Penalty"] = 0,
            ["Meridian Window Priority"] = 75,
            ["Mosaic Completion"] = 0,
            ["Percent Complete"] = 50,
            ["Project Priority"] = 50,
            ["Setting Soonest"] = 50,
            ["Smart Exposure Order"] = 0,
            ["Target Switch Penalty"] = 67,
        };
        public ISchedulerDatabase Database { get; set; }
        public ISchedulerControl Control => FakeControlInstance;
        public FakeControl FakeControlInstance { get; } = new FakeControl();
    }

    public static class Cmd {
        private static int n;

        public static CommandEnvelope Of(string kind, string payload, string id = null) {
            using var doc = JsonDocument.Parse(payload ?? "{}");
            return new CommandEnvelope {
                Id = id ?? $"cmd-{++n}",
                Kind = kind,
                Payload = doc.RootElement.Clone(),
                Author = "web",
                CreatedAt = new DateTime(2026, 10, 10, 6, 50, 0, DateTimeKind.Utc),
            };
        }
    }
}
