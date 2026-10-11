using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Exposures {

    /// <summary>
    /// Determine the appropriate exposure selector implementation based on project/target settings.
    /// </summary>
    public class ExposureSelectionExpert {

        public ExposureSelectionExpert() {
        }

        public IExposureSelector GetExposureSelector(IProject project, ITarget target, Target databaseTarget) {
            IExposureSelector selector = GetDefaultExposureSelector(project, target, databaseTarget);
            return FilterSteeringRule.IsOn(project) ? new SteeringExposureSelector(project, target, selector) : selector;
        }

        public IExposureSelector GetDefaultExposureSelector(IProject project, ITarget target, Target databaseTarget) {
            if (project.SmartExposureOrder) {
                return new SmartExposureSelector(project, target, databaseTarget);
            }

            if (databaseTarget.OverrideExposureOrders.Count > 0) {
                return new OverrideOrderExposureSelector(project, target, databaseTarget);
            }

            if (project.FilterSwitchFrequency == 0) {
                return new RepeatUntilDoneExposureSelector(project, target, databaseTarget);
            }

            return new BasicExposureSelector(project, target, databaseTarget);
        }
    }

    public interface IExposureSelector {

        IExposure Select(DateTime atTime, IProject project, ITarget target);

        void ExposureTaken(IExposure exposure);

        /// <summary>
        /// Undo the last ExposureTaken because its frame was aborted and discarded: the cadence goes back
        /// to the aborted exposure and the frame no longer counts toward the next dither. A dither that
        /// already ran before the frame still counts.
        /// </summary>
        void ExposureAborted(IExposure exposure);

        void TargetReset();
    }
}