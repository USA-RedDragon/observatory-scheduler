using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Diagnostics;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    [Explicit]
    [Category("Benchmark")]
    public class SeasonScanBenchmark {
        private const double D2R = Math.PI / 180;

        private class ManagedSky : INightUsability {
            private readonly double lat, lon, ra, dec, minAlt;
            private readonly DateTime first;
            public long Samples;

            public ManagedSky(double lat, double lon, double raHours, double dec, double minAlt, DateTime first) {
                this.lat = lat;
                this.lon = lon;
                ra = raHours * 15;
                this.dec = dec;
                this.minAlt = minAlt;
                this.first = first;
            }

            private static double Gmst(DateTime utc) {
                double d = utc.ToOADate() + 2415018.5 - 2451545.0;
                return (280.46061837 + 360.98564736629 * d) % 360;
            }

            private static (double ra, double dec) Sun(DateTime utc) {
                double n = utc.ToOADate() + 2415018.5 - 2451545.0;
                double l = (280.46 + 0.9856474 * n) % 360, g = (357.528 + 0.9856003 * n) % 360;
                double lam = (l + 1.915 * Math.Sin(g * D2R) + 0.02 * Math.Sin(2 * g * D2R)) * D2R;
                double eps = 23.439 * D2R;
                return (Math.Atan2(Math.Cos(eps) * Math.Sin(lam), Math.Cos(lam)) / D2R, Math.Asin(Math.Sin(eps) * Math.Sin(lam)) / D2R);
            }

            private double Alt(double raDeg, double decDeg, DateTime utc) {
                double ha = (Gmst(utc) + lon - raDeg) * D2R;
                return Math.Asin(Math.Sin(decDeg * D2R) * Math.Sin(lat * D2R) + Math.Cos(decDeg * D2R) * Math.Cos(lat * D2R) * Math.Cos(ha)) / D2R;
            }

            private (DateTime start, DateTime end)? Dark(int night) {
                DateTime noon = first.AddDays(night).AddHours(12 - lon / 15);
                DateTime? s = null, e = null;
                for (int m = 0; m < 24 * 60; m += 5) {
                    DateTime t = noon.AddMinutes(m);
                    var sun = Sun(t);
                    bool dark = Alt(sun.ra, sun.dec, t) < -18;
                    if (dark && s == null) { s = t; }
                    if (!dark && s != null && e == null) { e = t; }
                }
                if (s == null) { return null; }
                return (s.Value, e ?? noon.AddDays(1));
            }

            public double UsableMinutes(int night) {
                int anchor = night - night % 7;
                var span = Dark(anchor);
                if (span == null) { return 0; }
                DateTime start = span.Value.start.AddDays(night - anchor), end = span.Value.end.AddDays(night - anchor);
                double longest = 0;
                DateTime? run = null;
                for (DateTime t = start; ; t = t.AddMinutes(10)) {
                    bool last = t >= end;
                    DateTime at = last ? end : t;
                    Samples++;
                    bool visible = Alt(ra, dec, at) > minAlt;
                    if (visible && run == null) { run = at; } else if (!visible && run != null) { longest = Math.Max(longest, (at - run.Value).TotalMinutes); run = null; }
                    if (last) { break; }
                }
                if (run != null) { longest = Math.Max(longest, (end - run.Value).TotalMinutes); }
                return longest;
            }
        }

        [Test]
        public void SixtyTargetSeasonScan() {
            DateTime night = new DateTime(2026, 10, 9);
            Random rnd = new Random(42);
            Stopwatch total = Stopwatch.StartNew();
            long samples = 0, worst = 0;
            int nights = 0;
            for (int i = 0; i < 60; i++) {
                double raHours = rnd.NextDouble() * 24;
                double dec = -30 + rnd.NextDouble() * 100;
                Stopwatch one = Stopwatch.StartNew();
                ManagedSky sky = new ManagedSky(33.0, -97.0, raHours, dec, 20, night);
                SeasonResult r = SeasonCounter.Count(sky, 60);
                worst = Math.Max(worst, one.ElapsedTicks);
                samples += sky.Samples;
                nights += r.NightsScanned;
            }
            total.Stop();
            TestContext.WriteLine($"60 targets: {total.Elapsed.TotalMilliseconds:F1}ms total, {total.Elapsed.TotalMilliseconds / 60:F2}ms per target, worst {worst * 1000.0 / Stopwatch.Frequency:F2}ms, {nights} nights scanned, {samples} target samples");
        }
    }
}
