using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public readonly struct BodyPosition {
        public double Ra { get; }
        public double Dec { get; }

        public BodyPosition(double ra, double dec) {
            Ra = ra;
            Dec = dec;
        }
    }

    public class BodyEphemeris {
        public const double GapFactor = 2;
        public static readonly TimeSpan NominalStep = TimeSpan.FromMinutes(10);

        private readonly BodyPositionRow[] rows;
        private readonly DateTime[] times;

        public BodyEphemeris(IEnumerable<BodyPositionRow> rows) {
            this.rows = (rows ?? Enumerable.Empty<BodyPositionRow>())
                .Where(r => r != null)
                .GroupBy(r => RowTime(r.At))
                .Select(g => g.First())
                .OrderBy(r => RowTime(r.At))
                .ToArray();
            times = this.rows.Select(r => RowTime(r.At)).ToArray();
            Version = this.rows.Length == 0 ? "empty"
                : $"{this.rows.Length}:{times[0].Ticks}:{times[times.Length - 1].Ticks}:{this.rows.Max(r => r.ComputedAt?.Ticks ?? 0)}";
        }

        public int Count => rows.Length;

        public string Version { get; }

        public DateTime? Start => times.Length == 0 ? (DateTime?)null : times[0];

        public DateTime? End => times.Length == 0 ? (DateTime?)null : times[times.Length - 1];

        public bool Covers(DateTime time) {
            return Bracket(Utc(time)) >= 0;
        }

        public BodyPosition? At(DateTime time) {
            DateTime t = Utc(time);
            int i = Bracket(t);
            if (i < 0) { return null; }

            BodyPositionRow a = rows[i];
            if (i == rows.Length - 1 || times[i] == t) { return new BodyPosition(Normalize(a.Ra), a.Dec); }
            BodyPositionRow b = rows[i + 1];

            double h = (times[i + 1] - times[i]).TotalHours;
            double s = (t - times[i]).TotalHours / h;
            double ra0 = a.Ra;
            double ra1 = Unwrap(ra0, b.Ra);
            double ra = Hermite(s, h, ra0, RaPerHour(a), ra1, RaPerHour(b));
            double dec = Hermite(s, h, a.Dec, a.DecRate / 3600, b.Dec, b.DecRate / 3600);
            return new BodyPosition(Normalize(ra), Math.Max(-90, Math.Min(90, dec)));
        }

        public double? RateAt(DateTime time) {
            DateTime t = Utc(time);
            int i = Bracket(t);
            if (i < 0) { return null; }
            BodyPositionRow a = rows[i];
            if (i == rows.Length - 1) { return Rate(a); }
            BodyPositionRow b = rows[i + 1];
            double s = (t - times[i]).TotalSeconds / (times[i + 1] - times[i]).TotalSeconds;
            return Rate(a) + (Rate(b) - Rate(a)) * s;
        }

        public double? MaxRate(DateTime from, DateTime to) {
            DateTime f = Utc(from);
            DateTime e = Utc(to);
            if (e < f) { (f, e) = (e, f); }
            double? max = null;
            foreach (double? r in new[] { RateAt(f), RateAt(e) }) {
                if (r.HasValue) { max = Math.Max(max ?? 0, r.Value); }
            }
            for (int i = 0; i < rows.Length; i++) {
                if (times[i] > f && times[i] < e) { max = Math.Max(max ?? 0, Rate(rows[i])); }
            }
            return max;
        }

        public double? MaxRate() {
            return rows.Length == 0 ? (double?)null : rows.Max(Rate);
        }

        public static double Rate(BodyPositionRow row) {
            return Math.Sqrt(row.RaRate * row.RaRate + row.DecRate * row.DecRate);
        }

        private int Bracket(DateTime t) {
            if (rows.Length == 0 || t < times[0] || t > times[rows.Length - 1]) { return -1; }
            int lo = 0;
            int hi = rows.Length - 1;
            while (hi - lo > 1) {
                int mid = (lo + hi) / 2;
                if (times[mid] <= t) { lo = mid; } else { hi = mid; }
            }
            if (times[hi] <= t) { lo = hi; }
            if (lo == rows.Length - 1) { return lo; }
            if (times[lo] == t) { return lo; }
            return IsGap(lo) ? -1 : lo;
        }

        private bool IsGap(int i) {
            TimeSpan span = times[i + 1] - times[i];
            TimeSpan local = TimeSpan.Zero;
            if (i > 0) { local = Max(local, times[i] - times[i - 1]); }
            if (i + 2 < rows.Length) { local = Max(local, times[i + 2] - times[i + 1]); }
            if (local == TimeSpan.Zero) { local = NominalStep; }
            return span.TotalSeconds > GapFactor * local.TotalSeconds;
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b) {
            return a > b ? a : b;
        }

        private static double RaPerHour(BodyPositionRow row) {
            double cos = Math.Cos(row.Dec * Math.PI / 180);
            if (Math.Abs(cos) < 1e-9) { return 0; }
            return row.RaRate / cos / 3600;
        }

        private static double Hermite(double s, double h, double p0, double m0, double p1, double m1) {
            double s2 = s * s;
            double s3 = s2 * s;
            return (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * h * m0 + (-2 * s3 + 3 * s2) * p1 + (s3 - s2) * h * m1;
        }

        public static double Unwrap(double reference, double ra) {
            double d = ra - reference;
            while (d > 180) { d -= 360; }
            while (d < -180) { d += 360; }
            return reference + d;
        }

        public static double Normalize(double ra) {
            double r = ra % 360;
            return r < 0 ? r + 360 : r;
        }

        public static DateTime Utc(DateTime time) {
            return time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        }

        private static DateTime RowTime(DateTime at) {
            return at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : DateTime.SpecifyKind(at, DateTimeKind.Utc);
        }
    }
}
