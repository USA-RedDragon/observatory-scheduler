using NINA.Plugin.TargetScheduler.Goals;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Data {

    public class SqlPanelHistoryReader {
        public const string Sql =
            "SELECT ai.Id, ai.targetId, ai.filtername, ai.acquireddate, CASE WHEN ai.Id > @known THEN ai.metadata END " +
            "FROM acquiredimage ai JOIN project p ON p.Id = ai.projectId " +
            "WHERE p.isMosaic = 1 AND p.state = 1 AND ai.gradingStatus <> 2";

        private static readonly ConcurrentDictionary<long, double> seconds = new ConcurrentDictionary<long, double>();
        private static long known;

        private readonly DbConnection connection;

        public SqlPanelHistoryReader(DbConnection connection) {
            this.connection = connection;
        }

        public static void Forget() {
            seconds.Clear();
            known = 0;
        }

        public PanelHistory Load() {
            lock (seconds) {
                return LoadLocked();
            }
        }

        private PanelHistory LoadLocked() {
            List<PanelExposure> rows = new List<PanelExposure>();
            List<PanelExposure> missing = new List<PanelExposure>();
            bool opened = false;
            if (connection.State != ConnectionState.Open) {
                connection.Open();
                opened = true;
            }

            try {
                if (!SqlGoalRowProvider.TableExists(connection, "acquiredimage") || !SqlGoalRowProvider.TableExists(connection, "project")) {
                    return PanelHistory.Empty;
                }

                long seen = known;
                long newest = seen;
                using (DbCommand cmd = connection.CreateCommand()) {
                    cmd.CommandText = Sql;
                    AddParameter(cmd, "@known", seen);
                    using DbDataReader r = cmd.ExecuteReader();
                    while (r.Read()) {
                        if (r.IsDBNull(0) || r.IsDBNull(1) || r.IsDBNull(2) || r.IsDBNull(3)) { continue; }
                        PanelExposure e = new PanelExposure {
                            Id = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture),
                            TargetId = Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture),
                            Filter = Convert.ToString(r.GetValue(2), CultureInfo.InvariantCulture),
                            At = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(r.GetValue(3), CultureInfo.InvariantCulture)).UtcDateTime
                        };
                        if (e.Id > newest) { newest = e.Id; }
                        if (seconds.TryGetValue(e.Id, out double length)) {
                            e.Seconds = length;
                        } else if (e.Id > seen) {
                            e.Seconds = r.IsDBNull(4) ? 0 : ExposureSeconds(Convert.ToString(r.GetValue(4), CultureInfo.InvariantCulture));
                            seconds[e.Id] = e.Seconds;
                        } else {
                            missing.Add(e);
                            continue;
                        }
                        rows.Add(e);
                    }
                }

                for (int i = 0; i < missing.Count; i += 500) {
                    List<PanelExposure> batch = missing.GetRange(i, Math.Min(500, missing.Count - i));
                    Dictionary<long, PanelExposure> byId = new Dictionary<long, PanelExposure>();
                    batch.ForEach(e => byId[e.Id] = e);
                    using DbCommand cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Id, metadata FROM acquiredimage WHERE Id IN (" + string.Join(",", byId.Keys) + ")";
                    using DbDataReader r = cmd.ExecuteReader();
                    while (r.Read()) {
                        long id = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture);
                        if (!byId.TryGetValue(id, out PanelExposure e)) { continue; }
                        e.Seconds = r.IsDBNull(1) ? 0 : ExposureSeconds(Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture));
                        seconds[id] = e.Seconds;
                        rows.Add(e);
                    }
                }

                known = newest;
            } finally {
                if (opened) { connection.Close(); }
            }

            return new PanelHistory(rows);
        }

        private static void AddParameter(DbCommand cmd, string name, object value) {
            DbParameter p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        public static double ExposureSeconds(string metadata) {
            if (string.IsNullOrWhiteSpace(metadata)) { return 0; }
            try {
                using JsonDocument doc = JsonDocument.Parse(metadata);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("ExposureDuration", out JsonElement d)
                    && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out double v) && v > 0 && !double.IsInfinity(v)) {
                    return v;
                }
            } catch (JsonException) {
            }
            return 0;
        }
    }
}
