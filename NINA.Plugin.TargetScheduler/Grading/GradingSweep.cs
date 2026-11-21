using NINA.Plugin.TargetScheduler.Goals;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Grading {

    /// <summary>One acquired image reduced to the fields the grader's matching rule compares.</summary>
    public class SweepImage {
        public int Id { get; set; }
        public int ExposurePlanId { get; set; }
        public string ProfileId { get; set; }
        public double Exposure { get; set; }
        public int Gain { get; set; }
        public int Offset { get; set; }
        public string Binning { get; set; }
        public double Roi { get; set; }
        public bool Pending { get; set; }
    }

    /// <summary>One exposure plan with its acquired images and whether the sweep may grade it.</summary>
    public class SweepPlan {
        public int TargetId { get; set; }
        public int ExposurePlanId { get; set; }
        public bool HasGoal { get; set; }
        public bool GraderEnabled { get; set; }
        public double TargetRoi { get; set; }
        public List<SweepImage> Images { get; set; } = new List<SweepImage>();
    }

    /// <summary>One grading request: the image stands for its matching group, whose pending images all grade.</summary>
    public class SweepPick {
        public int TargetId { get; set; }
        public int ExposurePlanId { get; set; }
        public int AcquiredImageId { get; set; }
        public int GroupCount { get; set; }
        public int PendingCount { get; set; }
    }

    /// <summary>Chooses the matching groups of goal-driven plans that have enough subs and a pending backlog.</summary>
    public static class GradingSweep {

        /// <summary>
        /// Groups each goal plan's images the way the grader matches a population (profile, exposure, gain, offset,
        /// binning, and the target's ROI) and picks one pending image of every group that is ready to grade.
        /// </summary>
        public static List<SweepPick> Select(IEnumerable<SweepPlan> plans, bool delayedGradingEnabled) {
            List<SweepPick> picks = new List<SweepPick>();
            if (!delayedGradingEnabled || plans == null) { return picks; }

            foreach (SweepPlan plan in plans) {
                if (plan == null || !plan.HasGoal || !plan.GraderEnabled || plan.Images == null) { continue; }

                var groups = plan.Images
                    .Where(i => i.Roi == plan.TargetRoi)
                    .GroupBy(i => (i.ProfileId, i.Exposure, i.Gain, i.Offset, i.Binning));

                foreach (var group in groups) {
                    List<SweepImage> members = group.ToList();
                    if (!GoalCompletion.GradingReady(members.Count)) { continue; }

                    List<SweepImage> pending = members.Where(i => i.Pending).ToList();
                    if (pending.Count == 0) { continue; }

                    picks.Add(new SweepPick {
                        TargetId = plan.TargetId,
                        ExposurePlanId = plan.ExposurePlanId,
                        AcquiredImageId = pending[0].Id,
                        GroupCount = members.Count,
                        PendingCount = pending.Count
                    });
                }
            }

            return picks;
        }
    }
}
