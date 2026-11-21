using FluentAssertions;
using NINA.Plugin.TargetScheduler.Grading;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class GradingSweepTest {

        private static List<SweepImage> Images(int count, int pending, double exposure = 300, int firstId = 1) {
            return Enumerable.Range(0, count).Select(i => new SweepImage {
                Id = firstId + i,
                ExposurePlanId = 7,
                ProfileId = "p",
                Exposure = exposure,
                Gain = 100,
                Offset = 50,
                Binning = "1x1",
                Roi = 100,
                Pending = i < pending
            }).ToList();
        }

        private static SweepPlan Plan(List<SweepImage> images, bool goal = true, bool graderEnabled = true) {
            return new SweepPlan { TargetId = 3, ExposurePlanId = 7, HasGoal = goal, GraderEnabled = graderEnabled, TargetRoi = 100, Images = images };
        }

        [Test]
        public void GroupBelowTheMinimumIsNotEnqueued() {
            GradingSweep.Select(new[] { Plan(Images(19, 19)) }, true).Should().BeEmpty();
        }

        [Test]
        public void ReadyGroupWithPendingIsEnqueuedOnce() {
            var picks = GradingSweep.Select(new[] { Plan(Images(20, 5)) }, true);
            picks.Should().HaveCount(1);
            picks[0].AcquiredImageId.Should().Be(1);
            picks[0].PendingCount.Should().Be(5);
            picks[0].GroupCount.Should().Be(20);
            picks[0].TargetId.Should().Be(3);
            picks[0].ExposurePlanId.Should().Be(7);
        }

        [Test]
        public void ReadyGroupWithNoPendingIsNotEnqueued() {
            GradingSweep.Select(new[] { Plan(Images(25, 0)) }, true).Should().BeEmpty();
        }

        [Test]
        public void PlanWithoutGoalIsNotEnqueued() {
            GradingSweep.Select(new[] { Plan(Images(30, 30), goal: false) }, true).Should().BeEmpty();
        }

        [Test]
        public void TwoMatchingGroupsInOnePlanAreEachEnqueuedOnce() {
            var images = Images(20, 2, 300, 1);
            images.AddRange(Images(22, 3, 120, 100));
            var picks = GradingSweep.Select(new[] { Plan(images) }, true);
            picks.Should().HaveCount(2);
            picks.Select(p => p.AcquiredImageId).Should().BeEquivalentTo(new[] { 1, 100 });
            picks.Sum(p => p.PendingCount).Should().Be(5);
        }

        [Test]
        public void DisabledGraderIsNotEnqueued() {
            GradingSweep.Select(new[] { Plan(Images(30, 30), graderEnabled: false) }, true).Should().BeEmpty();
        }

        [Test]
        public void ImmediateGradingIsNotSwept() {
            GradingSweep.Select(new[] { Plan(Images(30, 30)) }, false).Should().BeEmpty();
        }

        [Test]
        public void ImagesOutsideTheTargetRoiDoNotCountTowardAGroup() {
            var images = Images(25, 25);
            images.Take(10).ToList().ForEach(i => i.Roi = 50);
            GradingSweep.Select(new[] { Plan(images) }, true).Should().BeEmpty();
        }
    }
}
