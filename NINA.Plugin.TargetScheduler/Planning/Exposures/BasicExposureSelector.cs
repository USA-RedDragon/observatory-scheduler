using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    /// <summary>
    /// Select the next exposure based on the persisted filter cadence for this target.  Add a dither before the
    /// exposure if appropriate.
    /// </summary>
    public class BasicExposureSelector : BaseExposureSelector, IExposureSelector {

        public BasicExposureSelector(IProject project, ITarget target, Target databaseTarget) : base(target) {
            FilterCadence = new FilterCadenceFactory().Generate(project, target, databaseTarget);
            DitherManager = GetDitherManager(project, target);
        }

        public IExposure Select(DateTime atTime, IProject project, ITarget target) {
            if (AllExposurePlansRejected(target)) {
                TSLogger.Warning($"unexpected: all exposure plans were rejected at exposure selection time for target '{target.Name}' at time {atTime}");
                return null;
            }

            try {
                foreach (IFilterCadenceItem item in FilterCadence) {
                    IExposure exposure = target.AllExposurePlans[item.ReferenceIdx];
                    if (exposure.Rejected) { continue; }

                    exposure.PreDither = DitherManager.DitherRequired(exposure);
                    FilterCadence.SetLastSelected(item);
                    return exposure;
                }
            } catch (Exception ex) {
                TSLogger.Warning($"exception in basic exposure selector for target '{target.Name}' at time {atTime}: {ex.Message}, aborting target");
                return null;
            }

            TSLogger.Warning($"no acceptable exposure plan in basic exposure selector for target '{target.Name}' at time {atTime}");
            return null;
        }

        /// <summary>
        /// Move the cadence's selection to the first item, from the next one on, for the exposure that Filter Steering
        /// chose instead of this selector's pick, so the cycle continues after the filter actually shot.
        /// </summary>
        public bool Follow(IExposure exposure) {
            foreach (IFilterCadenceItem item in FilterCadence) {
                if (item.Action == FilterCadenceAction.Exposure && ReferenceEquals(Target.AllExposurePlans[item.ReferenceIdx], exposure)) {
                    FilterCadence.SetLastSelected(item);
                    return true;
                }
            }

            return false;
        }

        public void ExposureTaken(IExposure exposure) {
            Action undoCadence = UndoCadence();
            UndoTaken = () => {
                undoCadence();
                DitherManager.RemoveLast(exposure);
            };
            FilterCadence.Advance();
            UpdateFilterCadences(FilterCadence);

            if (exposure.PreDither) DitherManager.Reset();
            DitherManager.AddExposure(exposure);
        }

        public void TargetReset() {
            DitherManager.Reset();
        }
    }
}