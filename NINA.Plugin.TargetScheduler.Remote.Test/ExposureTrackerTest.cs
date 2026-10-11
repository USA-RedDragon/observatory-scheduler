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
            t.PlanStarted(7);

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
            t.PlanStarted(7);
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
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter);

            var v = t.View(Shutter.AddSeconds(302), Take);
            v.Activity.Should().Be(Activities.Downloading);
            v.Exposure.PlanId.Should().Be(31);
            v.Exposure.StartedAt.Should().Be(Shutter);

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

            t.PlanStarted(7);
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
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7);
            t.PlanEnded();

            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(12)).Number.Should().Be(2);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7);
            t.ShutterOpened(32, "OIII", 300, Shutter.AddMinutes(20)).Number.Should().Be(3);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(8);
            t.ShutterOpened(40, "Ha", 300, Shutter.AddMinutes(30)).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(8);
            t.ShutterOpened(40, "Ha", 300, Shutter.AddMinutes(40)).Number.Should().Be(2);
        }

        [Test]
        public void AnAbortedExposureIsNotCountedOrShownAsSaving() {
            var t = new ExposureTracker();
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(10)).Number.Should().Be(2);
            t.CaptureAborted();
            var v = t.View(Shutter.AddMinutes(11), Take);
            v.Exposure.Should().BeNull();
            v.Activity.Should().NotBe(Activities.Saving);
            t.PlanEnded();

            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(12)).Number.Should().Be(2);
            t.CaptureAborted();
            t.CaptureAborted();
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(13)).Number.Should().Be(2);
        }

        [Test]
        public void TheExposureStaysInTheStatusThroughTheDownloadUntilAbortedSoAbortStaysPossible() {
            var t = new ExposureTracker();
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter);
            t.View(Shutter.AddSeconds(305), Take).Exposure.StartedAt.Should().Be(Shutter);
            t.Pending(Shutter.AddSeconds(305)).Message.Should().Be(PendingTiming.AfterSaving);

            t.CaptureAborted();
            var v = t.View(Shutter.AddSeconds(306), Take);
            v.Exposure.Should().BeNull();
            v.Activity.Should().Be(Activities.StartingExposure);
        }

        [Test]
        public void AfterAnAbortThePreviewStartsNowInsteadOfAtTheAbortedExposuresEnd() {
            var t = new ExposureTracker();
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter);
            var now = Shutter.AddSeconds(60);
            var during = t.View(now, Take).Exposure;
            var live = new LiveImaging { TargetId = 7, TargetName = "M31", PlanId = during.PlanId, Filter = during.Filter, EndsAt = during.EndsAt };
            PreviewBuilder.LiveStart(now, live).Should().Be(Shutter.AddSeconds(300));
            PreviewBuilder.LiveAssumption(now, live, true).Text.Should().Contain("counts that exposure as acquired");

            t.CaptureAborted();
            var after = t.View(now.AddSeconds(1), Take).Exposure;
            after.Should().BeNull();
            var idle = new LiveImaging { TargetId = 7, TargetName = "M31", PlanId = after?.PlanId, Filter = after?.Filter, EndsAt = after?.EndsAt };
            PreviewBuilder.LiveStart(now.AddSeconds(1), idle).Should().Be(now.AddSeconds(1));
            PreviewBuilder.LiveAssumption(now.AddSeconds(1), idle, true).Text.Should().StartWith("No exposure is running");
        }

        private static DateTime CentralNight(DateTime utc) => DateTime.SpecifyKind(utc.AddHours(-17).Date.AddHours(17), DateTimeKind.Utc);

        [Test]
        public void TheNumberCountsEachTargetsExposuresForTheWholeNight() {
            var t = new ExposureTracker(CentralNight);
            t.PlanStarted(130);
            for (var i = 0; i < 5; i++) {
                t.ShutterOpened(773, "Red", 600, Shutter.AddMinutes(i * 11));
                t.CaptureEnded();
            }
            t.PlanEnded();

            t.PlanStarted(130);
            t.ShutterOpened(774, "Green", 600, Shutter.AddMinutes(60)).Number.Should().Be(6);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(128);
            t.ShutterOpened(800, "Lum", 300, Shutter.AddMinutes(75)).Number.Should().Be(1);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(130);
            t.ShutterOpened(775, "Blue", 600, Shutter.AddMinutes(90)).Number.Should().Be(7);
        }

        [Test]
        public void TheNumberStartsAgainOnTheNextNight() {
            var t = new ExposureTracker(CentralNight);
            t.PlanStarted(130);
            t.ShutterOpened(773, "Red", 600, Shutter);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(130);
            t.ShutterOpened(773, "Red", 600, Shutter.AddHours(20)).Number.Should().Be(1);
        }

        [Test]
        public void TheLocalNightTurnsAtNoon() {
            var morning = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var evening = new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var noon10 = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var noon11 = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
            ExposureTracker.LocalNight(morning).Should().Be(noon10);
            ExposureTracker.LocalNight(evening).Should().Be(noon11);
        }

        [Test]
        public void TheNumberContinuesFromTheSubsAlreadyTakenTonightAfterARestart() {
            var asked = new List<(int, DateTime)>();
            var t = new ExposureTracker(CentralNight, (target, since) => {
                asked.Add((target, since));
                return target == 130 ? 5 : 0;
            });
            t.PlanStarted(130);
            t.ShutterOpened(774, "Green", 600, Shutter).Number.Should().Be(6);
            t.CaptureEnded();
            t.PlanStarted(130);
            t.ShutterOpened(775, "Blue", 600, Shutter.AddMinutes(11)).Number.Should().Be(7);
            t.CaptureEnded();
            t.PlanStarted(128);
            t.ShutterOpened(800, "Lum", 300, Shutter.AddMinutes(22)).Number.Should().Be(1);
            asked.Should().Equal((130, CentralNight(Shutter)), (128, CentralNight(Shutter)));
        }

        [Test]
        public void ASeedThatFailsStartsTheCountAtOne() {
            var t = new ExposureTracker(CentralNight, (_, _) => throw new InvalidOperationException("locked"));
            t.PlanStarted(130);
            t.ShutterOpened(774, "Green", 600, Shutter).Number.Should().Be(1);
        }

        [Test]
        public void TheDatabaseCountsTheTargetsSubsSinceTheNightStarted() {
            using var db = new TestDb().WithPlanning();
            var since = new DateTime(2026, 10, 10, 17, 0, 0, DateTimeKind.Utc);
            long at(DateTime d) => new DateTimeOffset(d).ToUnixTimeSeconds();
            db.Exec($@"INSERT INTO acquiredimage (Id, projectId, targetId, acquireddate, filtername, gradingStatus, metadata) VALUES
  (9001, 58, 130, {at(since.AddMinutes(-1))}, 'Red', 0, '{{}}'),
  (9002, 58, 130, {at(since)}, 'Red', 0, '{{}}'),
  (9003, 58, 130, {at(since.AddHours(12))}, 'Green', 1, '{{}}'),
  (9004, 58, 128, {at(since.AddHours(12))}, 'Lum', 0, '{{}}')");
            var sql = new SqlSchedulerDatabase(db.Connect);
            sql.CountAcquired(130, since).Should().Be(2);
            sql.CountAcquired(128, since).Should().Be(1);
            sql.CountAcquired(77, since).Should().Be(0);
        }

        [Test]
        public void ARepeatedPlanStartForTheSameTargetDoesNotResetTheCount() {
            var t = new ExposureTracker();
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter);
            t.CaptureEnded();
            t.PlanEnded();

            t.PlanStarted(7);
            t.PlanStarted(7);
            t.ShutterOpened(31, "Ha", 300, Shutter.AddMinutes(6)).Number.Should().Be(2);
        }

        [Test]
        public void PendingTimingFollowsTheRealApplyPoint() {
            var t = new ExposureTracker();
            t.Pending(Picked).Should().BeEquivalentTo(PendingTiming.At(null, PendingTiming.BeforeNextExposure));

            t.PlanStarted(7);
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
            t.PlanStarted(7);
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

            t.PlanStarted(7);
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
