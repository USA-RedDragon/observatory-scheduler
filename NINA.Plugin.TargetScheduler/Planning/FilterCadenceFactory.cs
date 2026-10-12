using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Planning {

    public class FilterCadenceFactory {

        public FilterCadenceFactory() {
        }

        public FilterCadence Generate(IProject project, ITarget target, Target databaseTarget) {
            List<IFilterCadenceItem> filterCadences = new List<IFilterCadenceItem>();

            // Restore from database records if available
            if (Common.IsNotEmpty(databaseTarget.FilterCadences)) {
                try {
                    return FilterCadence.FromRows(databaseTarget.FilterCadences);
                } catch (ArgumentException ex) {
                    // Persisted cadence is corrupt (e.g. duplicated rows from a prior write race).  Discard it
                    // and regenerate below; the next persisted update will overwrite the bad rows with a clean set.
                    TSLogger.Warning($"invalid persisted filter cadence for target {databaseTarget.Id}, regenerating: {ex.Message}");
                    filterCadences.Clear();
                }
            }

            // Generate from override list
            int order = 1;
            if (Common.IsNotEmpty(databaseTarget.OverrideExposureOrders)) {
                databaseTarget.OverrideExposureOrders.ForEach((oeo) => {
                    bool next = order == 1;
                    FilterCadenceAction action = oeo.Action == OverrideExposureOrderAction.Exposure ? FilterCadenceAction.Exposure : FilterCadenceAction.Dither;
                    filterCadences.Add(new PlanningFilterCadence(order++, next, action, oeo.ReferenceIdx));
                });

                return new FilterCadence(filterCadences);
            }

            // In these two cases, we don't have a fixed cadence known beforehand.  Instead,
            // we start empty and will add exposures and dithers later as needed.
            int filterSwitchFrequency = project.FilterSwitchFrequency;
            if (filterSwitchFrequency == 0 || project.SmartExposureOrder) {
                return new FilterCadence(filterCadences);
            }

            // Otherwise, generate from (non-zero) filter switch frequency
            return FilterCadence.ForSwitchFrequency(target.AllExposurePlans.Count, filterSwitchFrequency);
        }
    }
}