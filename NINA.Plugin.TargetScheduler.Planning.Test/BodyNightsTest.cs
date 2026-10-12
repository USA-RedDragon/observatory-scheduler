using FluentAssertions;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BodyNightsTest {
        private const double D2R = Math.PI / 180;
        private const double Latitude = 30;
        private const double Longitude = -110;
        private static readonly DateTime FirstNight = new DateTime(2026, 10, 12);

        private class StubSky : INightUsability {
            private readonly Func<int, double> minutes;
            public List<int> Asked { get; } = new List<int>();

            public StubSky(Func<int, double> minutes) {
                this.minutes = minutes;
            }

            public double UsableMinutes(int night) {
                Asked.Add(night);
                return minutes(night);
            }
        }

        private class TrackSky : INightUsability {
            private readonly BodyNightTrack track;
            private readonly double minAlt;

            public TrackSky(BodyNightTrack track, double minAlt) {
                this.track = track;
                this.minAlt = minAlt;
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

            private static double Alt(double raDeg, double decDeg, DateTime utc) {
                double ha = (Gmst(utc) + Longitude - raDeg) * D2R;
                return Math.Asin(Math.Sin(decDeg * D2R) * Math.Sin(Latitude * D2R) + Math.Cos(decDeg * D2R) * Math.Cos(Latitude * D2R) * Math.Cos(ha)) / D2R;
            }

            public double UsableMinutes(int night) {
                DateTime noon = DateTime.SpecifyKind(FirstNight.AddDays(night).AddHours(12 - Longitude / 15), DateTimeKind.Utc);
                double longest = 0, run = 0;
                for (int m = 0; m < 24 * 60; m += 10) {
                    DateTime t = noon.AddMinutes(m);
                    var sun = Sun(t);
                    if (Alt(sun.ra, sun.dec, t) >= -18) { run = 0; continue; }
                    BodyPosition? p = track.At(t);
                    if (p.HasValue && Alt(p.Value.Ra, p.Value.Dec, t) >= minAlt) {
                        run += 10;
                        longest = Math.Max(longest, run);
                    } else {
                        run = 0;
                    }
                }
                return longest;
            }
        }

        private static List<BodyNightRow> Recorded() {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "body-nights.csv");
            return File.ReadAllLines(path).Skip(1).Select(line => line.Split(',')).Select(f => new BodyNightRow {
                TargetGuid = f[0],
                Night = f[1],
                At = DateTime.Parse(f[2], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
                Designation = f[3],
                Ra = double.Parse(f[4], CultureInfo.InvariantCulture),
                Dec = double.Parse(f[5], CultureInfo.InvariantCulture),
                RaRate = double.Parse(f[6], CultureInfo.InvariantCulture),
                DecRate = double.Parse(f[7], CultureInfo.InvariantCulture),
                Mag = double.Parse(f[8], CultureInfo.InvariantCulture),
                MagLimit = double.Parse(f[9], CultureInfo.InvariantCulture),
                ComputedAt = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc)
            }).ToList();
        }

        private static BodyNightRow Row(string night, double? mag, double? limit, double ra = 10, DateTime? computed = null) {
            DateTime d = DateTime.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new BodyNightRow {
                TargetGuid = "AAAA-1", Night = night, At = DateTime.SpecifyKind(d.AddDays(1).AddHours(7), DateTimeKind.Utc), Designation = "161P",
                Ra = ra, Dec = 20, Mag = mag, MagLimit = limit, ComputedAt = computed ?? new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        [Test]
        public void BrightNeedsAnEstimateNoFainterThanTheLimit() {
            Row("2026-10-12", 14.9, 15).Bright.Should().BeTrue();
            Row("2026-10-12", 15, 15).Bright.Should().BeTrue();
            Row("2026-10-12", 15.01, 15).Bright.Should().BeFalse();
            Row("2026-10-12", null, 15).Bright.Should().BeFalse();
            Row("2026-10-12", null, null).Bright.Should().BeTrue();
            Row("2026-10-12", 20, null).Bright.Should().BeTrue();
        }

        [Test]
        public void TrackFindsNightsAndInterpolatesBetweenMidnights() {
            BodyNightTrack track = new BodyNightTrack(new[] { Row("2026-10-11", 9, 15, 10), Row("2026-10-12", 16, 15, 12), Row("2026-10-13", 9, 15, 14) });
            track.Count.Should().Be(3);
            track.HasNight(new DateTime(2026, 10, 12)).Should().BeTrue();
            track.HasNight(new DateTime(2026, 10, 14)).Should().BeFalse();
            track.Bright(new DateTime(2026, 10, 11)).Should().BeTrue();
            track.Bright(new DateTime(2026, 10, 12, 21, 0, 0)).Should().BeFalse();
            track.Bright(new DateTime(2026, 10, 14)).Should().BeFalse();

            BodyPosition? mid = track.At(new DateTime(2026, 10, 13, 19, 0, 0, DateTimeKind.Utc));
            mid.Should().NotBeNull();
            mid.Value.Ra.Should().BeApproximately(13, 0.01);
            track.At(new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc)).Should().BeNull();
            track.At(new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc)).Should().BeNull();
        }

        [Test]
        public void MissingNightIsAGapNotAnInterpolation() {
            BodyNightTrack track = new BodyNightTrack(new[] { Row("2026-10-10", 9, 15, 10), Row("2026-10-11", 9, 15, 11), Row("2026-10-14", 9, 15, 14), Row("2026-10-15", 9, 15, 15) });
            track.At(new DateTime(2026, 10, 13, 7, 0, 0, DateTimeKind.Utc)).Should().BeNull();
            track.At(new DateTime(2026, 10, 11, 19, 0, 0, DateTimeKind.Utc)).Should().NotBeNull();
        }

        [Test]
        public void VersionChangesWithPositionsBrightnessAndRecompute() {
            string v = new BodyNightTrack(new[] { Row("2026-10-11", 9, 15), Row("2026-10-12", 9, 15) }).Version;
            new BodyNightTrack(new[] { Row("2026-10-11", 9, 15), Row("2026-10-12", 9, 15) }).Version.Should().Be(v);
            new BodyNightTrack(new[] { Row("2026-10-11", 9, 15), Row("2026-10-12", 16, 15) }).Version.Should().NotBe(v);
            new BodyNightTrack(new[] { Row("2026-10-11", 9, 15), Row("2026-10-12", 9, 15, 10, new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc)) }).Version.Should().NotBe(v);
            new BodyNightTrack(new[] { Row("2026-10-11", 9, 15) }).Version.Should().NotBe(v);
            BodyNightTrack.Empty.Version.Should().Be("empty");
        }

        [Test]
        public void TableMatchesGuidAndDesignation() {
            BodyNightRow other = Row("2026-10-12", 9, 15);
            other.Designation = "2P";
            BodyNightTable table = new BodyNightTable(new[] { Row("2026-10-11", 9, 15), Row("2026-10-12", 9, 15), other, null, new BodyNightRow { TargetGuid = "x", Night = "2026-10-12", Designation = "1P", Ra = double.NaN } });
            table.Count.Should().Be(3);
            table.Track(" aaaa-1 ", "161p").Count.Should().Be(2);
            table.Track("AAAA-1", "2P").Count.Should().Be(1);
            table.Track("AAAA-1", "3P").Count.Should().Be(0);
            table.Track("BBBB", "161P").Should().BeSameAs(BodyNightTrack.Empty);
            table.Track("AAAA-1", null).Should().BeSameAs(BodyNightTrack.Empty);
            BodyNightTable.Empty.Count.Should().Be(0);
        }

        [Test]
        public void EventCentresAreNotBodiesWithSeasons() {
            BodyNightTrack.IsEvent("event:abc").Should().BeTrue();
            BodyNightTrack.IsEvent(" Event:abc").Should().BeTrue();
            BodyNightTrack.IsEvent("161P").Should().BeFalse();
            BodyNightTrack.IsEvent(null).Should().BeFalse();
        }

        [Test]
        public void ANewOrChangedNightTableAsksForAPass() {
            SeasonSchedule.BodyNightsChanged(null, "2|2026-10-12 20:00:00.000|2026-10-11|2026-10-12").Should().BeFalse();
            SeasonSchedule.BodyNightsChanged("absent", null).Should().BeFalse();
            SeasonSchedule.BodyNightsChanged("absent", "absent").Should().BeFalse();
            SeasonSchedule.BodyNightsChanged("absent", "0|||").Should().BeTrue();
            SeasonSchedule.BodyNightsChanged("0|||", "368|2026-10-12 20:00:00.000|2026-10-11|2027-10-13").Should().BeTrue();
            SeasonSchedule.BodyNightsChanged("368|2026-10-12 20:00:00.000|2026-10-11|2027-10-13", "368|2026-10-13 20:00:00.000|2026-10-12|2027-10-14").Should().BeTrue();
            SeasonSchedule.BodyNightsChanged("368|2026-10-12 20:00:00.000|2026-10-11|2027-10-13", "368|2026-10-12 20:00:00.000|2026-10-11|2027-10-13").Should().BeFalse();
        }

        [Test]
        public void UsabilityAsksTheSkyOnlyOnBrightNights() {
            BodyNightTrack track = new BodyNightTrack(new[] { Row("2026-10-12", 9, 15), Row("2026-10-13", 16, 15), Row("2026-10-14", 9, 15) });
            StubSky sky = new StubSky(n => 120);
            BodyNightUsability nights = new BodyNightUsability(sky, track, new DateTime(2026, 10, 12, 18, 0, 0));
            nights.UsableMinutes(0).Should().Be(120);
            nights.UsableMinutes(1).Should().Be(0);
            nights.UsableMinutes(2).Should().Be(120);
            nights.UsableMinutes(3).Should().Be(0);
            sky.Asked.Should().Equal(0, 2);
        }

        private static SeasonResult Season(List<BodyNightRow> rows, string guid, string designation) {
            BodyNightTrack track = new BodyNightTable(rows).Track(guid, designation);
            track.Count.Should().Be(368);
            return SeasonCounter.Count(new BodyNightUsability(new TrackSky(track, 30), track, FirstNight), 60);
        }

        [Test]
        public void RecordedHartleyIrasHasAClosingSeason() {
            SeasonResult s = Season(Recorded(), "1111-hartley", "161P");
            s.OutOfSeason.Should().BeFalse();
            s.NightsLeft.Should().Be(45);
            FirstNight.AddDays(s.LastUsableNight).Should().Be(new DateTime(2026, 11, 25));
            s.Capped.Should().BeFalse();
            GoalScoring.RarityScore(s).Should().BeGreaterThan(0.6);
            TargetSeasonRow.From("1111-hartley", s, FirstNight, DateTime.UtcNow).SeasonEnd.Should().Be(new DateTime(2026, 11, 25));
        }

        [Test]
        public void RecordedVestaSeasonRunsToItsConjunctionGap() {
            SeasonResult s = Season(Recorded(), "2222-vesta", "4");
            s.OutOfSeason.Should().BeFalse();
            s.NightsLeft.Should().Be(128);
            FirstNight.AddDays(s.LastUsableNight).Should().Be(new DateTime(2027, 2, 16));
            GoalScoring.RarityScore(s).Should().BeLessThan(GoalScoring.RarityScore(Season(Recorded(), "1111-hartley", "161P")));
        }

        [Test]
        public void RecordedCometThatIsNeverBrightEnoughHasNoSeason() {
            List<BodyNightRow> rows = Recorded();
            BodyNightTrack track = new BodyNightTable(rows).Track("3333-k2", "C/2017 K2");
            new TrackSky(track, 30).UsableMinutes(0).Should().BeGreaterThan(60);
            SeasonResult s = Season(rows, "3333-k2", "C/2017 K2");
            s.OutOfSeason.Should().BeTrue();
            s.NightsLeft.Should().Be(0);
            GoalScoring.RarityScore(s).Should().Be(0);
            SeasonalRunway.Score(5, s).Should().Be(0);
        }

        [Test]
        public void RunwayTreatsAShortBodySeasonAsUrgent() {
            SeasonResult hartley = Season(Recorded(), "1111-hartley", "161P");
            SeasonResult vesta = Season(Recorded(), "2222-vesta", "4");
            SeasonalRunway.Score(20, hartley).Should().BeGreaterThan(SeasonalRunway.Score(20, vesta));
        }
    }
}
