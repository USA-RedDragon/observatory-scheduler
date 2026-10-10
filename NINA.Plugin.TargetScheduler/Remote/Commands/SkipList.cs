using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public class SkipEntry {

        [JsonPropertyName("scope")]
        public string Scope { get; set; }

        [JsonPropertyName("target_id")]
        public int TargetId { get; set; }

        [JsonPropertyName("project_id")]
        public int ProjectId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("until")]
        public DateTime Until { get; set; }
    }

    public class SkipList {
        private readonly object sync = new object();
        private List<SkipEntry> entries = new List<SkipEntry>();

        public void Add(SkipPayload p, DateTime until) {
            lock (sync) {
                entries.RemoveAll(e => Same(e, p));
                entries.Add(new SkipEntry {
                    Scope = p.Scope,
                    TargetId = p.Scope == "target" ? p.TargetId : 0,
                    ProjectId = p.ProjectId,
                    Name = p.Scope == "project" ? p.ProjectName : p.TargetName,
                    Until = until.ToUniversalTime(),
                });
            }
        }

        public bool Remove(SkipPayload p) {
            lock (sync) {
                return entries.RemoveAll(e => Same(e, p)) > 0;
            }
        }

        private static bool Same(SkipEntry e, SkipPayload p) {
            if (e.Scope != p.Scope) {
                return false;
            }
            return p.Scope == "project" ? e.ProjectId == p.ProjectId : e.TargetId == p.TargetId;
        }

        public SkipEntry Match(int projectId, int targetId, DateTime now) {
            var t = now.ToUniversalTime();
            lock (sync) {
                return entries.FirstOrDefault(e => e.Until > t &&
                    ((e.Scope == "target" && e.TargetId == targetId) || (e.Scope == "project" && e.ProjectId == projectId)));
            }
        }

        public IList<SkipEntry> Active(DateTime now) {
            var t = now.ToUniversalTime();
            lock (sync) {
                entries.RemoveAll(e => e.Until <= t);
                return entries.Select(e => new SkipEntry { Scope = e.Scope, TargetId = e.TargetId, ProjectId = e.ProjectId, Name = e.Name, Until = e.Until }).ToList();
            }
        }

        public string Serialize(DateTime now) {
            return JsonSerializer.Serialize(Active(now), CommandJson.Options);
        }

        public void Load(string json, DateTime now) {
            if (string.IsNullOrWhiteSpace(json)) {
                return;
            }
            var list = JsonSerializer.Deserialize<List<SkipEntry>>(json, CommandJson.Options) ?? new List<SkipEntry>();
            var t = now.ToUniversalTime();
            lock (sync) {
                entries = list.Where(e => e.Until > t).Select(e => { e.Until = DateTime.SpecifyKind(e.Until, DateTimeKind.Utc); return e; }).ToList();
            }
        }
    }
}
