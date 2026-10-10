using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

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
            DarkPlanner.MoveTime(-13, -15, rates).Should().Be(TimeSpan.FromSeconds(40));
            DarkPlanner.MoveTime(-13, -5, rates).Should().BeNull();
            DarkPlanner.MoveTime(-13, -5, new CoolerRates { WarmingSecondsPerC = 5 }).Should().Be(TimeSpan.FromSeconds(40));
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
        public void BeforeWaitStartsOnlyADarkThatEndsWithRoomForRestoreAndAfterWait() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, WaitUntil = Now.AddSeconds(450), AfterWait = TimeSpan.FromSeconds(40), SunAltitudeAt = Night };
            var rates = new CoolerRates { CoolingSecondsPerC = 30, WarmingSecondsPerC = 10 };
            TimeSpan download = TimeSpan.FromSeconds(2);

            DarkPlanner.FitReason(w, Need("same", 1, -13), -13, -13, rates, download).Should().BeNull();
            DarkPlanner.FitReason(w, Need("long", 1, -13, exposure: 420), -13, -13, rates, download).Should().Contain("after the wait ends");
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
            DarkPlanner.LimitReason(limits, -15, 16.5, -13, 90).Should().BeNull();
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
                again.AfterWait().Should().Be(TimeSpan.FromSeconds(42));
                again.Limits().Should().BeEmpty();
            } finally {
                File.Delete(path);
            }
        }
    }
}
