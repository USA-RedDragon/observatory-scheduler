using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Text.RegularExpressions;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class ClosedRoofDarksTest {
        private static readonly DateTime Nov8 = new DateTime(2026, 11, 9, 1, 24, 46, DateTimeKind.Utc);

        private static int Temperatures(string reason) {
            return Regex.Matches(reason, @"-?\d+(\.\d+)? °C").Count;
        }

        [Test]
        public void TheNov8NightPredictsMinus15AndMinus16UnreachableAtEightyPercentWithOneHeldValue() {
            DarkAir air = DarkAir.Choose(true, 18.1, true, 15.2);
            air.Inside.Should().BeTrue();
            air.Value.Should().Be(18.1);

            CoolerFit fit = CoolerFit.RefitClosed(null, air.Inside);
            double? coldest = CoolerFit.ColdestSetpoint(air.Value, fit);
            coldest.Should().BeApproximately(-12.45, 0.05);

            foreach (double set in new[] { -15.0, -16.0 }) {
                string reason = DarkPlanner.PowerLimitReason(set, air.Value, coldest, air.Label);
                reason.Should().NotBeNull($"{set} °C is below what the cooler holds at 80% power");
                reason.Should().Be($"the cooler can't hold {set} °C at 18.1 °C inside air (at 80% cooler power it holds about -12.4 °C)");
                reason.Should().NotContain("reached");
                Temperatures(reason).Should().Be(3, "the set, the air and the one held value");
            }
            DarkPlanner.PowerLimitReason(-12, air.Value, coldest, air.Label).Should().BeNull();
        }

        [Test]
        public void TheNov8HoldAtFullPowerKeepsBothSetsUnreachableAfterTheRefit() {
            var trip = new CoolerTrip { At = Nov8, SetTemp = -15, Ambient = 18.1, Sensor = -14, Power = 100 };
            CoolerFit fit = CoolerFit.RefitClosed(new[] { trip.Sample() }, true);
            double? coldest = CoolerFit.ColdestSetpoint(18.1, fit);
            coldest.Should().BeGreaterThan(-12, "100% power held only -14 °C, 32.1 °C below the inside air");
            DarkPlanner.PowerLimitReason(-15, 18.1, coldest, "inside").Should().NotBeNull();
            DarkPlanner.PowerLimitReason(-16, 18.1, coldest, "inside").Should().NotBeNull();
            fit.Slope.Should().Be(CoolerFit.ClosedInsideSlope);
        }

        [Test]
        public void TheOldOpenRoofFitAgainstTheOutsideAirCalledMinus15Reachable() {
            double? old = CoolerFit.ColdestSetpoint(15.2, CoolerFit.Prior());
            old.Should().BeApproximately(-15.95, 0.05);
            DarkPlanner.PowerLimitReason(-15, 15.2, old).Should().BeNull("this is the prediction that kept the cooler at 100% all night");
        }

        [Test]
        public void WithoutTheFocuserTheRoofClosedFitAgainstTheOutsideAirStillRulesThemOut() {
            DarkAir air = DarkAir.Choose(false, double.NaN, true, 15.2);
            air.Inside.Should().BeFalse();
            air.Value.Should().Be(15.2);
            air.Note.Should().Contain("the focuser is not connected");
            double? coldest = CoolerFit.ColdestSetpoint(air.Value, CoolerRuntimeFree.Fit(air));
            coldest.Should().BeApproximately(-12.9, 0.1);
            DarkPlanner.PowerLimitReason(-15, air.Value, coldest, air.Label).Should().Contain("at 15.2 °C outside air");
        }

        [TestCase(true, 85.0, 15.2, false, "outside {1} to {2}")]
        [TestCase(true, 2.0, 15.2, false, "beyond")]
        [TestCase(true, 18.1, double.NaN, true, null)]
        [TestCase(false, 18.1, double.NaN, false, "no weather station")]
        public void TheFocuserCountsOnlyWhenItIsSane(bool connected, double focuser, double weather, bool inside, string note) {
            DarkAir air = DarkAir.Choose(connected, focuser, !double.IsNaN(weather), weather);
            air.Inside.Should().Be(inside);
            if (note == "outside {1} to {2}") {
                air.Note.Should().Contain("outside -30 to 50 °C");
            } else if (note != null) {
                air.Note.Should().Contain(note);
            }
        }

        [Test]
        public void ATripRecordsTheSensorAtTheTripNotTheDipOnTheWayDown() {
            var g = new CoolerGuard(-16, 18.1);
            DateTime t0 = Nov8.AddMinutes(-5);
            CoolerTrip trip = null;
            for (int s = 0; s <= 600 && trip == null; s += 2) {
                double sensor = s < 60 ? -15.3 : -15.2;
                trip = g.Observe(t0.AddSeconds(s), sensor, 100, -16);
            }
            trip.Should().NotBeNull();
            trip.Sensor.Should().Be(-15.2);
            trip.Reason.Should().NotContain("reached");
        }

        private static class CoolerRuntimeFree {
            public static CoolerFit Fit(DarkAir air) => CoolerFit.RefitClosed(null, air.Inside);
        }
    }
}
