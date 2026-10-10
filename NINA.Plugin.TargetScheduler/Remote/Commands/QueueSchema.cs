namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public static class QueueSchema {

        public const string CreateCommandTable = @"CREATE TABLE IF NOT EXISTS ts_command (
    id TEXT NOT NULL PRIMARY KEY,
    kind TEXT NOT NULL,
    payload TEXT NOT NULL,
    author TEXT NOT NULL,
    undo_of TEXT,
    created_at TIMESTAMP NOT NULL,
    cancelled INTEGER NOT NULL DEFAULT 0
)";

        public const string CreateResultTable = @"CREATE TABLE IF NOT EXISTS ts_command_result (
    command_id TEXT NOT NULL PRIMARY KEY,
    status TEXT NOT NULL,
    message TEXT,
    detail TEXT,
    updated_at TIMESTAMP NOT NULL
)";
    }
}
