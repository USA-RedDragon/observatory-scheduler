using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class PlannerSignalsTest {

        [Test]
        public void BoostsAreClampedAndTakeTheLargestPerTarget() {
            var signals = new PlannerSignals(new List<GoalProgressRow> {
                new GoalProgressRow { TargetGuid = " T1 ", Filter = "Ha", SeasonBoost = 0.4 },
                new GoalProgressRow { TargetGuid = "t1", Filter = "OIII", SeasonBoost = 0.6 },
                new GoalProgressRow { TargetGuid = "t2", Filter = "L", SeasonBoost = 3 },
                new GoalProgressRow { TargetGuid = "t3", Filter = "L", SeasonBoost = double.NaN },
                new GoalProgressRow { TargetGuid = "t4", Filter = "L" },
                null
            });
            signals.SeasonBoost("T1").Should().Be(0.6);
            signals.SeasonBoost("t2").Should().Be(1);
            signals.SeasonBoost("t3").Should().Be(0);
            signals.SeasonBoost("t4").Should().Be(0);
            signals.SeasonBoost(null).Should().Be(0);
            signals.BoostCount.Should().Be(2);
        }

        [Test]
        public void NightQualityIsTheNewestAndExpires() {
            DateTime at = new DateTime(2026, 10, 9, 5, 0, 0, DateTimeKind.Utc);
            var signals = new PlannerSignals(new List<GoalProgressRow> {
                new GoalProgressRow { TargetGuid = "t1", NightQuality = 0.4, NightQualityAt = at.AddHours(-2) },
                new GoalProgressRow { TargetGuid = "t2", NightQuality = 0.9, NightQualityAt = at },
                new GoalProgressRow { TargetGuid = "t3", NightQuality = 0.1 }
            });
            signals.NightQuality(at.AddMinutes(30)).Should().Be(0.9);
            signals.NightQuality(at.AddHours(4)).Should().BeNull();
            signals.NightQuality(at.AddHours(-4)).Should().BeNull();
            PlannerSignals.Empty.NightQuality(at).Should().BeNull();
        }

        [Test]
        public void CurrentNeverGoesNull() {
            PlannerSignals.Current = null;
            PlannerSignals.Current.Should().BeSameAs(PlannerSignals.Empty);
        }
    }
}
