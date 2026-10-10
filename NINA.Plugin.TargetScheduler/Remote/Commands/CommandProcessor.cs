using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class PendingCommand {
        public CommandEnvelope Command { get; set; }
        public string Via { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public class CommandProcessor {
        private readonly object sync = new object();
        private readonly CommandRegistry registry;
        private readonly ICommandStore store;
        private readonly ICommandContext context;
        private readonly Func<bool> webEditingEnabled;
        private readonly Func<bool> inPlan;
        private readonly Func<DateTime?> exposureEnd;
        private readonly List<PendingCommand> pending = new List<PendingCommand>();

        public event Action<CommandResult> ResultChanged;

        public Action<string> Log { get; set; }

        public CommandProcessor(CommandRegistry registry, ICommandStore store, ICommandContext context,
                Func<bool> webEditingEnabled, Func<bool> inPlan, Func<DateTime?> exposureEnd) {
            this.registry = registry;
            this.store = store;
            this.context = context;
            this.webEditingEnabled = webEditingEnabled;
            this.inPlan = inPlan;
            this.exposureEnd = exposureEnd;
        }

        public IReadOnlyList<PendingCommand> Pending {
            get {
                lock (sync) {
                    return pending.ToList();
                }
            }
        }

        public CommandResult Get(string id) {
            lock (sync) {
                var p = pending.FirstOrDefault(x => x.Command.Id == id);
                if (p != null) {
                    return PendingResult(p.Command.Id);
                }
                return store.GetResult(id);
            }
        }

        public CommandResult Submit(CommandEnvelope command, string via) {
            if (command == null || string.IsNullOrWhiteSpace(command.Id)) {
                return CommandResult.Of(command?.Id, CommandStatus.Rejected, "missing id");
            }
            CommandResult result;
            lock (sync) {
                if (pending.Any(x => x.Command.Id == command.Id)) {
                    return PendingResult(command.Id);
                }
                var known = store.GetResult(command.Id);
                if (known != null && CommandStatus.IsFinal(known.Status)) {
                    return known;
                }
                var handler = registry.Get(command.Kind);
                if (handler == null) {
                    result = CommandResult.Of(command.Id, CommandStatus.Rejected, $"unknown command kind {command.Kind}");
                } else if (handler.Validate(command) is string error) {
                    result = CommandResult.Of(command.Id, CommandStatus.Rejected, error);
                } else if (handler.IsEdit && !webEditingEnabled()) {
                    result = CommandResult.Of(command.Id, CommandStatus.Rejected, "Web editing is off on the observatory PC.");
                } else {
                    store.SaveCommand(command);
                    if (handler.Timing == CommandTiming.Immediate || !inPlan()) {
                        result = Run(handler, command);
                    } else {
                        pending.Add(new PendingCommand { Command = command, Via = via, ReceivedAt = context.Now });
                        result = PendingResult(command.Id);
                    }
                }
                store.SaveResult(result);
            }
            Log?.Invoke($"command {command.Id} {command.Kind} via {via}: {result.Status}{(result.Message != null ? " " + result.Message : "")}");
            ResultChanged?.Invoke(result);
            return result;
        }

        public CommandResult Cancel(string id) {
            CommandResult result;
            lock (sync) {
                var p = pending.FirstOrDefault(x => x.Command.Id == id);
                if (p != null) {
                    pending.Remove(p);
                } else {
                    var known = store.GetResult(id);
                    if (known != null && CommandStatus.IsFinal(known.Status)) {
                        return known;
                    }
                }
                result = CommandResult.Of(id, CommandStatus.Cancelled, "Cancelled before it applied.");
                store.SaveResult(result);
            }
            Log?.Invoke($"command {id} cancelled");
            ResultChanged?.Invoke(result);
            return result;
        }

        public int ApplyPending() {
            List<CommandResult> results = new List<CommandResult>();
            lock (sync) {
                var ordered = pending.Select((p, i) => (p, i)).OrderBy(x => x.p.Command.CreatedAt).ThenBy(x => x.i).Select(x => x.p).ToList();
                pending.Clear();
                pending.AddRange(ordered);
                while (pending.Count > 0) {
                    var p = pending[0];
                    pending.RemoveAt(0);
                    var handler = registry.Get(p.Command.Kind);
                    var r = handler == null
                        ? CommandResult.Of(p.Command.Id, CommandStatus.Rejected, $"unknown command kind {p.Command.Kind}")
                        : Run(handler, p.Command);
                    store.SaveResult(r);
                    results.Add(r);
                }
            }
            foreach (var r in results) {
                Log?.Invoke($"command {r.Id} applied at the plan boundary: {r.Status}{(r.Message != null ? " " + r.Message : "")}");
                ResultChanged?.Invoke(r);
            }
            return results.Count;
        }

        public int Poll() {
            IList<StoredCommand> rows;
            lock (sync) {
                rows = store.Unfinished();
            }
            int n = 0;
            foreach (var row in rows) {
                if (row.Cancelled) {
                    Cancel(row.Command.Id);
                    n++;
                    continue;
                }
                bool inMemory;
                lock (sync) {
                    inMemory = pending.Any(x => x.Command.Id == row.Command.Id);
                }
                if (!inMemory) {
                    Submit(row.Command, "queue");
                    n++;
                }
            }
            return n;
        }

        private CommandResult Run(ICommandHandler handler, CommandEnvelope command) {
            try {
                var r = handler.Apply(command, context) ?? CommandResult.Of(command.Id, CommandStatus.Failed, "no result");
                r.Id = command.Id;
                r.UpdatedAt = context.Now;
                return r;
            } catch (Exception e) {
                return CommandResult.Of(command.Id, CommandStatus.Failed, e.Message);
            }
        }

        private CommandResult PendingResult(string id) {
            var r = CommandResult.Of(id, CommandStatus.Pending, "Applies when the current exposure ends.");
            r.AppliesAt = exposureEnd();
            r.UpdatedAt = context.Now;
            return r;
        }
    }
}
