using System;
using System.Data;
using System.Data.Common;
using static NINA.Plugin.TargetScheduler.Darks.SqliteRows;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>The cooler setpoint decisions, one row per move, for the stacker and the morning report.</summary>
    public class CoolerSetpointStore {
        public const string Table = "ts_cooler_setpoint";

        public const string CreateTable = @"CREATE TABLE IF NOT EXISTS ts_cooler_setpoint (
    id TEXT NOT NULL PRIMARY KEY,
    decided_at TIMESTAMP NOT NULL,
    night TEXT NOT NULL,
    action TEXT NOT NULL,
    outcome TEXT NOT NULL,
    from_setpoint REAL,
    setpoint REAL,
    power REAL,
    ambient REAL,
    predicted_power REAL,
    target_power REAL NOT NULL,
    low_power REAL NOT NULL,
    high_power REAL NOT NULL,
    max_predicted_power REAL NOT NULL,
    fit_intercept REAL,
    fit_slope REAL,
    fit_samples INTEGER,
    reason TEXT NOT NULL
)";

        private readonly Func<DbConnection> connect;

        public CoolerSetpointStore(Func<DbConnection> connect) {
            this.connect = connect;
        }

        public void Ensure() {
            using DbConnection c = connect();
            SqliteRows.Ensure(c, CreateTable);
        }

        public void Record(CoolerDecision d, string night, string outcome, CoolerSettingsData settings, DateTime atUtc) {
            using DbConnection c = connect();
            SqliteRows.Ensure(c, CreateTable);
            if (c.State != ConnectionState.Open) { c.Open(); }
            using DbCommand cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO ts_cooler_setpoint (id, decided_at, night, action, outcome, from_setpoint, setpoint, power, ambient, predicted_power, target_power, low_power, high_power, max_predicted_power, fit_intercept, fit_slope, fit_samples, reason)
VALUES (@id, @at, @night, @action, @outcome, @from, @to, @power, @ambient, @predicted, @target, @low, @high, @max, @a, @b, @n, @reason)";
            Add(cmd, "@id", Guid.NewGuid().ToString());
            Add(cmd, "@at", Format(atUtc));
            Add(cmd, "@night", night);
            Add(cmd, "@action", d.Name);
            Add(cmd, "@outcome", outcome);
            Add(cmd, "@from", d.From);
            Add(cmd, "@to", d.Target);
            Add(cmd, "@power", d.Power);
            Add(cmd, "@ambient", d.Ambient);
            Add(cmd, "@predicted", d.Predicted);
            Add(cmd, "@target", settings.TargetPower);
            Add(cmd, "@low", settings.LowPower);
            Add(cmd, "@high", settings.HighPower);
            Add(cmd, "@max", settings.MaxPredictedPower);
            Add(cmd, "@a", d.Fit?.Intercept);
            Add(cmd, "@b", d.Fit?.Slope);
            Add(cmd, "@n", d.Fit?.Samples);
            Add(cmd, "@reason", d.Reason ?? "");
            cmd.ExecuteNonQuery();
        }
    }
}
