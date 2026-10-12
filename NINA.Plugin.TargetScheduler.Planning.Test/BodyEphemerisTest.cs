using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BodyEphemerisTest {
        private static readonly DateTime Start = DateTime.Parse(HorizonsFixtures.Start, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        private static List<BodyPositionRow> Parse(string csv, string designation = "C/2025 R2") {
            return csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => {
                double[] v = line.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                return new BodyPositionRow { TargetGuid = "g", Designation = designation, At = Start.AddMinutes(v[0]), Ra = v[1], Dec = v[2], RaRate = v[3], DecRate = v[4] };
            }).ToList();
        }

        private static double SeparationArcsec(double ra1, double dec1, double ra2, double dec2) {
            double r = Math.PI / 180;
            double sd = Math.Sin((dec2 - dec1) * r / 2);
            double sa = Math.Sin((ra2 - ra1) * r / 2);
            double a = sd * sd + Math.Cos(dec1 * r) * Math.Cos(dec2 * r) * sa * sa;
            return 2 * Math.Asin(Math.Min(1, Math.Sqrt(a))) / r * 3600;
        }

        private static (double max, double rms, int n) Errors(string input, string truth) {
            BodyEphemeris eph = new BodyEphemeris(Parse(input));
            List<double> errors = new List<double>();
            foreach (BodyPositionRow t in Parse(truth)) {
                BodyPosition? p = eph.At(t.At);
                p.Should().NotBeNull($"{t.At:O} is inside the table");
                errors.Add(SeparationArcsec(p.Value.Ra, p.Value.Dec, t.Ra, t.Dec));
            }
            return (errors.Max(), Math.Sqrt(errors.Average(e => e * e)), errors.Count);
        }

        [Test]
        public void CometAtTenMinuteStepsMatchesTheOneMinuteTable() {
            var (max, rms, n) = Errors(HorizonsFixtures.Comet10, HorizonsFixtures.CometTruth);
            TestContext.Out.WriteLine($"C/2025 R2, 10-minute rows vs {n} one-minute truth rows: max {max:0.0000}\", rms {rms:0.0000}\"");
            n.Should().BeGreaterThan(100);
            max.Should().BeLessThan(0.01);
        }

        [Test]
        public void MoonAtTenMinuteStepsMatchesTheOneMinuteTable() {
            var (max, rms, n) = Errors(HorizonsFixtures.Moon10, HorizonsFixtures.MoonTruth);
            TestContext.Out.WriteLine($"Moon, 10-minute rows vs {n} one-minute truth rows: max {max:0.0000}\", rms {rms:0.0000}\"");
            n.Should().BeGreaterThan(100);
            max.Should().BeLessThan(0.25);
        }

        [Test]
        public void RowTimesReturnTheRowExactly() {
            List<BodyPositionRow> rows = Parse(HorizonsFixtures.Comet10);
            BodyEphemeris eph = new BodyEphemeris(rows);
            foreach (BodyPositionRow r in rows) {
                BodyPosition p = eph.At(r.At).Value;
                p.Ra.Should().Be(r.Ra);
                p.Dec.Should().Be(r.Dec);
            }
        }

        [Test]
        public void RaUnwrapsAcrossZero() {
            BodyEphemeris eph = new BodyEphemeris(new[] {
                new BodyPositionRow { At = Start, Ra = 359.9, Dec = 10, RaRate = 0.2 * 3600 * Math.Cos(10 * Math.PI / 180) / (10.0 / 60), DecRate = 0 },
                new BodyPositionRow { At = Start.AddMinutes(10), Ra = 0.1, Dec = 10, RaRate = 0.2 * 3600 * Math.Cos(10 * Math.PI / 180) / (10.0 / 60), DecRate = 0 },
            });
            BodyPosition mid = eph.At(Start.AddMinutes(5)).Value;
            mid.Ra.Should().BeApproximately(0, 1e-9);
            mid.Dec.Should().Be(10);
            BodyPosition early = eph.At(Start.AddMinutes(2.5)).Value;
            early.Ra.Should().BeApproximately(359.95, 1e-9);
            early.Ra.Should().BeLessThan(360);

            BodyEphemeris back = new BodyEphemeris(new[] {
                new BodyPositionRow { At = Start, Ra = 0.1, Dec = 0, RaRate = -0.2 * 3600 / (10.0 / 60), DecRate = 0 },
                new BodyPositionRow { At = Start.AddMinutes(10), Ra = 359.9, Dec = 0, RaRate = -0.2 * 3600 / (10.0 / 60), DecRate = 0 },
            });
            back.At(Start.AddMinutes(7.5)).Value.Ra.Should().BeApproximately(359.95, 1e-9);
        }

        [Test]
        public void OutsideTheTableIsUnknown() {
            BodyEphemeris eph = new BodyEphemeris(Parse(HorizonsFixtures.Comet10));
            eph.At(Start.AddSeconds(-1)).Should().BeNull();
            eph.At(Start.AddHours(12).AddSeconds(1)).Should().BeNull();
            eph.At(Start.AddHours(12)).Should().NotBeNull();
            eph.RateAt(Start.AddHours(13)).Should().BeNull();
            new BodyEphemeris(null).At(Start).Should().BeNull();
            new BodyEphemeris(null).Covers(Start).Should().BeFalse();
        }

        [Test]
        public void GapsWiderThanTwiceTheLocalSpacingAreUnknown() {
            List<BodyPositionRow> rows = Parse(HorizonsFixtures.Comet10);
            rows.RemoveAll(r => r.At > Start.AddHours(3) && r.At < Start.AddHours(4));
            BodyEphemeris eph = new BodyEphemeris(rows);
            eph.At(Start.AddHours(3.5)).Should().BeNull();
            eph.At(Start.AddHours(3)).Should().NotBeNull();
            eph.At(Start.AddHours(4)).Should().NotBeNull();
            eph.At(Start.AddHours(2.9)).Should().NotBeNull();

            rows = Parse(HorizonsFixtures.Comet10);
            rows.RemoveAll(r => r.At == Start.AddMinutes(60));
            new BodyEphemeris(rows).At(Start.AddMinutes(55)).Should().NotBeNull("one missing row doubles the step, which is not more than twice the spacing");
        }

        [Test]
        public void TwoRowTablesUseTheNominalStep() {
            BodyPositionRow Row(double minutes) => new BodyPositionRow { At = Start.AddMinutes(minutes), Ra = 10, Dec = 0, RaRate = 0, DecRate = 0 };
            new BodyEphemeris(new[] { Row(0), Row(20) }).At(Start.AddMinutes(10)).Should().NotBeNull();
            new BodyEphemeris(new[] { Row(0), Row(21) }).At(Start.AddMinutes(10)).Should().BeNull();
            new BodyEphemeris(new[] { Row(0), Row(21) }).At(Start).Should().NotBeNull();
        }

        [Test]
        public void StepChangesAreNotGaps() {
            List<BodyPositionRow> rows = Parse(HorizonsFixtures.Comet10).Where(r => r.At <= Start.AddHours(2)).ToList();
            List<BodyPositionRow> fine = Parse(HorizonsFixtures.CometTruth).Where(r => r.At > Start.AddHours(2) && r.At < Start.AddHours(3)).ToList();
            BodyEphemeris eph = new BodyEphemeris(rows.Concat(fine));
            eph.At(Start.AddHours(2).AddMinutes(1)).Should().NotBeNull();
        }

        [Test]
        public void LocalTimesAreConvertedToUtc() {
            BodyEphemeris eph = new BodyEphemeris(Parse(HorizonsFixtures.Comet10));
            DateTime utc = Start.AddMinutes(95);
            eph.At(utc.ToLocalTime()).Value.Ra.Should().Be(eph.At(utc).Value.Ra);
        }

        [Test]
        public void RatesInterpolateAndTakeTheWindowMaximum() {
            BodyEphemeris eph = new BodyEphemeris(new[] {
                new BodyPositionRow { At = Start, Ra = 10, Dec = 0, RaRate = 30, DecRate = 40 },
                new BodyPositionRow { At = Start.AddMinutes(10), Ra = 10, Dec = 0, RaRate = 60, DecRate = 80 },
                new BodyPositionRow { At = Start.AddMinutes(20), Ra = 10, Dec = 0, RaRate = 0, DecRate = 10 },
            });
            eph.RateAt(Start).Should().Be(50);
            eph.RateAt(Start.AddMinutes(5)).Should().Be(75);
            eph.MaxRate(Start, Start.AddMinutes(5)).Should().Be(75);
            eph.MaxRate(Start.AddMinutes(5), Start.AddMinutes(15)).Should().Be(100);
            eph.MaxRate().Should().Be(100);
            eph.MaxRate(Start.AddHours(1), Start.AddHours(2)).Should().BeNull();
        }

        [Test]
        public void TablesKeepOnlyTheCurrentDesignation() {
            List<BodyPositionRow> rows = Parse(HorizonsFixtures.Comet10);
            rows.AddRange(Parse(HorizonsFixtures.Moon10, "301"));
            BodyPositionTable table = new BodyPositionTable(rows.Select(r => { r.TargetGuid = "g"; return r; }));
            table.Ephemeris("g", "c/2025 r2").Count.Should().Be(73);
            table.Ephemeris("G", "301").At(Start).Value.Ra.Should().BeApproximately(222.049164998, 1e-9);
            table.Ephemeris("g", "C/2024 G3").Count.Should().Be(0);
            table.Ephemeris("g", null).Should().BeNull();
        }
    }
}
