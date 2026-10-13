using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class DarkReadinessTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

        private static DarkNeed Need(string key, double setTemp, double exposure = 300, int priority = 1) {
            return new DarkNeed { ComboKey = key, Priority = priority, Exposure = exposure, Gain = 100, Offset = 50, SetTemp = setTemp, FramesNeeded = 3, SessionGapHours = 36 };
        }

        private static DarkReadyInput Input(params DarkNeed[] needs) {
            return new DarkReadyInput {
                Now = Now,
                SunAltitudeAt = t => -30,
                Queue = new List<DarkNeed>(needs),
                WeatherConnected = true,
                Ambient = 15,
                Holds = new List<CoolerHold>(),
                Limits = new List<CoolerLimit>(),
                SaveTime = TimeSpan.FromSeconds(3)
            };
        }

        private static CoolerHold Hold(double ambient, double setTemp) {
            return new CoolerHold { At = Now.AddDays(-1), Ambient = ambient, SetTemp = setTemp, PeakPower = 80 };
        }

        [Test]
        public void EmptyBacklogIsNotReady() {
            DarkReadyResult r = DarkReadiness.Check(Input());
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("backlog");
        }

        [Test]
        public void SunAtOrAboveTheLimitIsNotReady() {
            DarkReadyInput i = Input(Need("a", -5));
            i.SunAltitudeAt = t => -7.9;
            DarkReadiness.Check(i).Reason.Should().Contain("light-safe limit");
            i.SunAltitudeAt = null;
            DarkReadiness.Check(i).Reason.Should().Contain("not known");
        }

        [Test]
        public void TheShortestDarkMustFitBeforeDawn() {
            DarkReadyInput i = Input(Need("long", 5, exposure: 900), Need("short", 5, exposure: 300));
            i.Holds.Add(Hold(20, 5));
            i.CoolingSecondsPerC = 1;
            i.SunAltitudeAt = t => -8.5 + (t - Now).TotalMinutes * 0.1;
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("shortest needed dark (300 s");
            i.SunAltitudeAt = t => -10 + (t - Now).TotalMinutes * 0.1;
            r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Need.ComboKey.Should().Be("short");
        }

        [Test]
        public void CoolingTimeCountsTowardTheSunCheckAndAnUnmeasuredRateCountsAnHour() {
            DarkReadyInput i = Input(Need("cold", -5, exposure: 300));
            i.Holds.Add(Hold(20, -5));
            i.CoolingSecondsPerC = 1;
            i.SunAltitudeAt = t => -11.6 + (t - Now).TotalMinutes * 0.1;
            DarkReadiness.Check(i).Ready.Should().BeTrue();
            i.SunAltitudeAt = t => -10.4 + (t - Now).TotalMinutes * 0.1;
            DarkReadiness.Check(i).Ready.Should().BeFalse();
            i.SunAltitudeAt = t => -11.6 + (t - Now).TotalMinutes * 0.1;
            i.CoolingSecondsPerC = 90;
            DarkReadiness.Check(i).Ready.Should().BeTrue();
            i.CoolingSecondsPerC = 120;
            DarkReadiness.Check(i).Ready.Should().BeFalse();
            i.CoolingSecondsPerC = null;
            DarkReadiness.Check(i).Ready.Should().BeFalse();
            i.SunAltitudeAt = t => -16 + (t - Now).TotalMinutes * 0.1;
            DarkReadiness.Check(i).Ready.Should().BeTrue();
            DarkReadiness.CoolTime(15, 20, null).Should().Be(TimeSpan.Zero);
            DarkReadiness.CoolTime(15, -5, 1).Should().Be(TimeSpan.FromMinutes(20));
            DarkReadiness.CoolTime(15, -5, 90).Should().Be(TimeSpan.FromMinutes(30));
        }

        [Test]
        public void WeatherMustReportTheAmbient() {
            DarkReadyInput i = Input(Need("a", -5));
            i.WeatherConnected = false;
            DarkReadiness.Check(i).Reason.Should().Contain("weather");
            i.WeatherConnected = true;
            i.Ambient = null;
            DarkReadiness.Check(i).Reason.Should().Contain("no temperature");
        }

        [Test]
        public void AHoldAtAWarmerAmbientWithAsLargeADropIsProof() {
            DarkReadyInput i = Input(Need("a", -10));
            i.LearningUsedAt = Now.AddHours(-1);
            i.Holds.Add(Hold(20, -5));
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeFalse();
            r.Basis.Should().Contain("held -5 °C at 20 °C ambient");

            i.Holds[0] = Hold(14, -15);
            DarkReadiness.Check(i).Ready.Should().BeFalse();
            i.Holds[0] = Hold(20, 0);
            DarkReadiness.Check(i).Ready.Should().BeFalse();
        }

        [Test]
        public void ProvenNeedsWinOverUnknownOnesAndWarmestFirst() {
            DarkReadyInput i = Input(Need("cold", -20, priority: 1), Need("warm", 0, priority: 2));
            i.Holds.Add(Hold(15, -5));
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeFalse();
            r.Need.ComboKey.Should().Be("warm");
        }

        [Test]
        public void AFailureAtAColderOrEqualAmbientRefusesAsColdOrColder() {
            DarkReadyInput i = Input(Need("a", -15));
            i.Limits.Add(new CoolerLimit { At = Now, SetTemp = -15, Ambient = 12, Reason = "r" });
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("cannot reach");
            i.Ambient = 11;
            r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeTrue();
        }

        [Test]
        public void UncoveredAmbientAllowsOneLearningAttemptPerNight() {
            DarkReadyInput i = Input(Need("a", -15));
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeTrue();
            r.Basis.Should().Contain("learning attempt");
            i.LearningUsedAt = Now.AddMinutes(-30);
            r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("learning attempt was used up");
        }

        [Test]
        public void ASetTemperatureBeyondTheMeasuredCapacityIsRefused() {
            DarkReadyInput i = Input(Need("cold", -17));
            i.Ambient = 19.1;
            i.Holds.Add(new CoolerHold { At = Now.AddHours(-1), Ambient = 19.1, SetTemp = -15, PeakPower = 100 });
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("most the cooler has reached is 34.1 °C below ambient");
            i.Ambient = 17;
            r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeFalse();
        }

        [Test]
        public void NightRunsFromNoonToNoon() {
            DarkReadiness.NightOf(new DateTime(2026, 10, 10, 19, 0, 0)).Should().Be("2026-10-10");
            DarkReadiness.NightOf(new DateTime(2026, 10, 11, 6, 0, 0)).Should().Be("2026-10-10");
            DarkReadiness.NightOf(new DateTime(2026, 10, 11, 12, 0, 0)).Should().Be("2026-10-11");
        }

        [Test]
        public void AFailureOutranksAHoldThatSeemsToCoverTheSameNeed() {
            DarkReadyInput i = Input(Need("a", -12));
            i.Ambient = 8;
            i.LearningUsedAt = Now;
            i.Holds.Add(Hold(15, -10));
            i.Limits.Add(new CoolerLimit { At = Now, SetTemp = -12, Ambient = 5, Reason = "r" });
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("could not hold -12");
        }

        [Test]
        public void ASetTemperatureAboveAmbientIsRefused() {
            DarkReadyInput i = Input(Need("hot", 16));
            i.Holds.Add(Hold(20, 0));
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("cannot heat");
        }

        private static BiasNeed Bias(string key, double offset) {
            return new BiasNeed { ComboKey = key, Priority = 1, Gain = 100, Offset = offset, Binning = 1, FramesNeeded = 30, SessionGapHours = 36 };
        }

        [Test]
        public void BiasOnlyIsReadyWithoutWeatherOrCoolerProof() {
            DarkReadyInput i = Input();
            i.BiasQueue = new List<BiasNeed> { Bias("bias|100|50|1", 50), Bias("bias|100|240|1", 240) };
            i.WeatherConnected = false;
            i.Ambient = null;
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Learning.Should().BeFalse();
            r.Need.Should().BeNull();
            r.Bias.Should().HaveCount(2);
            r.Basis.Should().Contain("no combo in the stacker's dark backlog still needs darks");
            r.Basis.Should().Contain("bias, gain 100, offset 50, bin 1; bias, gain 100, offset 240, bin 1");
        }

        [Test]
        public void BiasIsReadyWhenTheNeededDarksAreNot() {
            DarkReadyInput i = Input(Need("a", -5));
            i.WeatherConnected = false;
            i.BiasQueue = new List<BiasNeed> { Bias("b", 50) };
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeTrue();
            r.Need.Should().BeNull();
            r.Basis.Should().Contain("no weather device");
        }

        [Test]
        public void ReadyDarksAreUnchangedByABiasQueue() {
            DarkReadyInput i = Input(Need("a", -5));
            i.Holds.Add(Hold(20, -5));
            DarkReadyResult without = DarkReadiness.Check(i);
            i.BiasQueue = new List<BiasNeed> { Bias("b", 50) };
            DarkReadyResult with = DarkReadiness.Check(i);
            with.Ready.Should().BeTrue();
            with.Need.ComboKey.Should().Be("a");
            with.Bias.Should().BeNull();
            with.Basis.Should().Be(without.Basis);
        }

        [Test]
        public void NeitherQueueIsNotReady() {
            DarkReadyInput i = Input();
            i.BiasQueue = new List<BiasNeed>();
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Be("no combo in the stacker's dark backlog still needs darks");
            i.BiasQueue = null;
            DarkReadiness.Check(i).Reason.Should().Be("no combo in the stacker's dark backlog still needs darks");
        }

        [Test]
        public void BiasWaitsForTheSunBelowTheLimit() {
            DarkReadyInput i = Input();
            i.BiasQueue = new List<BiasNeed> { Bias("b", 50) };
            i.SunAltitudeAt = t => -7.9;
            DarkReadyResult r = DarkReadiness.Check(i);
            r.Ready.Should().BeFalse();
            r.Reason.Should().Contain("the bias frames cannot be taken either");
            r.Reason.Should().Contain("light-safe limit");
            i.SunAltitudeAt = t => -8.5 + (t - Now).TotalMinutes * 6;
            i.SaveTime = TimeSpan.FromSeconds(10);
            DarkReadiness.Check(i).Ready.Should().BeFalse();
            i.SaveTime = TimeSpan.FromSeconds(3);
            DarkReadiness.Check(i).Ready.Should().BeTrue();
            i.SunAltitudeAt = null;
            DarkReadiness.Check(i).Reason.Should().Contain("not known");
        }
    }
}
