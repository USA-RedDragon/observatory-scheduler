using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class CalibrationActivityTest {
        private static readonly DateTime T0 = new DateTime(2026, 10, 16, 3, 0, 0, DateTimeKind.Utc);
        private DateTime now;
        private CalibrationActivity a;

        [SetUp]
        public void SetUp() {
            now = T0;
            a = new CalibrationActivity(() => now);
        }

        private static StatusCalibrationQueueItem Dark(string key, double exposure, double setTemp, int remaining, int total = 25) {
            return new StatusCalibrationQueueItem {
                Kind = CalibrationActivity.KindDarks,
                ComboKey = key,
                Exposure = exposure,
                Gain = 0,
                Offset = 50,
                Binning = 1,
                SetTemp = setTemp,
                FramesTotal = total,
                FramesHave = total - remaining,
                Remaining = remaining,
            };
        }

        private static CalibrationPlanContext Ctx(DateTime? limit = null) {
            return new CalibrationPlanContext { Overhead = TimeSpan.FromSeconds(10), Move = (x, y) => TimeSpan.FromMinutes(Math.Abs(x - y)), Limit = limit };
        }

        private static StatusCalibrationCamera Cam(double t, double sp) => new StatusCalibrationCamera { Connected = true, Temperature = t, Setpoint = sp, CoolerPower = 40 };

        [Test]
        public void TheDarksStartLineSaysColdestFirstAndNamesTheFirstSet() {
            var items = new List<StatusCalibrationQueueItem> { Dark("c", 300, -21, 20), Dark("w", 600, -10, 25) };
            a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", items, 10, null, Ctx());
            string line = a.Snapshot(null).Log.Last().Text;
            line.Should().Be("Darks start: 2 dark sets, coldest first, starting with 300 s at -21 °C, gain 0.");
            line.Should().NotContain("warmest");
            a.RunStarted(CalibrationActivity.KindDarks, "end_of_night", new List<StatusCalibrationQueueItem> { Dark("c", 300, -21, 20) }, 10, null, Ctx());
            a.Snapshot(null).Log.Last().Text.Should().Be("Darks start: 1 dark set.");
        }

        [Test]
        public void NothingIsReportedBeforeAnyCalibration() {
            a.Snapshot(Cam(20, 20)).Should().BeNull();
            a.Running.Should().BeFalse();
        }

        [Test]
        public void ADarksRunReportsTheSetTheFrameAndThePlan() {
            var items = new List<StatusCalibrationQueueItem> {
                Dark("300|0|50|1|6", 300, 6, 20),
                Dark("600|0|50|1|4", 600, 4, 25),
            };
            a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", items, 18.2, T0.AddHours(9), Ctx(T0.AddHours(9)));
            a.Running.Should().BeTrue();

            a.SetStarted("300|0|50|1|6", 5, 1, s => s.ReadoutMode = "High Gain");
            now = T0.AddSeconds(30);
            a.FrameStarted(6, 300);
            now = T0.AddSeconds(130);

            StatusCalibration s = a.Snapshot(Cam(6, 6));
            s.Kind.Should().Be("darks");
            s.Placement.Should().Be("while_unsafe");
            s.Ambient.Should().Be(18.2);
            s.Set.ComboKey.Should().Be("300|0|50|1|6");
            s.Set.FramesBefore.Should().Be(5);
            s.Set.FramesDone.Should().Be(5);
            s.Set.FramesTotal.Should().Be(25);
            s.Set.Index.Should().Be(1);
            s.Set.Count.Should().Be(2);
            s.Set.ReadoutMode.Should().Be("High Gain");
            s.Frame.Number.Should().Be(6);
            s.Frame.EndsAt.Should().Be(T0.AddSeconds(330));
            s.Queue[0].Status.Should().Be("taking");
            s.Queue[1].Status.Should().Be("next");
            s.Queue[0].PlannedStart.Should().Be(T0.AddSeconds(30));
            s.Queue[0].PlannedEnd.Should().Be(T0.AddSeconds(330 + 19 * 310));
            s.Set.EndsAt.Should().Be(s.Queue[0].PlannedEnd);
            s.Queue[0].FitFrames.Should().Be(20);
            s.Queue[1].PlannedStart.Should().Be(s.Queue[0].PlannedEnd.Value.AddMinutes(2));

            a.FrameTaken(6);
            s = a.Snapshot(Cam(6, 6));
            s.Frame.Should().BeNull();
            s.Set.FramesDone.Should().Be(6);
            s.Queue[0].Remaining.Should().Be(19);
            s.Taken.Should().ContainSingle().Which.Frames.Should().Be(1);
        }

        [Test]
        public void SetsCutAtTheSunLimitFitFewerFrames() {
            var items = new List<StatusCalibrationQueueItem> { Dark("a", 600, 4, 25) };
            items[0].Status = CalibrationActivity.Next;
            CalibrationPlan.Fill(items, T0, 4, Ctx(T0.AddMinutes(61)), null);
            items[0].FitFrames.Should().Be(6);
            items[0].PlannedEnd.Should().Be(T0.AddSeconds(25 * 610));
        }

        [Test]
        public void SkippedSetsKeepTheirReasonAfterTheRunEnds() {
            var items = new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 1), Dark("b", 300, 3, 25), Dark("c", 300, -19, 25) };
            a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", items, 18, null, Ctx());
            a.SetStarted("a", 24, 1);
            a.FrameStarted(25, 300);
            now = T0.AddMinutes(5);
            a.FrameTaken(25);
            a.SetFinished("a", CalibrationActivity.Done);
            a.SetFinished("b", CalibrationActivity.Skipped, "sun", "a 300 s dark would end at 07:10:00");
            a.SetFinished("c", CalibrationActivity.Skipped, "cooler_capacity", "-19 °C is 37 °C below the 18 °C ambient");
            a.RunEnded(null, null);

            a.Running.Should().BeFalse();
            StatusCalibration s = a.Snapshot(Cam(-11.6, -11.6));
            s.Kind.Should().Be("done");
            s.Stop.Code.Should().Be("finished");
            s.Queue.Select(q => q.Status).Should().Equal("done", "skipped", "skipped");
            s.Queue[2].ReasonCode.Should().Be("cooler_capacity");
            s.Queue.All(q => q.PlannedStart == null).Should().BeTrue();
            s.Taken.Should().ContainSingle().Which.EndedAt.Should().Be(T0.AddMinutes(5));
            s.Log.Select(l => l.Text).Should().Contain("Not taking 300 s at -19 °C, gain 0: -19 °C is 37 °C below the 18 °C ambient.");
        }

        [Test]
        public void AStopLeavesTheOpenSetsWaitingWithTheStopReason() {
            a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 20), Dark("b", 300, 3, 25) }, 18, null, Ctx());
            a.SetStarted("a", 5, 1);
            a.RunEnded("safe", "it is safe again");
            StatusCalibration s = a.Snapshot(null);
            s.Stop.Code.Should().Be("safe");
            s.Queue.Select(q => q.Status).Should().Equal("waiting", "waiting");
            s.Queue[1].ReasonCode.Should().Be("stopped");
            s.Log.Last().Text.Should().Be("Darks stopped: it is safe again.");
        }

        [Test]
        public void ARunThatCannotStartLogsOncePerCause() {
            a.RunNotStarted(CalibrationActivity.KindDarks, "while_unsafe", "sun", "the sun is at -5.1°", 18, null);
            a.RunNotStarted(CalibrationActivity.KindDarks, "while_unsafe", "sun", "the sun is at -5.4°", 18, null);
            StatusCalibration s = a.Snapshot(null);
            s.Kind.Should().Be("done");
            s.Stop.Reason.Should().Be("the sun is at -5.4°");
            s.Log.Should().ContainSingle().Which.Text.Should().Be("Darks not taken: the sun is at -5.1°.");
        }

        [Test]
        public void RepeatedQuietRunsLogOnce() {
            for (int i = 0; i < 3; i++) {
                now = T0.AddMinutes(i);
                a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", new List<StatusCalibrationQueueItem> { Dark("b", 300, 3, 25) }, 18, null, Ctx());
                a.SetFinished("b", CalibrationActivity.Skipped, "sun", $"would end at 07:0{i}");
                a.RunEnded(null, null);
            }
            a.Snapshot(null).Log.Count.Should().Be(3);
        }

        [Test]
        public void TheSequenceCoolDownAfterAPassedDarksCheckIsForDarks() {
            var planned = new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 20) };
            planned[0].Status = CalibrationActivity.Next;
            a.DarksChecked(true, null, planned, 18.4, T0.AddHours(9));
            now = T0.AddMinutes(1);
            a.SequenceCoolingSeen(-11.6, 34.6, 23, 23, Ctx());
            now = T0.AddMinutes(2);
            a.SequenceCoolingSeen(-11.6, 34.6, 21.5, 22, Ctx());

            a.Running.Should().BeTrue();
            StatusCalibration s = a.Snapshot(Cam(21.5, 22));
            s.Kind.Should().Be("cooling");
            s.Purpose.Should().Be("darks");
            s.Cooling.Target.Should().Be(-11.6);
            s.Cooling.From.Should().Be(23);
            s.Cooling.Source.Should().Be("sequence");
            s.Cooling.RateCPerMin.Should().Be(1);
            s.Cooling.EndsAt.Should().Be(now.AddMinutes(33.6));
            s.Queue.Should().ContainSingle().Which.PlannedStart.Should().Be(s.Cooling.EndsAt.Value.AddMinutes(17.6));
            s.Log.Select(l => l.Text).Should().Equal(
                "Darks check passed: 1 dark set is wanted and at least one fits before dawn.",
                "Cooling the camera to -11.6 °C for darks.");

            a.SequenceCoolingEnded(-11.6);
            a.Running.Should().BeFalse();
            a.Snapshot(null).Should().BeNull();
        }

        [Test]
        public void ADarksCheckIsUsedUpByTheRunItGated() {
            var planned = new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 20) };
            planned[0].Status = CalibrationActivity.Next;
            a.DarksChecked(true, null, planned, 18.4, T0.AddMinutes(30));
            a.SequenceCoolingSeen(-11.6, 34.6, 23, 23, Ctx());
            a.Snapshot(null).Queue.Single().FitFrames.Should().BeLessThan(20);
            a.SequenceCoolingEnded(-11.6);
            a.RunStarted(CalibrationActivity.KindDarks, "while_unsafe", planned, 18, null, Ctx());
            a.SetStarted("a", 24, 1);
            a.FrameTaken(25);
            a.RunEnded("safe", "it is safe again");
            a.Snapshot(null).Queue.Single().Status.Should().Be("done");

            now = T0.AddMinutes(40);
            a.SequenceCoolingSeen(-10, 3, -7, -7, Ctx());
            a.Running.Should().BeFalse();
            StatusCalibration s = a.Snapshot(null, out bool running);
            running.Should().BeFalse();
            s.Purpose.Should().Be("imaging");
            s.Queue.Single().PlannedStart.Should().BeNull();
        }

        [Test]
        public void ACoolDownWithoutADarksCheckIsForImaging() {
            a.SequenceCoolingSeen(-10, 30, 20, 20, Ctx());
            StatusCalibration s = a.Snapshot(null);
            s.Purpose.Should().Be("imaging");
            s.Queue.Should().BeEmpty();
            a.Running.Should().BeFalse();
        }

        [Test]
        public void CoolingRateFallsBackToThePlannedRampUntilTheSetpointHasMoved() {
            CoolingEstimate.Rate(new List<(DateTime, double)> { (T0, 23) }, 23, -7, 30).Should().Be(1);
            CoolingEstimate.Rate(new List<(DateTime, double)> { (T0, 23), (T0.AddMinutes(2), 22) }, 23, -7, 30).Should().Be(0.5);
            CoolingEstimate.EndsAt(T0, 22, -7, 0.5).Should().Be(T0.AddMinutes(58));
            CoolingEstimate.EndsAt(T0, null, -7, 0.5).Should().BeNull();
        }

        [Test]
        public void TakenSetsStartOverOnANewNight() {
            a.RunStarted(CalibrationActivity.KindDarks, "end_of_night", new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 20) }, 18, null, Ctx());
            a.SetStarted("a", 5, 1);
            a.FrameTaken(6);
            a.RunEnded(null, null);
            a.Snapshot(null).Taken.Should().HaveCount(1);
            now = T0.AddHours(30);
            a.Snapshot(null).Taken.Should().BeEmpty();
        }

        [Test]
        public void FlatsReportTheTargetFilterAndPanel() {
            var items = new List<StatusCalibrationQueueItem> {
                new StatusCalibrationQueueItem { Kind = CalibrationActivity.KindFlats, ComboKey = "flat:1", Filter = "Luminance", TargetName = "M 15", Exposure = 3, FramesTotal = 30, Remaining = 30 },
                new StatusCalibrationQueueItem { Kind = CalibrationActivity.KindFlats, ComboKey = "flat:2", Filter = "Blue", TargetName = "Barnard's E", Exposure = 5, FramesTotal = 30, Remaining = 30 },
            };
            a.RunStarted(CalibrationActivity.KindFlats, "end_of_night", items, null, null, new CalibrationPlanContext { Overhead = TimeSpan.FromSeconds(1) });
            a.SetStarted("flat:2", 0, 2, s => { s.PanelBrightness = 48; s.FramesTotal = 30; });
            a.FrameStarted(1, 5);
            a.FrameTaken(1);
            a.FrameStarted(2, 5);
            StatusCalibration st = a.Snapshot(null);
            st.Kind.Should().Be("flats");
            st.Set.TargetName.Should().Be("Barnard's E");
            st.Set.Filter.Should().Be("Blue");
            st.Set.PanelBrightness.Should().Be(48);
            st.Set.FramesDone.Should().Be(1);
            st.Frame.Number.Should().Be(2);
            st.Queue[0].Status.Should().Be("next");
            st.Taken.Single().PanelBrightness.Should().Be(48);
            CalibrationText.Describe(st.Queue[1]).Should().Be("Blue flats for Barnard's E");
        }

        [Test]
        public void TheStatusJsonUsesSnakeCaseAndOmitsMissingValues() {
            a.RunStarted(CalibrationActivity.KindDarks, "before_wait", new List<StatusCalibrationQueueItem> { Dark("a", 300, 6, 20) }, null, null, Ctx());
            var status = new Status { State = "calibrating", Calibration = a.Snapshot(new StatusCalibrationCamera { Connected = false }) };
            string json = JsonSerializer.Serialize(status, Json.Options);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement c = doc.RootElement.GetProperty("calibration");
            c.GetProperty("kind").GetString().Should().Be("darks");
            c.GetProperty("sun_limit_degrees").GetDouble().Should().Be(-8);
            c.GetProperty("queue")[0].GetProperty("combo_key").GetString().Should().Be("a");
            c.GetProperty("queue")[0].GetProperty("planned_start").GetDateTime().Should().Be(T0);
            c.TryGetProperty("ambient", out _).Should().BeFalse();
            c.TryGetProperty("set", out _).Should().BeFalse();
            c.GetProperty("camera").TryGetProperty("temperature", out _).Should().BeFalse();
        }
    }
}
