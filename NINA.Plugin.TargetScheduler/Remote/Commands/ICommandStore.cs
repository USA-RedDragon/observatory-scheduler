using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class StoredCommand {
        public CommandEnvelope Command { get; set; }
        public bool Cancelled { get; set; }
        public CommandResult Result { get; set; }
    }

    public interface ICommandStore {

        void EnsureSchema();

        void SaveCommand(CommandEnvelope command);

        CommandResult GetResult(string id);

        void SaveResult(CommandResult result);

        IList<StoredCommand> Unfinished();

        IList<CommandResult> RecentResults(int limit);
    }
}
