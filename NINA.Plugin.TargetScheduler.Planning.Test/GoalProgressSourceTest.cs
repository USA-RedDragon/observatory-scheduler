using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GoalProgressSourceTest {
        private const string T1 = "6F9619FF-8B86-D011-B42D-00C04FC964FF";
        private const string T2 = "aaaaaaaa-0000-0000-0000-000000000002";

        private class Rows : IGoalRowProvider {
            public List<GoalRow> Goals = new List<GoalRow>();
            public List<GoalProgressRow> Progress = new List<GoalProgressRow>();

            public IList<GoalRow> LoadGoals() => Goals;

            public IList<GoalProgressRow> LoadProgress() => Progress;
        }

        private static Rows Sample() {
            var rows = new Rows();
            rows.Goals.Add(new GoalRow { TargetGuid = T1, Filter = "Ha", Kind = GoalKind.FaintSnr, SnrGoal = 12 });
            rows.Goals.Add(new GoalRow { TargetGuid = T1, Filter = "OIII", Kind = GoalKind.Depth, DepthGoal = 25.5 });
            rows.Goals.Add(new GoalRow { TargetGuid = T2, Filter = "L" });
            rows.Progress.Add(new GoalProgressRow { TargetGuid = T1.ToLowerInvariant(), Filter = " HA ", Progress = 0.5, EffectiveHours = 3, HoursNeeded = 2, Done = false });
            rows.Progress.Add(new GoalProgressRow { TargetGuid = T1, Filter = "SII", Progress = 0.9, Done = true });
            rows.Progress.Add(new GoalProgressRow { TargetGuid = "other", Filter = "L", Progress = 1, Done = true });
            return rows;
        }

        [Test]
        public void GetNeedsBothAGoalAndAProgressRow() {
            var sut = new SqliteGoalProgressSource(Sample());
            sut.Get(T1, "Ha").Should().NotBeNull();
            sut.Get(T1, "OIII").Should().BeNull();
            sut.Get(T1, "SII").Should().BeNull();
            sut.Get(T2, "L").Should().BeNull();
            sut.Get("other", "L").Should().BeNull();
            sut.Count.Should().Be(1);
        }

        [Test]
        public void MatchingIgnoresCaseAndWhitespace() {
            var sut = new SqliteGoalProgressSource(Sample());
            sut.Get(T1.ToLowerInvariant(), "ha").Should().NotBeNull();
            sut.Get(" " + T1 + " ", "  Ha").Progress.Should().Be(0.5);
            sut.Get(null, "Ha").Should().BeNull();
            sut.Get(T1, null).Should().BeNull();
            sut.Get(T1, " ").Should().BeNull();
        }

        [Test]
        public void GoalDrivenMeansHavingGoalRows() {
            var sut = new SqliteGoalProgressSource(Sample());
            sut.IsGoalDriven(T1).Should().BeTrue();
            sut.IsGoalDriven(T2.ToUpperInvariant()).Should().BeTrue();
            sut.IsGoalDriven("other").Should().BeFalse();
        }

        [Test]
        public void ForTargetListsMatchedFilters() {
            var sut = new SqliteGoalProgressSource(Sample());
            sut.ForTarget(T1).Should().ContainSingle().Which.Filter.Should().Be("HA");
            sut.ForTarget(T2).Should().BeEmpty();
            sut.ForTarget(null).Should().BeEmpty();
        }

        [Test]
        public void GoalValueFallsBackToTheGoalRow() {
            var rows = Sample();
            rows.Progress.Add(new GoalProgressRow { TargetGuid = T1, Filter = "OIII", Progress = double.NaN });
            var sut = new SqliteGoalProgressSource(rows);
            GoalProgress oiii = sut.Get(T1, "oiii");
            oiii.Kind.Should().Be(GoalKind.Depth);
            oiii.GoalValue.Should().Be(25.5);
            oiii.Progress.Should().Be(0);
            sut.Get(T1, "Ha").GoalValue.Should().Be(12);
        }

        [Test]
        public void EmptySourceHasNoGoals() {
            SqliteGoalProgressSource.Empty.Get(T1, "Ha").Should().BeNull();
            SqliteGoalProgressSource.Empty.Count.Should().Be(0);
        }
    }
}
