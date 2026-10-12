using FluentAssertions;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class FilterCadenceAcrossSwitchesTest {

        private static readonly string[] Lrgb = { "L", "R", "G", "B" };

        private sealed class Night {
            private readonly Dictionary<int, List<FilterCadenceItem>> rows = new Dictionary<int, List<FilterCadenceItem>>();
            private readonly int frequency;
            public readonly List<string> Taken = new List<string>();

            public Night(int frequency) {
                this.frequency = frequency;
            }

            public string Take(int targetId) {
                FilterCadence cadence = rows.TryGetValue(targetId, out var saved)
                    ? FilterCadence.FromRows(saved)
                    : FilterCadence.ForSwitchFrequency(Lrgb.Length, frequency);
                IFilterCadenceItem item = null;
                foreach (IFilterCadenceItem i in cadence) {
                    item = i;
                    break;
                }
                cadence.SetLastSelected(item);
                cadence.Advance();
                rows[targetId] = cadence.ToRows(targetId);
                string taken = $"{targetId}:{Lrgb[item.ReferenceIdx]}";
                Taken.Add(taken);
                return taken;
            }

            public List<FilterCadenceItem> Rows(int targetId) => rows[targetId];
        }

        [Test]
        public void ATargetResumesItsFilterCycleAfterAnotherTarget() {
            var night = new Night(1);
            night.Take(1);
            night.Take(1);
            night.Take(2);
            night.Take(2);
            night.Take(2);
            night.Take(1);
            night.Take(2);
            night.Take(1);

            night.Taken.Should().Equal("1:L", "1:R", "2:L", "2:R", "2:G", "1:G", "2:B", "1:B");
        }

        [Test]
        public void SwitchingAfterEverySubStillCyclesEveryFilter() {
            var night = new Night(1);
            for (int i = 0; i < 8; i++) {
                night.Take(1);
                night.Take(2);
                night.Take(3);
            }

            foreach (int target in new[] { 1, 2, 3 }) {
                night.Taken.Where(t => t.StartsWith($"{target}:")).Select(t => t.Substring(2))
                    .Should().Equal("L", "R", "G", "B", "L", "R", "G", "B");
            }
        }

        [Test]
        public void AFrequencyOfTwoKeepsItsPlaceInsideAPair() {
            var night = new Night(2);
            night.Take(1);
            night.Take(2);
            night.Take(1);
            night.Take(1);

            night.Taken.Should().Equal("1:L", "2:L", "1:L", "1:R");
        }

        [Test]
        public void TheRowsAPlanIsRebuiltFromNameTheNextFilter() {
            var night = new Night(1);
            night.Take(1);
            night.Take(2);

            var rows = night.Rows(1);
            rows.Select(r => r.TargetId).Distinct().Should().Equal(1);
            rows.Select(r => r.Order).Should().Equal(1, 2, 3, 4);
            rows.Single(r => r.Next).ReferenceIdx.Should().Be(1);
            FilterCadence.FromRows(rows).List.Single(i => i.Next).ReferenceIdx.Should().Be(1);
        }

        [Test]
        public void ForSwitchFrequencyMatchesTheFactoryLayout() {
            var c = FilterCadence.ForSwitchFrequency(3, 2);
            c.List.Select(i => i.Order).Should().Equal(1, 2, 3, 4, 5, 6);
            c.List.Select(i => i.ReferenceIdx).Should().Equal(0, 0, 1, 1, 2, 2);
            c.List.Select(i => i.Next).Should().Equal(true, false, false, false, false, false);
            c.List.All(i => i.Action == FilterCadenceAction.Exposure).Should().BeTrue();
            FilterCadence.ForSwitchFrequency(0, 1).Count.Should().Be(0);
        }
    }
}
