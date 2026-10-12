using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class CommandRegistry {
        private readonly Dictionary<string, ICommandHandler> handlers = new Dictionary<string, ICommandHandler>();

        public CommandRegistry(IEnumerable<ICommandHandler> handlers = null) {
            foreach (var h in handlers ?? Enumerable.Empty<ICommandHandler>()) {
                Register(h);
            }
        }

        public void Register(ICommandHandler handler) {
            if (handlers.ContainsKey(handler.Kind)) {
                throw new InvalidOperationException($"command kind {handler.Kind} registered twice");
            }
            handlers[handler.Kind] = handler;
        }

        public ICommandHandler Get(string kind) {
            return kind != null && handlers.TryGetValue(kind, out var h) ? h : null;
        }

        public IEnumerable<string> Kinds => handlers.Keys.OrderBy(k => k);

        public static CommandRegistry Builtin() {
            return new CommandRegistry(new ICommandHandler[] {
                EditHandler.Project(),
                EditHandler.Target(),
                EditHandler.ExposurePlan(),
                new SkipHandler(),
                new UnskipHandler(),
                new PauseHandler(),
                new ResumeHandler(),
                new ReplanHandler(),
                new AbortExposureHandler(),
                EditHandler.ExposureTemplate(),
                BatchEditHandler.ExposureTemplates(),
                BatchEditHandler.Projects(),
                BatchEditHandler.ExposurePlans(),
                new GoalEditHandler(),
                new RuleWeightEditHandler(),
                new TemplateCloneHandler(),
                new TemplateDeleteHandler(),
                new ExposureSetHandler(false),
                new ExposureSetHandler(true),
                new ProjectCreateHandler(false),
                new ProjectCreateHandler(true),
                new TargetBodyHandler(),
                new TargetWindowHandler(),
            });
        }
    }
}
