using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace NINA.Plugin.TargetScheduler.Planning {

    public class FilterCadence : IEnumerable {
        private object lockObj = new object();
        private List<IFilterCadenceItem> filterCadenceList;
        private int lastSelectedIdx = -1;

        public FilterCadence(List<IFilterCadenceItem> filterCadenceList) {
            this.filterCadenceList = filterCadenceList is null
                ? new List<IFilterCadenceItem>(0)
                : new List<IFilterCadenceItem>(filterCadenceList);
            AssertProper();
        }

        public static FilterCadence FromRows(IEnumerable<FilterCadenceItem> rows) {
            return new FilterCadence(rows.Select(r => (IFilterCadenceItem)new PlanningFilterCadence(r)).ToList());
        }

        public static FilterCadence ForSwitchFrequency(int plans, int filterSwitchFrequency) {
            var items = new List<IFilterCadenceItem>();
            int order = 1;
            for (int idx = 0; idx < plans; idx++) {
                for (int i = 0; i < filterSwitchFrequency; i++) {
                    items.Add(new PlanningFilterCadence(order, order == 1, FilterCadenceAction.Exposure, idx));
                    order++;
                }
            }
            return new FilterCadence(items);
        }

        public List<FilterCadenceItem> ToRows(int targetId) {
            lock (lockObj) {
                return filterCadenceList.Select(fci => new FilterCadenceItem {
                    TargetId = targetId,
                    Order = fci.Order,
                    Next = fci.Next,
                    Action = fci.Action,
                    ReferenceIdx = fci.ReferenceIdx,
                }).ToList();
            }
        }

        public void SetLastSelected(IFilterCadenceItem item) {
            lock (lockObj) {
                int idx = filterCadenceList.FindIndex(fc => fc == item);
                if (idx == -1) {
                    TSLogger.Warning("filter cadence SetLastSelected: item not found");
                    lastSelectedIdx = -1;
                    return;
                }

                lastSelectedIdx = idx;
            }
        }

        public IFilterCadenceItem Advance() {
            if (lastSelectedIdx == -1) {
                TSLogger.Warning("filter cadence Advance called with no last selected position => no change");
                return null;
            }

            lock (lockObj) {
                int idx = filterCadenceList.FindIndex(fc => fc.Next);
                filterCadenceList[idx].Next = false;
                int next = lastSelectedIdx == filterCadenceList.Count - 1 ? 0 : lastSelectedIdx + 1;
                filterCadenceList[next].Next = true;

                lastSelectedIdx = -1;
                return filterCadenceList[next];
            }
        }

        public CadenceMark Mark() {
            lock (lockObj) {
                return new CadenceMark(filterCadenceList.FindIndex(fc => fc.Next), lastSelectedIdx);
            }
        }

        public void Restore(CadenceMark mark) {
            lock (lockObj) {
                if (mark == null || mark.Next < 0 || mark.Next >= filterCadenceList.Count) { return; }
                for (int i = 0; i < filterCadenceList.Count; i++) {
                    filterCadenceList[i].Next = i == mark.Next;
                }
                lastSelectedIdx = mark.LastSelected;
            }
        }

        public int Count { get => filterCadenceList.Count; }

        public ReadOnlyCollection<IFilterCadenceItem> List { get => filterCadenceList.AsReadOnly(); }

        private void AssertProper() {
            if (filterCadenceList.Count == 0) { return; }

            int count = 0;
            int order = 1;
            filterCadenceList.ForEach(fc => {
                if (fc.Next) count++;
                if (fc.Order != order++) {
                    throw new ArgumentException($"incorrect ordering in filter cadence list at index {order - 2}");
                }
            });

            if (count != 1) {
                throw new ArgumentException($"wrong count of next items in filter cadence list: {count}");
            }
        }

        public IEnumerator GetEnumerator() {
            return new FilterCadenceEnumerator(filterCadenceList);
        }

        public override string ToString() {
            StringBuilder sb = new StringBuilder();
            filterCadenceList.ForEach(fc => { sb.Append(fc).AppendLine(); });
            return sb.ToString();
        }
    }

    public sealed record CadenceMark(int Next, int LastSelected);

    /// <summary>
    /// Custom circular iterator filter cadence for cycling.
    /// </summary>
    internal class FilterCadenceEnumerator : IEnumerator {
        private List<IFilterCadenceItem> filterCadenceList;
        private int currentIdx = -1;
        private int wrappedIdx;
        private bool first = true;

        public FilterCadenceEnumerator(List<IFilterCadenceItem> filterCadenceList) {
            this.filterCadenceList = filterCadenceList;
            if (filterCadenceList.Count > 0) {
                currentIdx = filterCadenceList.FindIndex(fc => fc.Next);
                if (currentIdx == -1) { throw new ArgumentException("filter cadence list has no next=true"); }
                wrappedIdx = currentIdx;
            }
        }

        public object Current => filterCadenceList[currentIdx];

        public bool MoveNext() {
            if (filterCadenceList.Count == 0) return false;

            if (!first) {
                currentIdx = currentIdx == filterCadenceList.Count - 1 ? 0 : currentIdx + 1;
            } else {
                first = false;
                return true;
            }

            return currentIdx != wrappedIdx;
        }

        public void Reset() {
            throw new NotImplementedException("not implemented");
        }
    }
}