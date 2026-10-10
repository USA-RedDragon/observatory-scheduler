using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class ExposureTrackerTest {
        private static readonly DateTime Picked = new DateTime(2026, 10, 10, 2, 39, 11, DateTimeKind.Utc);
        private static readonly DateTime Shutter = new DateTime(2026, 10, 10, 2, 44, 21, DateTimeKind.Utc);

        private static readonly RunningStep[] Slew = { RunningStep.Of("NINA.Sequencer.SequenceItem.Telescope.SlewScopeToRaDec", "Slew") };
        private static readonly RunningStep[] Center = { RunningStep.Of("NINA.Plugin.TargetScheduler.Sequencer.PlanCenterAndRotate", "Center And Rotate") };

        private static readonly RunningStep[] Focus = {
            RunningStep.Of("NINA.Sequencer.Trigger.Autofocus.AutofocusAfterHFRIncreaseTrigger", "AF After HFR Increase"),
            RunningStep.Of("NINA.Sequencer.SequenceItem.Autofocus.RunAutofocus", "Run Autofocus"),
        };

        private static readonly RunningStep[] Guide = {
            RunningStep.Of("NINA.Plugin.TargetScheduler.Sequencer.InstructionContainer", "Before New Target"),
            RunningStep.Of("NINA.Sequencer.SequenceItem.Guider.StartGuiding", "Start Guiding"),
        };

        private static readonly RunningStep[] Take = { RunningStep.Of("NINA.Plugin.TargetScheduler.Sequencer.PlanTakeExposure", "TakeExposure") };
        private static readonly RunningStep[] Dither = { RunningStep.Of("NINA.Sequencer.SequenceItem.Guider.Dither", "Dither") };

        [Test]
        public void TheExposureIsNullUntilTheShutterOpens() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);

            var v = t.View(Picked.AddSeconds(5), Slew);
            v.Exposure.Should().BeNull();
            v.Activity.Should().Be(Activities.Slewing);
            v.ActivityDetail.Should().Be("Slew");

            t.View(Picked.AddMinutes(1), Center).Activity.Should().Be(Activities.Centering);
            t.View(Picked.AddMinutes(2), Focus).Activity.Should().Be(Activities.Focusing);
            t.View(Picked.AddMinutes(2), Focus).ActivityDetail.Should().Be("AF After HFR Increase");
            t.View(Picked.AddMinutes(4), Guide).Activity.Should().Be(Activities.Guiding);
            t.View(Picked.AddMinutes(5), Take).Activity.Should().Be(Activities.StartingExposure);
            t.View(Picked.AddMinutes(5), null).Activity.Should().Be(Activities.Preparing);
            t.View(Picked.AddMinutes(5), null).Exposure.Should().BeNull();
        }

        [Test]
        public void TheExposureTimesComeFromTheShutterNotThePick() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);
            t.View(Picked, Focus);

            t.ShutterOpened(31, "Ha", 300, Shutter);

            var v = t.View(Shutter.AddSeconds(11), Take);
            v.Activity.Should().Be(Activities.Exposing);
            v.Exposure.StartedAt.Should().Be(Shutter);
            v.Exposure.EndsAt.Should().Be(Shutter.AddSeconds(300));
            v.Exposure.Seconds.Should().Be(300);
            v.Exposure.Filter.Should().Be("Ha");
            v.Exposure.PlanId.Should().Be(31);
            v.Exposure.Number.Should().Be(1);
            v.ActivitySince.Should().Be(Shutter.AddSeconds(11));
        }

        [Test]
        public void AfterTheEndTimeTheCameraIsDownloadingNotExposing() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);
            t.ShutterOpened(31, "Ha", 300, Shutter);

            var v = t.View(Shutter.AddSeconds(302), Take);
            v.Exposure.Should().BeNull();
            v.Activity.Should().Be(Activities.Downloading);

            t.CaptureEnded();
            t.View(Shutter.AddSeconds(305), Take).Activity.Should().Be(Activities.Saving);
            t.View(Shutter.AddSeconds(306), Dither).Activity.Should().Be(Activities.Dithering);
            t.View(Shutter.AddSeconds(320), null).Activity.Should().Be(Activities.Saving);
            t.View(Shutter.AddSeconds(320), null).Exposure.Should().BeNull();
        }

        [Test]
        public void OutsideAPlanThereIsNoActivityOrExposure() {
            var t = new ExposureTracker();
            t.View(Picked, Slew).Activity.Should().BeNull();

            t.PlanStarted(7, true);
            t.ShutterOpened(31, "Ha", 300, Shutter);
            t.PlanEnded();

            var v = t.View(Shutter.AddSeconds(10), Take);
            v.Exposure.Should().BeNull();
            v.Activity.Should().BeNull();
            v.ActivitySince.Should().BeNull();
        }

        [Test]
        public void TheNumberCountsRealExposuresOnTheSameTarget() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);
            t.ShutterOpened(31, "Ha", 300, Shutter).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7, false);
            t.PlanEnded();

            t.PlanStarted(7, false);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(12)).Number.Should().Be(2);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7, false);
            t.ShutterOpened(32, "OIII", 300, Shutter.AddMinutes(20)).Number.Should().Be(3);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(8, false);
            t.ShutterOpened(40, "Ha", 300, Shutter.AddMinutes(30)).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(8, true);
            t.ShutterOpened(40, "Ha", 300, Shutter.AddMinutes(40)).Number.Should().Be(1);
        }

        [Test]
        public void ARepeatedPlanStartForTheSameTargetDoesNotResetTheCount() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);
            t.ShutterOpened(31, "Ha", 300, Shutter);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7, false);
            t.PlanStarted(7, false);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(6)).Number.Should().Be(2);
        }

        [Test]
        public void PendingTimingFollowsTheRealApplyPoint() {
            var t = new ExposureTracker();
            t.Pending(Picked).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.BeforeNextExposure));

            t.PlanStarted(7, true);
            t.Pending(Picked.AddMinutes(2)).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.AfterNextExposure));

            t.ShutterOpened(31, "Ha", 300, Shutter);
            t.Pending(Shutter.AddSeconds(11)).Should().BeEquivalentTo(PendingTiming.At(Shutter.AddSeconds(300), PendingTiming.WhenExposureEnds));

            t.Pending(Shutter.AddSeconds(301)).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.AfterSaving));
            t.CaptureEnded();
            t.Pending(Shutter.AddSeconds(305)).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.AfterSaving));

            t.PlanEnded();
            t.Pending(Shutter.AddSeconds(310)).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.BeforeNextExposure));
        }

        [Test]
        public void ActivitySinceMarksWhenThePhaseChanged() {
            var t = new ExposureTracker();
            t.PlanStarted(7, true);
            t.View(Picked, Slew).ActivitySince.Should().Be(Picked);
            t.View(Picked.AddSeconds(30), Slew).ActivitySince.Should().Be(Picked);
            t.View(Picked.AddSeconds(40), Focus).ActivitySince.Should().Be(Picked.AddSeconds(40));
        }

        [TestCase("NINA.Sequencer.SequenceItem.Telescope.SlewScopeToRaDec", Activities.Slewing)]
        [TestCase("NINA.Sequencer.SequenceItem.Platesolving.Center", Activities.Centering)]
        [TestCase("NINA.Sequencer.SequenceItem.Platesolving.CenterAndRotate", Activities.Centering)]
        [TestCase("NINA.Sequencer.SequenceItem.Platesolving.SolveAndSync", Activities.Centering)]
        [TestCase("NINA.Sequencer.Trigger.Platesolving.CenterAfterDriftTrigger", Activities.Centering)]
        [TestCase("NINA.Sequencer.SequenceItem.Autofocus.RunAutofocus", Activities.Focusing)]
        [TestCase("NINA.Sequencer.Trigger.Autofocus.AutofocusAfterTimeTrigger", Activities.Focusing)]
        [TestCase("NINA.Sequencer.Trigger.MeridianFlip.MeridianFlipTrigger", Activities.MeridianFlip)]
        [TestCase("NINA.Sequencer.Trigger.Guider.DitherAfterExposures", Activities.Dithering)]
        [TestCase("NINA.Sequencer.SequenceItem.Guider.Dither", Activities.Dithering)]
        [TestCase("NINA.Sequencer.SequenceItem.Guider.StartGuiding", Activities.Guiding)]
        [TestCase("NINA.Sequencer.SequenceItem.Guider.RestoreGuiding", Activities.Guiding)]
        [TestCase("NINA.Sequencer.SequenceItem.FilterWheel.SwitchFilter", Activities.SwitchingFilter)]
        [TestCase("NINA.Plugin.TargetScheduler.Sequencer.PlanWaitInstruction", Activities.Waiting)]
        [TestCase("NINA.Plugin.TargetScheduler.Sequencer.PlanTakeExposure", Activities.Exposure)]
        [TestCase("NINA.Plugin.TargetScheduler.Sequencer.SyncTakeExposure", Activities.Exposure)]
        [TestCase("NINA.Sequencer.SequenceItem.Camera.SetReadoutMode", Activities.Busy)]
        [TestCase("NINA.Plugin.TargetScheduler.Sequencer.InstructionContainer", null)]
        [TestCase("NINA.Sequencer.Container.SequentialContainer", null)]
        [TestCase("NINA.Plugin.TargetScheduler.Sequencer.PlanSchedulerProgress", null)]
        public void StepsAreClassifiedByType(string type, string activity) {
            ActivityClassifier.Classify(type).Should().Be(activity);
        }

        [Test]
        public void UnknownStepsAreBusyWithTheirName() {
            var (a, d) = ActivityClassifier.Classify(new[] {
                RunningStep.Of("NINA.Sequencer.Container.SequentialContainer", "Before New Target"),
                RunningStep.Of("Some.Plugin.OpenDomeShutter", "Open Dome"),
            });
            a.Should().Be(Activities.Busy);
            d.Should().Be("Open Dome");
        }

        [Test]
        public void AKnownStepBeatsABusyOne() {
            var (a, d) = ActivityClassifier.Classify(new[] {
                RunningStep.Of("Some.Plugin.SmartTrigger", "Smart"),
                RunningStep.Of("NINA.Sequencer.SequenceItem.Autofocus.RunAutofocus", "Run Autofocus"),
            });
            a.Should().Be(Activities.Focusing);
            d.Should().Be("Run Autofocus");
        }

        [Test]
        public void ThePendingCommandFromTheLiveNightAppliesAtTheRealExposureEnd() {
            using var db = new TestDb();
            var ctx = new FakeContext { Database = new SqlSchedulerDatabase(db.Connect), Now = Picked };
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            var t = new ExposureTracker();
            var proc = new CommandProcessor(CommandRegistry.Builtin(), store, ctx, () => true, () => true, () => t.Pending(ctx.Now));
            var events = new List<CommandResult>();
            proc.ResultChanged += r => events.Add(r);

            t.PlanStarted(7, true);
            ctx.Now = Picked.AddMinutes(2);
            var early = proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":5,""changes"":[{""field"":""priority"",""before"":1,""after"":0}]}", "early"), "api");
            early.Status.Should().Be(CommandStatus.Pending);
            early.AppliesAt.Should().BeNull();
            early.Message.Should().Be(PendingTiming.AfterNextExposure);

            t.ShutterOpened(31, "Ha", 300, Shutter);
            events.Clear();
            proc.RefreshPending().Should().Be(1);
            events.Should().ContainSingle().Which.AppliesAt.Should().Be(Shutter.AddSeconds(300));

            ctx.Now = Shutter.AddSeconds(11);
            var late = proc.Submit(Cmd.Of(CommandKinds.ProjectEdit, @"{""id"":7,""changes"":[{""field"":""priority"",""before"":0,""after"":2}]}", "late"), "api");
            late.Status.Should().Be(CommandStatus.Pending);
            late.AppliesAt.Should().Be(Shutter.AddSeconds(300));
            late.AppliesAt.Should().BeAfter(ctx.Now);
            late.Message.Should().Be(PendingTiming.WhenExposureEnds);

            ctx.Now = Shutter.AddSeconds(302);
            proc.Get("late").AppliesAt.Should().BeNull();
            proc.Get("late").Message.Should().Be(PendingTiming.AfterSaving);

            t.CaptureEnded();
            t.PlanEnded();
            proc.ApplyPending().Should().Be(2);
            store.GetResult("early").Status.Should().Be(CommandStatus.Applied);
            store.GetResult("late").Status.Should().Be(CommandStatus.Applied);
        }
    }
}
