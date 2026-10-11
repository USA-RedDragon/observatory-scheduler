using FluentAssertions;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class FilterCadenceAbortTest {

        private static FilterCadence Cadence(int next, params FilterCadenceAction[] actions) {
            var items = new List<IFilterCadenceItem>();
            for (int i = 0; i < actions.Length; i++) {
                items.Add(new PlanningFilterCadence(i + 1, i == next, actions[i], i));
            }
            return new FilterCadence(items);
        }

        private static FilterCadence Lrgb(int next = 0) => Cadence(next, FilterCadenceAction.Exposure, FilterCadenceAction.Exposure, FilterCadenceAction.Exposure, FilterCadenceAction.Exposure);

        private static IFilterCadenceItem First(FilterCadence c) {
            foreach (IFilterCadenceItem item in c) {
                return item;
            }
            return null;
        }

        private static int NextIdx(FilterCadence c) => c.List.ToList().FindIndex(i => i.Next);

        private static List<bool> Persisted(FilterCadence c) => c.List.Select(i => i.Next).ToList();

        [Test]
        public void TheCadenceIsAdvancedWhileTheExposureRunsSoThePreviewSeesTheNextFilter() {
            var c = Lrgb();
            var selected = First(c);
            c.SetLastSelected(selected);
            var mark = c.Mark();
            c.Advance();

            NextIdx(c).Should().Be(1);
            First(c).ReferenceIdx.Should().Be(1);
            mark.Should().Be(new CadenceMark(0, 0));
        }

        [Test]
        public void RestoringAfterAnAbortPutsTheAbortedFilterNextAgain() {
            var c = Lrgb();
            var before = Persisted(c);
            c.SetLastSelected(First(c));
            var mark = c.Mark();
            c.Advance();

            c.Restore(mark);

            Persisted(c).Should().Equal(before);
            First(c).ReferenceIdx.Should().Be(0);
            c.Advance().ReferenceIdx.Should().Be(1);
        }

        [Test]
        public void AFreshCadenceBuiltFromTheRestoredRowsMatchesTheOneBeforeTheExposure() {
            var c = Lrgb(3);
            c.SetLastSelected(First(c));
            var mark = c.Mark();
            c.Advance();
            NextIdx(c).Should().Be(0);

            c.Restore(mark);
            var rows = c.List.Select(i => new FilterCadenceItem(7, i.Order, i.Next, i.Action, i.ReferenceIdx)).ToList();
            var replanned = new FilterCadence(rows.Select(r => (IFilterCadenceItem)new PlanningFilterCadence(r)).ToList());

            First(replanned).ReferenceIdx.Should().Be(3);
        }

        [Test]
        public void RestoringBringsBackSkippedItemsAndAPrecedingDither() {
            var c = Cadence(0, FilterCadenceAction.Dither, FilterCadenceAction.Exposure, FilterCadenceAction.Exposure);
            var exposure = c.List[1];
            c.SetLastSelected(exposure);
            var mark = c.Mark();
            c.Advance();
            NextIdx(c).Should().Be(2);

            c.Restore(mark);

            NextIdx(c).Should().Be(0);
            c.Advance().Should().BeSameAs(c.List[2]);
        }

        [Test]
        public void AMarkThatDoesNotFitTheCadenceChangesNothing() {
            var c = Lrgb(2);
            c.Restore(new CadenceMark(-1, 0));
            c.Restore(new CadenceMark(9, 0));
            c.Restore(null);
            NextIdx(c).Should().Be(2);
            c.List.Count(i => i.Next).Should().Be(1);
        }
    }
}
