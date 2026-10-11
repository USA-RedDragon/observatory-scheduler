using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Flats {

    public class FlatRequest {
        public string RequestId { get; set; }
        public int TargetId { get; set; }
        public string TargetName { get; set; }
        public string Night { get; set; }
        public string Filters { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? ComputedAt { get; set; }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "request {0}: {1} (target {2}), night of {3}{4}",
                RequestId, TargetName, TargetId, Night, string.IsNullOrWhiteSpace(Filters) ? "" : $", filters {Filters}");
        }
    }

    public class FlatRequestTaken {
        public string RequestId { get; set; }
        public DateTime TakenAt { get; set; }
        public int Sets { get; set; }
        public string Note { get; set; }
    }

    public static class FlatRequestPlanner {
        public static readonly TimeSpan LedgerWindow = TimeSpan.FromHours(36);

        public static List<FlatRequest> Due(IEnumerable<FlatRequest> requests, IEnumerable<FlatRequestTaken> taken, DateTime nowUtc) {
            List<FlatRequestTaken> list = (taken ?? Enumerable.Empty<FlatRequestTaken>()).Where(t => t != null).ToList();
            return (requests ?? Enumerable.Empty<FlatRequest>())
                .Where(r => r != null && !string.IsNullOrEmpty(r.RequestId))
                .Where(r => !list.Any(t => t.RequestId == r.RequestId && nowUtc - t.TakenAt <= LedgerWindow))
                .OrderBy(r => r.RequestedAt ?? DateTime.MinValue)
                .ThenBy(r => r.RequestId, StringComparer.Ordinal)
                .ToList();
        }

        public static bool SameNight(DateTime lightSessionDate, string night) {
            return !string.IsNullOrWhiteSpace(night) &&
                lightSessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == night.Trim();
        }

        public static string NoLightsNote(FlatRequest request) {
            return string.Format(CultureInfo.InvariantCulture,
                "Target Scheduler has no lights of target {0} ({1}) on the night of {2}, so no flats were taken for this request",
                request.TargetId, request.TargetName, request.Night);
        }

        public static string FailedNote(int failed, int sessions) {
            return failed == 0 ? null : string.Format(CultureInfo.InvariantCulture,
                "{0} of {1} light sessions got no flats; NINA's log has the reason", failed, sessions);
        }
    }
}
