using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Remote.Api;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class AutofocusStore {
        public const string Table = "ts_autofocus_run";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_autofocus_run (
    id TEXT NOT NULL PRIMARY KEY,
    profile_id TEXT,
    started_at TIMESTAMP,
    finished_at TIMESTAMP NOT NULL,
    duration_seconds REAL,
    success INTEGER NOT NULL,
    error TEXT,
    filter TEXT,
    temperature REAL,
    autofocuser TEXT,
    method TEXT,
    fitting TEXT,
    initial_position REAL,
    previous_position REAL,
    previous_value REAL,
    chosen_position REAL,
    chosen_value REAL,
    points TEXT NOT NULL,
    fit TEXT,
    target_name TEXT,
    target_guid TEXT,
    source TEXT NOT NULL,
    recorded_at TIMESTAMP NOT NULL
)";

        private static readonly JsonSerializerOptions PointJson = new JsonSerializerOptions {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new UtcDateTimeConverter() },
        };

        private readonly Func<DbConnection> connect;

        public AutofocusStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public static void Ensure(DbConnection connection) {
            SqliteRows.Ensure(connection, CreateTable);
        }

        public void Save(AutofocusRun run, DateTime recordedUtc) {
            using DbConnection c = connect();
            Ensure(c);
            if (c.State != ConnectionState.Open) { c.Open(); }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO ts_autofocus_run (id, profile_id, started_at, finished_at, duration_seconds, success, error, filter, temperature, autofocuser, method, fitting, " +
                "initial_position, previous_position, previous_value, chosen_position, chosen_value, points, fit, target_name, target_guid, source, recorded_at) VALUES " +
                "(@id, @profile, @started, @finished, @duration, @success, @error, @filter, @temp, @af, @method, @fitting, @initial, @prevpos, @prevval, @pos, @val, @points, @fit, @tname, @tguid, @source, @recorded) " +
                "ON CONFLICT(id) DO UPDATE SET profile_id = excluded.profile_id, started_at = excluded.started_at, finished_at = excluded.finished_at, duration_seconds = excluded.duration_seconds, " +
                "success = excluded.success, error = excluded.error, filter = excluded.filter, temperature = excluded.temperature, autofocuser = excluded.autofocuser, method = excluded.method, " +
                "fitting = excluded.fitting, initial_position = excluded.initial_position, previous_position = excluded.previous_position, previous_value = excluded.previous_value, " +
                "chosen_position = excluded.chosen_position, chosen_value = excluded.chosen_value, points = excluded.points, fit = excluded.fit, target_name = excluded.target_name, " +
                "target_guid = excluded.target_guid, source = excluded.source, recorded_at = excluded.recorded_at";
            Add(cmd, "@id", run.Id);
            Add(cmd, "@profile", run.ProfileId);
            Add(cmd, "@started", run.StartedAt.HasValue ? Format(run.StartedAt.Value) : null);
            Add(cmd, "@finished", Format(run.FinishedAt));
            Add(cmd, "@duration", run.DurationSeconds);
            Add(cmd, "@success", run.Success ? 1 : 0);
            Add(cmd, "@error", run.Error);
            Add(cmd, "@filter", run.Filter);
            Add(cmd, "@temp", run.Temperature);
            Add(cmd, "@af", run.Autofocuser);
            Add(cmd, "@method", run.Method);
            Add(cmd, "@fitting", run.Fitting);
            Add(cmd, "@initial", run.InitialPosition);
            Add(cmd, "@prevpos", run.PreviousPosition);
            Add(cmd, "@prevval", run.PreviousValue);
            Add(cmd, "@pos", run.ChosenPosition);
            Add(cmd, "@val", run.ChosenValue);
            Add(cmd, "@points", JsonSerializer.Serialize(run.Points ?? new List<AutofocusPoint>(), PointJson));
            Add(cmd, "@fit", run.Fit);
            Add(cmd, "@tname", run.TargetName);
            Add(cmd, "@tguid", run.TargetGuid);
            Add(cmd, "@source", run.Source ?? AutofocusReport.SourceEvent);
            Add(cmd, "@recorded", Format(recordedUtc));
            cmd.ExecuteNonQuery();
        }

        public AutofocusRun PreviousSuccess(DateTime beforeUtc) {
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, Table)) { return null; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, finished_at, chosen_position, chosen_value FROM ts_autofocus_run WHERE success = 1 AND chosen_position IS NOT NULL AND finished_at < @before ORDER BY finished_at DESC LIMIT 1";
            Add(cmd, "@before", Format(beforeUtc));
            using DbDataReader r = cmd.ExecuteReader();
            if (!r.Read()) { return null; }
            return new AutofocusRun { Id = Text(r, 0), FinishedAt = Time(r, 1) ?? default, Success = true, ChosenPosition = Real(r, 2), ChosenValue = Real(r, 3) };
        }

        public IList<AutofocusRun> Recent(int limit) {
            var rows = new List<AutofocusRun>();
            using DbConnection c = connect();
            if (c.State != ConnectionState.Open) { c.Open(); }
            if (!TableExists(c, Table)) { return rows; }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = "SELECT id, profile_id, started_at, finished_at, duration_seconds, success, error, filter, temperature, autofocuser, method, fitting, " +
                "initial_position, previous_position, previous_value, chosen_position, chosen_value, points, fit, target_name, target_guid, source " +
                "FROM ts_autofocus_run ORDER BY finished_at DESC, id DESC LIMIT @n";
            Add(cmd, "@n", limit);
            using DbDataReader r = cmd.ExecuteReader();
            while (r.Read()) {
                List<AutofocusPoint> points;
                try {
                    points = JsonSerializer.Deserialize<List<AutofocusPoint>>(Text(r, 17) ?? "[]", PointJson) ?? new List<AutofocusPoint>();
                } catch (JsonException) {
                    points = new List<AutofocusPoint>();
                }
                rows.Add(new AutofocusRun {
                    Id = Text(r, 0),
                    ProfileId = Text(r, 1),
                    StartedAt = Time(r, 2),
                    FinishedAt = Time(r, 3) ?? default,
                    DurationSeconds = Real(r, 4),
                    Success = (Real(r, 5) ?? 0) != 0,
                    Error = Text(r, 6),
                    Filter = Text(r, 7),
                    Temperature = Real(r, 8),
                    Autofocuser = Text(r, 9),
                    Method = Text(r, 10),
                    Fitting = Text(r, 11),
                    InitialPosition = Real(r, 12),
                    PreviousPosition = Real(r, 13),
                    PreviousValue = Real(r, 14),
                    ChosenPosition = Real(r, 15),
                    ChosenValue = Real(r, 16),
                    Points = points,
                    Fit = Text(r, 18),
                    TargetName = Text(r, 19),
                    TargetGuid = Text(r, 20),
                    Source = Text(r, 21),
                });
            }
            return rows;
        }
    }
}
