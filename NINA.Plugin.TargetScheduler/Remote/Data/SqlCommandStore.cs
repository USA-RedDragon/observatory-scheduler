using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlCommandStore : ICommandStore {
        private readonly Func<DbConnection> connect;

        public SqlCommandStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public void EnsureSchema() {
            using var conn = connect();
            Exec(conn, QueueSchema.CreateCommandTable);
            Exec(conn, QueueSchema.CreateResultTable);
            GoalSchema.Ensure(conn);
        }

        public void SaveCommand(CommandEnvelope command) {
            using var conn = connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO ts_command (id, kind, payload, author, undo_of, created_at, cancelled) VALUES (@id, @kind, @payload, @author, @undo, @created, 0)";
            Add(cmd, "@id", command.Id);
            Add(cmd, "@kind", command.Kind);
            Add(cmd, "@payload", command.Payload.ValueKind == JsonValueKind.Undefined ? "{}" : command.Payload.GetRawText());
            Add(cmd, "@author", command.Author ?? "");
            Add(cmd, "@undo", (object)command.UndoOf ?? DBNull.Value);
            Add(cmd, "@created", Timestamp(command.CreatedAt == default ? DateTime.UtcNow : command.CreatedAt));
            cmd.ExecuteNonQuery();
        }

        public CommandResult GetResult(string id) {
            using var conn = connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT command_id, status, message, detail, updated_at FROM ts_command_result WHERE command_id = @id";
            Add(cmd, "@id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadResult(r) : null;
        }

        public void SaveResult(CommandResult result) {
            using var conn = connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO ts_command_result (command_id, status, message, detail, updated_at) VALUES (@id, @status, @message, @detail, @updated) " +
                "ON CONFLICT(command_id) DO UPDATE SET status = excluded.status, message = excluded.message, detail = excluded.detail, updated_at = excluded.updated_at";
            Add(cmd, "@id", result.Id);
            Add(cmd, "@status", result.Status);
            Add(cmd, "@message", (object)result.Message ?? DBNull.Value);
            Add(cmd, "@detail", result.Detail == null ? DBNull.Value : JsonSerializer.Serialize(result.Detail, CommandJson.Options));
            Add(cmd, "@updated", Timestamp(result.UpdatedAt == default ? DateTime.UtcNow : result.UpdatedAt));
            cmd.ExecuteNonQuery();
        }

        public IList<StoredCommand> Unfinished() {
            var list = new List<StoredCommand>();
            using var conn = connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT c.id, c.kind, c.payload, c.author, c.undo_of, c.created_at, c.cancelled, r.status " +
                "FROM ts_command c LEFT JOIN ts_command_result r ON r.command_id = c.id " +
                "WHERE r.command_id IS NULL OR r.status IN ('queued', 'pending') ORDER BY c.created_at, c.id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                var env = new CommandEnvelope {
                    Id = r.GetString(0),
                    Kind = r.GetString(1),
                    Payload = ParseJson(r.IsDBNull(2) ? "{}" : r.GetString(2)),
                    Author = r.IsDBNull(3) ? "" : r.GetString(3),
                    UndoOf = r.IsDBNull(4) ? null : r.GetString(4),
                    CreatedAt = ReadTime(r.GetValue(5)),
                };
                list.Add(new StoredCommand { Command = env, Cancelled = !r.IsDBNull(6) && Convert.ToInt64(r.GetValue(6), CultureInfo.InvariantCulture) != 0 });
            }
            return list;
        }

        public IList<CommandResult> RecentResults(int limit) {
            var list = new List<CommandResult>();
            using var conn = connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT command_id, status, message, detail, updated_at FROM ts_command_result ORDER BY updated_at DESC LIMIT @n";
            Add(cmd, "@n", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) {
                list.Add(ReadResult(r));
            }
            return list;
        }

        private static CommandResult ReadResult(DbDataReader r) {
            return new CommandResult {
                Id = r.GetString(0),
                Status = r.GetString(1),
                Message = r.IsDBNull(2) ? null : r.GetString(2),
                Detail = r.IsDBNull(3) ? null : ParseJson(r.GetString(3)),
                UpdatedAt = ReadTime(r.GetValue(4)),
            };
        }

        private static JsonElement ParseJson(string s) {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s);
            return doc.RootElement.Clone();
        }

        public static string Timestamp(DateTime t) {
            return t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        public static DateTime ReadTime(object v) {
            switch (v) {
                case DateTime dt:
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed):
                    return parsed;
                case long l:
                    return DateTimeOffset.FromUnixTimeSeconds(l).UtcDateTime;
                default:
                    return DateTime.UtcNow;
            }
        }

        private static void Exec(DbConnection conn, string sql) {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public static void Add(DbCommand cmd, string name, object value) {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}
