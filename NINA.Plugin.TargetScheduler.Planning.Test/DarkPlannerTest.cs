using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class DarkPlannerTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc);

        private static double? Night(DateTime t) => -40;

        private static Func<DateTime, double?> Rising(double atNow, double degreesPerMinute) => t => atNow + (t - Now).TotalMinutes * degreesPerMinute;

        private static DarkNeed Need(string key, int priority, double setTemp, double exposure = 300, int have = 0, DateTime? newest = null) {
            return new DarkNeed { ComboKey = key, Priority = priority, Exposure = exposure, Gain = 100, Offset = 50, SetTemp = setTemp, FramesNeeded = 3, FramesHave = have, NewestDarkAt = newest, SessionGapHours = 36 };
        }

        [Test]
        public void RemainingCountsOnlyDarksTheStackerHasNotSeen() {
            DarkNeed n = Need("a", 1, -13, have: 1, newest: Now.AddHours(-2));
            var taken = new List<DarkTaken> {
                new DarkTaken { ComboKey = "a", TakenAt = Now.AddHours(-3) },
                new DarkTaken { ComboKey = "a", TakenAt = Now.AddHours(-1) },
                new DarkTaken { ComboKey = "b", TakenAt = Now.AddHours(-1) },
                new DarkTaken { ComboKey = "a", TakenAt = Now.AddHours(-40) }
            };
            DarkPlanner.TakenNotIngested(n, taken, Now).Should().Be(1);
            DarkPlanner.Remaining(n, taken, Now).Should().Be(1);
            taken.Add(new DarkTaken { ComboKey = "a", TakenAt = Now.AddMinutes(-5) });
            DarkPlanner.Remaining(n, taken, Now).Should().Be(0);
        }

        [Test]
        public void QueueKeepsTheStackerPriorityAndDropsMetCombos() {
            var needs = new List<DarkNeed> { Need("late", 3, -5), Need("first", 1, -20), Need("met", 2, -13, have: 3), Need("bad", 4, -5, exposure: 0) };
            List<DarkNeed> q = DarkPlanner.Queue(needs, new List<DarkTaken>(), Now);
            q.ConvertAll(n => n.ComboKey).Should().Equal("first", "late");
        }

        [Test]
        public void AtTemperatureUsesNinasTwoDegreeTolerance() {
            DarkPlanner.AtTemperature(-11.0, -13).Should().BeTrue();
            DarkPlanner.AtTemperature(-10.9, -13).Should().BeFalse();
            DarkPlanner.AtTemperature(double.NaN, -13).Should().BeFalse();
        }

        [Test]
        public void MoveTimeNeedsAMeasuredRateInThatDirection() {
            var rates = new CoolerRates { CoolingSecondsPerC = 20 };
            DarkPlanner.MoveTime(-13, -13, rates).Should().Be(TimeSpan.Zero);
            DarkPlanner.MoveTime(-13, -15, rates).Should().Be(TimeSpan.FromMinutes(2));
            DarkPlanner.MoveTime(-13, -15, new CoolerRates { CoolingSecondsPerC = 90 }).Should().Be(TimeSpan.FromSeconds(180));
            DarkPlanner.MoveTime(-13, -5, rates).Should().BeNull();
            DarkPlanner.MoveTime(-13, -5, new CoolerRates { WarmingSecondsPerC = 5 }).Should().Be(TimeSpan.FromMinutes(8));
        }

        [Test]
        public void AMeasuredSettleTimeIsAddedToTheRamp() {
            var rates = new CoolerRates { CoolingSecondsPerC = 100, CoolingSettleSeconds = 45, WarmingSecondsPerC = 63 };
            DarkPlanner.MoveTime(-10, -30, rates).Should().Be(TimeSpan.FromSeconds(1200 + 45));
            DarkPlanner.MoveTime(-14, -15, rates).Should().Be(TimeSpan.FromSeconds(60 + 45));
            DarkPlanner.MoveTime(-15, -14, rates).Should().Be(TimeSpan.FromSeconds(63));
            DarkPlanner.MoveTime(-15, -14, new CoolerRates { WarmingSettleSeconds = 30 }).Should().BeNull();
            DarkPlanner.SettleSeconds(-13, -15, TimeSpan.FromSeconds(150)).Should().Be(30);
            DarkPlanner.SettleSeconds(-13, -15, TimeSpan.FromSeconds(100)).Should().Be(0);
            DarkPlanner.SettleSeconds(-13, -13.5, TimeSpan.FromSeconds(100)).Should().BeNull();
        }

        [Test]
        public void TheSetpointRampsAtOneDegreePerMinuteInWholeNinaSteps() {
            DarkPlanner.Ramp(-5, -15).Should().Be(TimeSpan.FromMinutes(10));
            DarkPlanner.Ramp(-15, -5).Should().Be(TimeSpan.FromMinutes(10));
            DarkPlanner.Ramp(20, -5).Should().Be(TimeSpan.FromMinutes(25));
            DarkPlanner.Ramp(-13.1, -15).Should().Be(TimeSpan.FromSeconds(120));
            DarkPlanner.Ramp(-13.3, -15).Should().Be(TimeSpan.FromSeconds(105));
            DarkPlanner.Ramp(-14.6, -15).Should().Be(DarkPlanner.MinRamp);
            DarkPlanner.Ramp(-15, -15).Should().Be(DarkPlanner.MinRamp);
            DarkPlanner.Ramp(40, -40).Should().Be(DarkPlanner.MaxRamp);
            DarkPlanner.Ramp(double.NaN, -15).Should().Be(DarkPlanner.MaxRamp);
            for (double from = -30; from <= 30; from += 0.7) {
                (DarkPlanner.Ramp(from, -10).TotalSeconds % 15).Should().Be(0);
            }
        }

        [Test]
        public void BeforeWaitStopsWithoutAWaitATimedAfterWaitOrOnAReplan() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, SunAltitudeAt = Night };
            DarkPlanner.StopReason(w).Should().Contain("not inside");
            w.WaitUntil = Now.AddHours(1);
            DarkPlanner.StopReason(w).Should().Contain("AfterWait");
            w.AfterWait = TimeSpan.FromSeconds(40);
            DarkPlanner.StopReason(w).Should().BeNull();
            w.ReplanOrPause = true;
            DarkPlanner.StopReason(w).Should().Contain("re-plan");
            w.ReplanOrPause = false;
            w.Now = Now.AddHours(1).AddSeconds(-30);
            DarkPlanner.StopReason(w).Should().Be("the wait is over");
        }

        [Test]
        public void StopAndFitNameTheirCause() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, SunAltitudeAt = Night };
            DarkPlanner.Stop(w).Code.Should().Be("wait_end");
            w.WaitUntil = Now.AddHours(1);
            w.AfterWait = TimeSpan.FromSeconds(40);
            DarkPlanner.Stop(w).Should().Be(((string)null, (string)null));
            w.ReplanOrPause = true;
            DarkPlanner.Stop(w).Code.Should().Be("replan");
            var u = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = Now, SunAltitudeAt = Night, Safe = true };
            DarkPlanner.Stop(u).Should().Be(("safe", "it is safe again"));
            u.Safe = null;
            DarkPlanner.Stop(u).Code.Should().Be("no_safety");
            u.StopBy = Now;
            DarkPlanner.Stop(u).Code.Should().Be("time_limit");
            u.SunAltitudeAt = t => null;
            u.StopBy = null;
            DarkPlanner.Stop(u).Code.Should().Be("sun");

            var need = new DarkNeed { Exposure = 300, SetTemp = 0 };
            var wait = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, WaitUntil = Now.AddSeconds(200), AfterWait = TimeSpan.FromSeconds(40), SunAltitudeAt = Night };
            var rates = new CoolerRates { CoolingSecondsPerC = 30, WarmingSecondsPerC = 10 };
            DarkPlanner.Fit(wait, need, 0, 0, rates, TimeSpan.Zero).Code.Should().Be("wait_end");
            DarkPlanner.Fit(wait, need, 5, 5, new CoolerRates(), TimeSpan.Zero).Code.Should().Be("unmeasured_move");
            var unsafeWindow = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = Now, SunAltitudeAt = Night };
            DarkPlanner.Fit(unsafeWindow, need, 0, 0, rates, TimeSpan.Zero).Should().Be(((string)null, (string)null));
            DarkPlanner.FitReason(wait, need, 0, 0, rates, TimeSpan.Zero).Should().Be(DarkPlanner.Fit(wait, need, 0, 0, rates, TimeSpan.Zero).Reason);
        }

        [Test]
        public void BeforeWaitStartsOnlyADarkThatEndsWithRoomForRestoreAndAfterWait() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, WaitUntil = Now.AddSeconds(600), AfterWait = TimeSpan.FromSeconds(40), SunAltitudeAt = Night };
            var rates = new CoolerRates { CoolingSecondsPerC = 30, WarmingSecondsPerC = 10 };
            TimeSpan download = TimeSpan.FromSeconds(2);

            DarkPlanner.FitReason(w, Need("same", 1, -13), -13, -13, rates, download).Should().BeNull();
            DarkPlanner.FitReason(w, Need("long", 1, -13, exposure: 600), -13, -13, rates, download).Should().Contain("after the wait ends");
            DarkPlanner.FitReason(w, Need("warm", 1, -11), -13, -13, rates, download).Should().BeNull();
            DarkPlanner.FitReason(w, Need("warmer", 1, -9), -13, -13, rates, download).Should().Contain("after the wait ends");
            DarkPlanner.FitReason(w, Need("warm", 1, -11), -13, -13, new CoolerRates { WarmingSecondsPerC = 10 }, download).Should().Contain("not been measured");
        }

        [Test]
        public void AnUnmeasuredMoveIsSkippedWhenTheSunLimitIsNear() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 0.8, 0.2) };
            string reason = DarkPlanner.FitReason(w, Need("cold", 1, -20, exposure: 120), -14, -14, new CoolerRates { WarmingSecondsPerC = 25.9 }, TimeSpan.FromSeconds(3));
            reason.Should().Contain("cooling rate has not been measured").And.Contain("must start by");
            DarkPlanner.FitReason(w, Need("same", 1, -14, exposure: 120), -14, -14, new CoolerRates(), TimeSpan.FromSeconds(3)).Should().BeNull();
        }

        [Test]
        public void TheMeasuredRateRejectsTheDarkTheIncidentStarted() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 0.8, 0.2) };
            var rates = new CoolerRates { CoolingSecondsPerC = 41.8, WarmingSecondsPerC = 25.9 };
            DarkPlanner.FitReason(w, Need("cold", 1, -20, exposure: 120), -14, -14, rates, TimeSpan.FromSeconds(3)).Should().Contain("light-safe limit");
        }

        [Test]
        public void AnUnmeasuredMoveIsMeasuredWhenNoDeadlineIsNear() {
            var w = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = Now, Safe = false, SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 30, 0.2) };
            DarkPlanner.FitReason(w, Need("cold", 1, -20, exposure: 300), -14, -14, new CoolerRates(), TimeSpan.FromSeconds(3)).Should().BeNull();
            DarkPlanner.LatestStart(w, 300, TimeSpan.FromSeconds(3), TimeSpan.FromHours(18)).Should().Be(Now.AddMinutes(150).AddSeconds(-303));
            DarkPlanner.LatestStart(w, 300, TimeSpan.Zero, TimeSpan.FromHours(1)).Should().BeNull();
        }

        [Test]
        public void BeforeWaitSkipsAnUnmeasuredMoveBecauseTheWaitEnds() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, WaitUntil = Now.AddHours(5), AfterWait = TimeSpan.Zero, SunAltitudeAt = Night };
            DarkPlanner.FitReason(w, Need("cold", 1, -20), -14, -14, new CoolerRates { WarmingSecondsPerC = 10 }, TimeSpan.Zero).Should().Contain("the wait ends");
        }

        [Test]
        public void WhileUnsafeYieldsAsSoonAsItIsSafe() {
            var w = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = Now, SunAltitudeAt = Night };
            DarkPlanner.StopReason(w).Should().Contain("no safety monitor");
            w.Safe = false;
            DarkPlanner.StopReason(w).Should().BeNull();
            DarkPlanner.FitReason(w, Need("x", 1, -30, exposure: 3600), -13, -13, new CoolerRates(), TimeSpan.Zero).Should().BeNull();
            w.Safe = true;
            DarkPlanner.StopReason(w).Should().Be("it is safe again");
        }

        [Test]
        public void EndOfSequenceRunsUntilItsOptionalTimeLimit() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night };
            DarkPlanner.StopReason(w).Should().BeNull();
            w.StopBy = Now;
            DarkPlanner.StopReason(w).Should().Contain("time limit");
        }

        private static DarkCover Flat(bool connected, DarkCoverState state) {
            return new DarkCover { Configured = true, Device = "ASCOM.DeepSkyDad.FP.CoverCalibrator1", Connected = connected, State = state };
        }

        [Test]
        public void FlatDeviceNotConnectedStopsEveryPlacement() {
            foreach (DarkPlacement p in Enum.GetValues(typeof(DarkPlacement))) {
                var w = new DarkWindow { Placement = p, Now = Now, WaitUntil = Now.AddHours(1), AfterWait = TimeSpan.Zero, Safe = false, SunAltitudeAt = Night, Cover = Flat(false, DarkCoverState.Closed) };
                DarkPlanner.StopReason(w).Should().Contain("not connected").And.Contain("ASCOM.DeepSkyDad.FP.CoverCalibrator1");
            }
        }

        [TestCase(DarkCoverState.Open)]
        [TestCase(DarkCoverState.NeitherOpenNorClosed)]
        [TestCase(DarkCoverState.Unknown)]
        [TestCase(DarkCoverState.Error)]
        [TestCase(DarkCoverState.NotPresent)]
        public void CoverNotClosedStops(DarkCoverState state) {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night, Cover = Flat(true, state) };
            DarkPlanner.StopReason(w).Should().Contain($"reports its cover as {state}, not Closed");
        }

        [Test]
        public void UnreadableFlatDeviceStops() {
            var c = Flat(true, DarkCoverState.Closed);
            c.ReadError = "driver threw";
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night, Cover = c };
            DarkPlanner.StopReason(w).Should().Contain("could not be read").And.Contain("driver threw");
        }

        [Test]
        public void ClosedCoverAllowsDarks() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night, Cover = Flat(true, DarkCoverState.Closed) };
            DarkPlanner.CoverStopReason(w.Cover).Should().BeNull();
            DarkPlanner.StopReason(w).Should().BeNull();
        }

        [Test]
        public void ALitPanelStopsDarksUnderAClosedCover() {
            var c = Flat(true, DarkCoverState.Closed);
            c.LightOn = true;
            DarkPlanner.CoverStopReason(c).Should().Contain("light on");
        }

        [Test]
        public void NoFlatDeviceInTheProfileKeepsTheOldBehavior() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night, Cover = new DarkCover { Configured = false } };
            DarkPlanner.StopReason(w).Should().BeNull();
            w.Cover = null;
            DarkPlanner.StopReason(w).Should().BeNull();
        }

        [Test]
        public void CoverOpeningMidRunStopsBeforeTheNextFrame() {
            DarkCover cover = Flat(true, DarkCoverState.Closed);
            int captured = 0;
            string stop = null;
            for (int frame = 0; frame < 10; frame++) {
                var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night, Cover = cover };
                stop = DarkPlanner.StopReason(w);
                if (stop != null) {
                    break;
                }
                captured++;
                if (captured == 2) {
                    cover.State = DarkCoverState.Open;
                }
            }
            captured.Should().Be(2);
            stop.Should().Contain("reports its cover as Open");
        }

        [Test]
        public void EveryPlacementStopsWhenTheSunIsAtTheLimitOrUnknown() {
            foreach (DarkPlacement p in Enum.GetValues(typeof(DarkPlacement))) {
                var w = new DarkWindow { Placement = p, Now = Now, WaitUntil = Now.AddHours(1), AfterWait = TimeSpan.Zero, Safe = false, SunAltitudeAt = t => DarkPlanner.SunLimitDegrees };
                DarkPlanner.StopReason(w).Should().Contain("light-safe limit");
                w.SunAltitudeAt = t => DarkPlanner.SunLimitDegrees + 10;
                DarkPlanner.StopReason(w).Should().Contain("light-safe limit");
                w.SunAltitudeAt = null;
                DarkPlanner.StopReason(w).Should().Contain("not known");
                w.SunAltitudeAt = t => null;
                DarkPlanner.StopReason(w).Should().Contain("not known");
                w.SunAltitudeAt = t => DarkPlanner.SunLimitDegrees - 0.1;
                DarkPlanner.StopReason(w).Should().BeNull();
            }
        }

        [Test]
        public void EveryPlacementStartsOnlyADarkThatEndsBeforeTheSunReachesTheLimit() {
            foreach (DarkPlacement p in Enum.GetValues(typeof(DarkPlacement))) {
                var w = new DarkWindow { Placement = p, Now = Now, WaitUntil = Now.AddHours(10), AfterWait = TimeSpan.Zero, Safe = false, SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 2.1, 0.21) };
                var rates = new CoolerRates { CoolingSecondsPerC = 30, WarmingSecondsPerC = 30 };
                DarkPlanner.FitReason(w, Need("short", 1, -13, exposure: 300), -13, -13, rates, TimeSpan.FromSeconds(5)).Should().BeNull();
                DarkPlanner.FitReason(w, Need("long", 1, -13, exposure: 600), -13, -13, rates, TimeSpan.FromSeconds(5)).Should().Contain("light-safe limit");
                DarkPlanner.FitReason(w, Need("move", 1, -23, exposure: 300), -13, -13, rates, TimeSpan.FromSeconds(5)).Should().Contain("light-safe limit");
                DarkPlanner.SunFitReason(w, TimeSpan.Zero, 600, TimeSpan.Zero).Should().Contain("light-safe limit");
            }
        }

        [Test]
        public void ADuskDarkFitsOnceTheSunIsBelowTheLimit() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 0.5, -0.2) };
            DarkPlanner.StopReason(w).Should().BeNull();
            DarkPlanner.SunFitReason(w, TimeSpan.Zero, 3600, TimeSpan.Zero).Should().BeNull();
        }

        [Test]
        public void ThePeakDuringTheDarkCountsNotOnlyItsEnds() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = t => Math.Abs((t - Now).TotalMinutes - 5) < 1 ? DarkPlanner.SunLimitDegrees + 1 : -40 };
            DarkPlanner.SunFitReason(w, TimeSpan.Zero, 600, TimeSpan.Zero).Should().Contain("light-safe limit");
        }

        [Test]
        public void CoolerLimitSkipsAsColdOrColderUntilItIsCoolerOutside() {
            var limits = new List<CoolerLimit> { new CoolerLimit { SetTemp = -15, Ambient = 17, ImagingPower = 90, ImagingSetpoint = -13, Reason = "r" } };
            DarkPlanner.LimitReason(limits, -15, 17, -13, 90).Should().Contain("could not hold -15");
            DarkPlanner.LimitReason(limits, -20, 18, -13, 90).Should().NotBeNull();
            DarkPlanner.LimitReason(limits, -14, 17, -13, 90).Should().BeNull();
            DarkPlanner.LimitReason(limits, -15, 16.5, -13, 90).Should().Contain("16 °C or lower");
            DarkPlanner.LimitReason(limits, -15, 16, -13, 90).Should().BeNull();
        }

        private static List<CoolerHold> DawnHolds() {
            return new List<CoolerHold> {
                new CoolerHold { At = Now.AddMinutes(-40), Ambient = 19.1, SetTemp = -15, PeakPower = 100 },
                new CoolerHold { At = Now.AddMinutes(-50), Ambient = 19.1, SetTemp = -4.24, PeakPower = 75 }
            };
        }

        private static List<CoolerLimit> DawnLimits(bool reached) {
            return new List<CoolerLimit> {
                new CoolerLimit { At = Now.AddMinutes(-49), SetTemp = -20, Ambient = 19.1, Reached = reached ? -14.9 : (double?)null, Reason = "r" },
                new CoolerLimit { At = Now.AddMinutes(-41), SetTemp = -17, Ambient = 19.1, Reached = reached ? -15.3 : (double?)null, Reason = "r" }
            };
        }

        [Test]
        public void CapacitySkipsSetpointsColderThanTheCoolerHasReachedAtThisAmbient() {
            CoolerCapacity c = DarkPlanner.Capacity(DawnHolds(), DawnLimits(true));
            c.DropC.Should().BeApproximately(34.4, 0.001);
            DarkPlanner.CapacityReason(c, -20, 19.1).Should().Contain("left for a night with the ambient at 14.4 °C or lower");
            DarkPlanner.CapacityReason(c, -17, 19.1).Should().Contain("36.1 °C below the 19.1 °C ambient");
            DarkPlanner.CapacityReason(c, -16, 19.1).Should().NotBeNull();
            DarkPlanner.CapacityReason(c, -15, 19.1).Should().BeNull();
            DarkPlanner.CapacityReason(c, -17, 17.1).Should().BeNull();
            DarkPlanner.CapacityReason(c, -3, 19.1).Should().BeNull();
        }

        [Test]
        public void CapacityFromOldEvidenceUsesTheBestHold() {
            CoolerCapacity c = DarkPlanner.Capacity(DawnHolds(), DawnLimits(false));
            c.DropC.Should().BeApproximately(34.1, 0.001);
            c.ToString().Should().Contain("held -15 °C at 19.1 °C ambient");
            DarkPlanner.CapacityReason(c, -15, 19.1).Should().BeNull();
            DarkPlanner.CapacityReason(c, -16, 19.1).Should().NotBeNull();
        }

        [Test]
        public void CapacityIsUnknownUntilTheCoolerHasBeenPushedToItsLimit() {
            var holds = new List<CoolerHold> { new CoolerHold { At = Now, Ambient = 19, SetTemp = -4, PeakPower = 80 } };
            DarkPlanner.Capacity(holds, new List<CoolerLimit>()).Should().BeNull();
            DarkPlanner.CapacityReason(null, -30, 19).Should().BeNull();
            DarkPlanner.Capacity(new List<CoolerHold>(), DawnLimits(false)).Should().BeNull();
            holds.Add(new CoolerHold { At = Now, Ambient = 19, SetTemp = -14, PeakPower = 99 });
            DarkPlanner.Capacity(holds, null).DropC.Should().Be(33);
            DarkPlanner.CapacityReason(DarkPlanner.Capacity(holds, null), -14, null).Should().BeNull();
        }

        [Test]
        public void ALimitIsRetriedOnlyAfterTheAmbientDropsOneDegree() {
            List<CoolerLimit> limits = DawnLimits(true);
            DarkPlanner.LimitReason(limits, -17, 18.5, -4.24, null).Should().Contain("18.1 °C or lower");
            DarkPlanner.LimitReason(limits, -17, 18.1, -4.24, null).Should().BeNull();
            DarkPlanner.LimitReason(limits, -15, 19.1, -4.24, null).Should().BeNull();
        }

        [Test]
        public void TonightFirstPutsSetpointsTheImagingCoolerHoldsAheadOfColderOnes() {
            var queue = new List<DarkNeed> {
                Need("300|0|240|1|-15", 1, -15),
                Need("300|0|50|1|-20", 2, -20),
                Need("300|0|50|1|-3", 3, -3),
                Need("300|0|50|1|6", 4, 6),
                Need("600|0|50|1|-6", 5, -6),
                Need("600|0|50|1|-7", 6, -7)
            };
            DarkPlanner.TonightFirst(queue, -4.24).Select(n => n.ComboKey).Should().Equal(
                "300|0|50|1|-3", "300|0|50|1|6", "600|0|50|1|-6", "300|0|240|1|-15", "300|0|50|1|-20", "600|0|50|1|-7");
            DarkPlanner.TonightFirst(queue, double.NaN).Select(n => n.Priority).Should().Equal(1, 2, 3, 4, 5, 6);
        }

        [Test]
        public void CoolerLimitFallsBackToCoolerPowerAtTheImagingSetpoint() {
            var limits = new List<CoolerLimit> { new CoolerLimit { SetTemp = -15, ImagingPower = 91, ImagingSetpoint = -13, Reason = "r" } };
            DarkPlanner.LimitReason(limits, -15, null, -13, 91).Should().Contain("91%");
            DarkPlanner.LimitReason(limits, -15, null, -13, 80).Should().BeNull();
            DarkPlanner.LimitReason(limits, -15, null, -10, 50).Should().Contain("earlier this run");
        }

        [Test]
        public void ReadoutModeIsFoundByName() {
            var modes = new List<string> { "Default", "High Gain" };
            DarkPlanner.ReadoutIndex("high gain", modes).Should().Be((short)1);
            DarkPlanner.ReadoutIndex(null, modes).Should().BeNull();
            DarkPlanner.ReadoutIndex("Fast", modes).Should().BeNull();
        }

        [Test]
        public void StateMeasuresRatesAndPersists() {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"darks-{Guid.NewGuid():N}.json");
            try {
                var s = new DarkState(path);
                s.AfterWait().Should().BeNull();
                s.RecordMove(-13, -15, TimeSpan.FromSeconds(60), Now);
                s.RecordMove(-15, -13, TimeSpan.FromSeconds(20), Now);
                s.RecordMove(-13, -13.5, TimeSpan.FromSeconds(20), Now);
                s.RecordAfterWait(TimeSpan.FromSeconds(42), Now);
                var again = new DarkState(path);
                again.Rates().CoolingSecondsPerC.Should().Be(30);
                again.Rates().WarmingSecondsPerC.Should().Be(10);
                again.Rates().CoolingSettleSeconds.Should().Be(0);
                again.Rates().WarmingSettleSeconds.Should().Be(0);
                s.RecordMove(-13, -18, TimeSpan.FromSeconds(340), Now);
                new DarkState(path).Rates().CoolingSettleSeconds.Should().Be(40);
                again.AfterWait().Should().Be(TimeSpan.FromSeconds(42));
                again.Limits().Should().BeEmpty();
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void WarmFirstOrdersBySetTemperatureThenPriority() {
            var q = DarkPlanner.WarmFirst(new[] { Need("c", 1, -20), Need("w2", 3, 0), Need("w1", 2, 0), Need("m", 1, -10) });
            q.ConvertAll(n => n.ComboKey).Should().Equal("w1", "w2", "m", "c");
        }

        [Test]
        public void EvidencePersistsAndTheNewestWins() {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"darks-{Guid.NewGuid():N}.json");
            try {
                var s = new DarkState(path);
                s.AddLimit(new CoolerLimit { At = Now, SetTemp = -15, Ambient = 12, Reason = "r" });
                s.AddLimit(new CoolerLimit { At = Now, SetTemp = -20, Reason = "no ambient" });
                s.RecordLearning("2026-10-10", Now, 12);

                var again = new DarkState(path);
                again.Limits().Should().ContainSingle().Which.SetTemp.Should().Be(-15);
                again.LearningUsed("2026-10-10").Should().Be(Now);
                again.LearningUsed("2026-10-11").Should().BeNull();

                again.RecordHold(new CoolerHold { At = Now.AddHours(1), Ambient = 13, SetTemp = -16, PeakPower = 95 });
                again.Limits().Should().BeEmpty();
                again.Holds().Should().ContainSingle();

                again.AddLimit(new CoolerLimit { At = Now.AddHours(2), SetTemp = -14, Ambient = 12.5, Reason = "r2" });
                var third = new DarkState(path);
                third.Holds().Should().BeEmpty();
                third.Limits().Should().ContainSingle().Which.Reason.Should().Be("r2");
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void EvidenceKeepsTheNewestEntries() {
            var s = new DarkState(null);
            for (int k = 0; k < DarkState.MaxEvidence + 10; k++) {
                s.RecordHold(new CoolerHold { At = Now.AddMinutes(k), Ambient = k, SetTemp = 100 + k });
            }
            s.Holds().Should().HaveCount(DarkState.MaxEvidence);
            s.Holds().Should().NotContain(h => h.At < Now.AddMinutes(10));
        }

        [Test]
        public void KeptFailuresExpireAndCanBeLeftOut() {
            var s = new DarkState(null);
            s.AddLimit(new CoolerLimit { At = DateTime.UtcNow.AddDays(-31), SetTemp = -20, Ambient = 10, Reason = "old" });
            s.AddLimit(new CoolerLimit { At = DateTime.UtcNow, SetTemp = -15, Ambient = 10, Reason = "new" });
            s.AddLimit(new CoolerLimit { At = DateTime.UtcNow, SetTemp = -25, Reason = "this run" });
            s.Limits().Should().HaveCount(2).And.NotContain(l => l.Reason == "old");
            s.Limits(false).Should().ContainSingle().Which.Reason.Should().Be("this run");
        }

        [Test]
        public void ASetTemperatureAboveAmbientCannotBeReached() {
            DarkPlanner.AboveAmbientReason(15, 12).Should().Contain("cannot heat");
            DarkPlanner.AboveAmbientReason(12, 12).Should().BeNull();
            DarkPlanner.AboveAmbientReason(15, null).Should().BeNull();
        }
    }
}
