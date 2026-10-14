using NINA.Core.Model;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Plugin.TargetScheduler.Util;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    public class SchedulerTargetEndTimeTrigger : SequenceTrigger {
        private DateTime EndTime;
        private int TargetId;

        public SchedulerTargetEndTimeTrigger(DateTime endTime, int targetId) {
            Name = nameof(SchedulerTargetEndTimeTrigger);
            Category = PlanContainer.INSTRUCTION_CATEGORY;
            this.EndTime = endTime;
            this.TargetId = targetId;
        }

        public override object Clone() {
            throw new NotImplementedException();
        }

        public override Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            TSLogger.Info("target stop time exceeded, interrupting target container");
            Planning.EndTimeStops.Record(TargetId, EndTime);
            this.Parent.Interrupt().Wait(token);
            return Task.CompletedTask;
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            double nextDuration = nextItem?.GetEstimatedDuration().TotalSeconds ?? 0;
            bool shouldTrigger = DateTime.Now.AddSeconds(nextDuration) > EndTime;
            if (shouldTrigger) {
                TSLogger.Info($"will trigger scheduler target plan stop: now plus next duration {nextDuration} > endTime {Utils.FormatDateTimeFull(EndTime)}");
            }

            return shouldTrigger;
        }
    }
}