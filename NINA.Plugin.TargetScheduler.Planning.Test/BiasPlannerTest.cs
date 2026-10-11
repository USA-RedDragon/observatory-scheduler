using FluentAssertions;
using NINA.Plugin.TargetScheduler.Darks;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class BiasPlannerTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc);

        private static double? Night(DateTime t) => -40;

        private static Func<DateTime, double?> Rising(double atNow, double degreesPerMinute) => t => atNow + (t - Now).TotalMinutes * degreesPerMinute;

        private static BiasNeed Need(string key, int priority, int have = 0, DateTime? newest = null, double gapHours = 36) {
            return new BiasNeed { ComboKey = key, Priority = priority, Gain = 100, Offset = 50, Binning = 1, FramesNeeded = 30, FramesHave = have, NewestBiasAt = newest, SessionGapHours = gapHours };
        }

        [Test]
        public void RemainingCountsOnlyFramesTheStackerHasNotSeen() {
            BiasNeed n = Need("a", 1, have: 26, newest: Now.AddHours(-2));
            var taken = new List<BiasTaken> {
                new BiasTaken { ComboKey = "a", TakenAt = Now.AddHours(-3) },
                new BiasTaken { ComboKey = "a", TakenAt = Now.AddHours(-1) },
                new BiasTaken { ComboKey = "a", TakenAt = Now.AddMinutes(-30) },
                new BiasTaken { ComboKey = "b", TakenAt = Now.AddHours(-1) },
                new BiasTaken { ComboKey = "a", TakenAt = Now.AddHours(-40) }
            };
            BiasPlanner.TakenNotIngested(n, taken, Now).Should().Be(2);
            BiasPlanner.Remaining(n, taken, Now).Should().Be(2);
            taken.Add(new BiasTaken { ComboKey = "a", TakenAt = Now.AddMinutes(-5) });
            taken.Add(new BiasTaken { ComboKey = "a", TakenAt = Now.AddMinutes(-4) });
            taken.Add(new BiasTaken { ComboKey = "a", TakenAt = Now.AddMinutes(-3) });
            BiasPlanner.Remaining(n, taken, Now).Should().Be(0);
        }

        [Test]
        public void TakenNotIngestedUsesTheSessionGapWithA36HourDefault() {
            var taken = new List<BiasTaken> { new BiasTaken { ComboKey = "a", TakenAt = Now.AddHours(-30) } };
            BiasPlanner.TakenNotIngested(Need("a", 1, gapHours: 36), taken, Now).Should().Be(1);
            BiasPlanner.TakenNotIngested(Need("a", 1, gapHours: 0), taken, Now).Should().Be(1);
            BiasPlanner.TakenNotIngested(Need("a", 1, gapHours: 24), taken, Now).Should().Be(0);
            BiasPlanner.SessionGap(Need("a", 1, gapHours: 0)).Should().Be(TimeSpan.FromHours(36));
            BiasPlanner.SessionGap(Need("a", 1, gapHours: 12)).Should().Be(TimeSpan.FromHours(12));
        }

        [Test]
        public void QueueKeepsThePriorityThenTheKeyAndDropsMetCombos() {
            var needs = new List<BiasNeed> { Need("z", 1), Need("late", 3), Need("a", 1), Need("met", 2, have: 30), null, Need("req", 0) };
            var taken = new List<BiasTaken>();
            for (int i = 0; i < 30; i++) {
                taken.Add(new BiasTaken { ComboKey = "late", TakenAt = Now.AddMinutes(-i) });
            }
            List<BiasNeed> q = BiasPlanner.Queue(needs, taken, Now);
            q.ConvertAll(n => n.ComboKey).Should().Equal("req", "a", "z");
            BiasPlanner.Queue(null, null, Now).Should().BeEmpty();
        }

        [Test]
        public void ExposureIsTheCameraMinimumAsNinaCapturesIt() {
            BiasPlanner.Exposure(0.000032).Should().Be(0.000032);
            BiasPlanner.Exposure(0).Should().Be(0);
            BiasPlanner.Exposure(double.NaN).Should().Be(0);
            BiasPlanner.Exposure(-1).Should().Be(0);
            BiasPlanner.Exposure(double.PositiveInfinity).Should().Be(0);
            BiasPlanner.Exposure(30).Should().Be(30);
            BiasPlanner.Exposure(30.5).Should().Be(0);
        }

        [Test]
        public void TheSunMustStayBelowTheLimitForTheFrameAndItsDownload() {
            var w = new DarkWindow { Placement = DarkPlacement.EndOfSequence, Now = Now, SunAltitudeAt = Night };
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(5)).Should().BeNull();
            w.SunAltitudeAt = Rising(DarkPlanner.SunLimitDegrees - 0.05, 1);
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(1)).Should().BeNull();
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(10)).Should().Contain("light-safe limit");
            w.SunAltitudeAt = t => DarkPlanner.SunLimitDegrees;
            BiasPlanner.FitReason(w, 0, TimeSpan.Zero).Should().Contain("light-safe limit");
            w.SunAltitudeAt = null;
            BiasPlanner.FitReason(w, 0, TimeSpan.Zero).Should().Contain("not known");
        }

        [Test]
        public void BeforeWaitNeedsTheFrameAndAfterWaitToEndBeforeTheWaitEnds() {
            var w = new DarkWindow { Placement = DarkPlacement.BeforeWait, Now = Now, WaitUntil = Now.AddSeconds(50), SunAltitudeAt = Night };
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(5)).Should().Contain("AfterWait instructions have not been timed");
            w.AfterWait = TimeSpan.FromSeconds(40);
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(5)).Should().BeNull();
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(10)).Should().BeNull();
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(11)).Should().Contain("after the wait ends");
            BiasPlanner.FitReason(w, 2, TimeSpan.FromSeconds(9)).Should().Contain("a 2 s bias frame");
            w.WaitUntil = null;
            BiasPlanner.FitReason(w, 0, TimeSpan.Zero).Should().Contain("not inside");
        }

        [Test]
        public void WhileUnsafeAndEndOfSequenceIgnoreTheWait() {
            var w = new DarkWindow { Placement = DarkPlacement.WhileUnsafe, Now = Now, Safe = false, SunAltitudeAt = Night };
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(5)).Should().BeNull();
            w.Placement = DarkPlacement.EndOfSequence;
            BiasPlanner.FitReason(w, 0, TimeSpan.FromSeconds(5)).Should().BeNull();
        }

        [Test]
        public void DescribeNamesEachCombo() {
            var b = new BiasNeed { ComboKey = "bias|0|240|1", Gain = 0, Offset = 240, Binning = 1 };
            BiasPlanner.Describe(new[] { Need("a", 1), b }).Should().Be("bias, gain 100, offset 50, bin 1; bias, gain 0, offset 240, bin 1");
        }
    }
}
