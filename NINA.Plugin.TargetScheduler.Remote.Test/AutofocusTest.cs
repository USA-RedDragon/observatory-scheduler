using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class AutofocusTest {

        public const string Report = @"{
  ""Version"": 2,
  ""Filter"": ""L"",
  ""AutoFocuserName"": ""Hocus Focus"",
  ""StarDetectorName"": ""Hocus Focus"",
  ""Timestamp"": ""2026-10-12T03:02:10.123-05:00"",
  ""Temperature"": 11.5,
  ""Method"": ""STARHFR"",
  ""Fitting"": ""HYPERBOLIC"",
  ""InitialFocusPoint"": { ""Position"": 5000, ""Value"": 0, ""Error"": 0 },
  ""CalculatedFocusPoint"": { ""Position"": 5041.7, ""Value"": 1.92, ""Error"": 0 },
  ""PreviousFocusPoint"": { ""Position"": 5010, ""Value"": 2.2, ""Error"": 0 },
  ""MeasurePoints"": [
    { ""Position"": 4900, ""Value"": 3.01, ""Error"": 0.11 },
    { ""Position"": 5000, ""Value"": 2.05, ""Error"": 0.05 },
    { ""Position"": 5100, ""Value"": ""NaN"", ""Error"": 0.1 },
    { ""Position"": 5200, ""Value"": 2.95, ""Error"": 0.12 }
  ],
  ""Intersections"": {
    ""TrendLineIntersection"": { ""Position"": 5050, ""Value"": 1.8, ""Error"": 0 },
    ""GaussianMaximum"": { ""Position"": ""NaN"", ""Value"": ""NaN"", ""Error"": 0 },
    ""QuadraticMinimum"": null,
    ""HyperbolicMinimum"": { ""Position"": 5041.7, ""Value"": 1.92, ""Error"": 0 }
  },
  ""Fittings"": { ""Quadratic"": """", ""Hyperbolic"": ""y = 1.92 * cosh(asinh((5041.7 - x) / 120))"", ""Gaussian"": """", ""LeftTrend"": ""y = -0.01 * x + 52"", ""RightTrend"": ""y = 0.009 * x - 44"" },
  ""RSquares"": { ""Quadratic"": ""NaN"", ""Hyperbolic"": 0.987, ""LeftTrend"": 0.95, ""RightTrend"": 0.97 },
  ""BacklashCompensation"": { ""BacklashCompensationModel"": ""OVERSHOOT"", ""BacklashIN"": 0, ""BacklashOUT"": 0 },
  ""Duration"": ""00:02:13.5000000""
}";

        [Test]
        public void ParsesTheNinaReport() {
            var r = AutofocusReport.Parse(Report);
            r.Should().NotBeNull();
            r.Filter.Should().Be("L");
            r.Autofocuser.Should().Be("Hocus Focus");
            r.Temperature.Should().Be(11.5);
            r.Method.Should().Be("STARHFR");
            r.Fitting.Should().Be("HYPERBOLIC");
            r.DurationSeconds.Should().Be(133.5);
            r.InitialPosition.Should().Be(5000);
            r.DurationSeconds.Should().BeGreaterThan(0);
            r.ChosenPosition.Should().Be(5041.7);
            r.ChosenValue.Should().Be(1.92);
            r.Points.Select(p => p.Position).Should().Equal(4900, 5000, 5200);
            r.Points[0].Error.Should().Be(0.11);
            using var fit = JsonDocument.Parse(r.Fit);
            fit.RootElement.GetProperty("r_squared").GetProperty("Hyperbolic").GetDouble().Should().Be(0.987);
            fit.RootElement.GetProperty("r_squared").TryGetProperty("Quadratic", out _).Should().BeFalse();
            fit.RootElement.GetProperty("fittings").GetProperty("Hyperbolic").GetString().Should().StartWith("y = 1.92");
            fit.RootElement.GetProperty("intersections").GetProperty("HyperbolicMinimum").GetProperty("Position").GetDouble().Should().Be(5041.7);
            fit.RootElement.GetProperty("intersections").GetProperty("GaussianMaximum").ToString().Should().Be(@"{""Error"":0}");
            fit.RootElement.GetProperty("intersections").TryGetProperty("QuadraticMinimum", out _).Should().BeFalse();
            fit.RootElement.GetProperty("fittings").TryGetProperty("Quadratic", out _).Should().BeFalse();
            r.Fit.Should().NotContain("NaN").And.NotContain("null");
        }

        [Test]
        public void BadReportsParseToNothing() {
            AutofocusReport.Parse(null).Should().BeNull();
            AutofocusReport.Parse("").Should().BeNull();
            AutofocusReport.Parse("not json").Should().BeNull();
            AutofocusReport.Parse("[1,2]").Should().BeNull();
            var r = AutofocusReport.Parse(@"{""Temperature"": ""NaN"", ""Duration"": ""junk""}");
            r.Temperature.Should().BeNull();
            r.DurationSeconds.Should().BeNull();
            r.Points.Should().BeEmpty();
            r.Fit.Should().BeNull();
        }

        private static AutofocusCompletion Done(bool success, string error = null) {
            return new AutofocusCompletion {
                Success = success,
                Error = error,
                FinishedAt = new DateTime(2026, 10, 12, 8, 2, 15, DateTimeKind.Utc),
                Filter = "Ha",
                Temperature = 10.25,
                Position = 5042,
                ProfileId = "profile",
                TargetName = "M31",
                TargetGuid = "g31",
            };
        }

        [Test]
        public void SuccessfulRunTakesTheReport() {
            var previous = new AutofocusRun { Success = true, ChosenPosition = 5010, ChosenValue = 2.2 };
            var run = AutofocusRuns.Build(Done(true), AutofocusReport.Parse(Report), previous);
            Guid.TryParse(run.Id, out _).Should().BeTrue();
            run.Success.Should().BeTrue();
            run.Error.Should().BeNull();
            run.FinishedAt.Should().Be(new DateTime(2026, 10, 12, 8, 2, 15, DateTimeKind.Utc));
            run.StartedAt.Should().Be(new DateTime(2026, 10, 12, 8, 0, 1, 500, DateTimeKind.Utc));
            run.DurationSeconds.Should().Be(133.5);
            run.Filter.Should().Be("L");
            run.Temperature.Should().Be(11.5);
            run.Autofocuser.Should().Be("Hocus Focus");
            run.ChosenPosition.Should().Be(5041.7);
            run.ChosenValue.Should().Be(1.92);
            run.PreviousPosition.Should().Be(5010);
            run.InitialPosition.Should().Be(5000);
            run.Points.Should().HaveCount(3);
            run.TargetName.Should().Be("M31");
            run.TargetGuid.Should().Be("g31");
            run.ProfileId.Should().Be("profile");
            run.Source.Should().Be(AutofocusReport.SourceReport);
            AutofocusRuns.Build(Done(true), AutofocusReport.Parse(Report)).Id.Should().NotBe(run.Id);
            var same = Done(false, "x");
            same.RunId = "fixed";
            AutofocusRuns.Build(same, null).Id.Should().Be("fixed");
        }

        [Test]
        public void ReanalysisHasNoDuration() {
            AutofocusRuns.IsReanalysis(null).Should().BeFalse();
            AutofocusRuns.IsReanalysis(AutofocusReport.Parse(Report)).Should().BeFalse();
            AutofocusRuns.IsReanalysis(AutofocusReport.Parse(@"{""Duration"": ""00:00:00""}")).Should().BeTrue();
            AutofocusRuns.IsReanalysis(AutofocusReport.Parse(@"{""Filter"": ""L""}")).Should().BeFalse();
        }

        [Test]
        public void SuccessWithoutAReportUsesTheBroadcast() {
            var run = AutofocusRuns.Build(Done(true), null);
            run.Source.Should().Be(AutofocusReport.SourceEvent);
            run.Filter.Should().Be("Ha");
            run.Temperature.Should().Be(10.25);
            run.ChosenPosition.Should().Be(5042);
            run.ChosenValue.Should().BeNull();
            run.StartedAt.Should().BeNull();
            run.PreviousPosition.Should().BeNull();
            run.DurationSeconds.Should().BeNull();
            run.Points.Should().BeEmpty();
            run.Fit.Should().BeNull();
        }

        [Test]
        public void FailedRunHasNoChosenFocus() {
            var run = AutofocusRuns.Build(Done(false, "no successful-autofocus broadcast followed this report"), AutofocusReport.Parse(Report));
            run.Success.Should().BeFalse();
            run.Error.Should().Be("no successful-autofocus broadcast followed this report");
            run.ChosenPosition.Should().BeNull();
            run.ChosenValue.Should().BeNull();
            run.Points.Should().HaveCount(3);
            run.Fit.Should().NotBeNull();
            var status = AutofocusRuns.Status(run);
            status.State.Should().Be(AutofocusStates.Failed);
            JsonSerializer.Serialize(status, Json.Options).Should().NotContain("chosen_position").And.Contain(@"""error"":""no successful");
        }

        [Test]
        public void StatusCarriesTheLastRun() {
            var latest = new AutofocusLatest();
            var changes = 0;
            latest.Changed += () => changes++;
            latest.Snapshot().Should().BeNull();
            var run = AutofocusRuns.Build(Done(true), AutofocusReport.Parse(Report));
            latest.Record(run);
            changes.Should().Be(1);
            var s = latest.Snapshot();
            s.RunId.Should().Be(run.Id);
            s.State.Should().Be(AutofocusStates.Finished);
            s.ChosenPosition.Should().Be(5041.7);
            s.Points.Add(new AutofocusPoint());
            latest.Snapshot().Points.Should().HaveCount(3);
            var json = JsonSerializer.Serialize(new Status { Autofocus = latest.Snapshot() }, Json.Options);
            json.Should().Contain(@"""autofocus"":{""run_id"":""" + run.Id + @""",""state"":""finished"",""started_at"":""2026-10-12T08:00:01.500Z"",""finished_at"":""2026-10-12T08:02:15.000Z"",""filter"":""L""");
            json.Should().Contain(@"""points"":[{""position"":4900,""value"":3.01,""error"":0.11}");
            json.Should().Contain(@"""fit"":{""fittings"":{");
        }

        [Test]
        public void ThrowingListenerNeverReachesTheCaller() {
            var latest = new AutofocusLatest();
            latest.Changed += () => throw new InvalidOperationException("boom");
            latest.Invoking(x => x.Record(AutofocusRuns.Build(Done(true), null))).Should().NotThrow();
        }

        [Test]
        public void PointsAreCapped() {
            var report = new AutofocusResult();
            for (int i = 0; i < AutofocusRuns.MaxPoints + 10; i++) {
                report.Points.Add(new AutofocusPoint { Position = i, Value = 1 });
            }
            AutofocusRuns.Build(Done(true), report).Points.Should().HaveCount(AutofocusRuns.MaxPoints);
        }

        [Test]
        public void ReportsAreClaimedOnce() {
            var now = new DateTime(2026, 10, 12, 8, 2, 15, DateTimeKind.Utc);
            const string me = "2b5bb7a6-6f0e-4b43-9a59-6f3e8a2a7f10";
            const string other = "0c1d2e3f-0000-4000-8000-000000000001";
            var window = TimeSpan.FromSeconds(60);
            var reports = new AutofocusReports();
            string newest = $@"C:\af\2026-10-12--03-02-14--{me}.json";
            string older = $@"C:\af\2026-10-12--03-01-30--{me}.json";
            string stale = $@"C:\af\2026-10-12--02-00-00--{me}.json";
            string foreign = $@"C:\af\2026-10-12--03-02-15--{other}.json";
            var files = new[] { (newest, now.AddSeconds(-1)), (stale, now.AddHours(-1)), (older, now.AddSeconds(-45)), (foreign, now) };

            reports.ClaimForSuccess(files, now, window, me).Should().Be((newest, (string)null));
            reports.ClaimForFailure(newest).Should().BeNull();
            reports.ClaimForSuccess(files, now, window, me).Should().Be(((string)null, (string)null));

            var failedId = reports.ClaimForFailure(older);
            Guid.TryParse(failedId, out _).Should().BeTrue();
            reports.ClaimForSuccess(files.Skip(1), now, window, me).Should().Be((older, failedId));
            reports.ClaimForSuccess(files.Skip(1), now, window, me).Should().Be(((string)null, (string)null));
            reports.ClaimForSuccess(new[] { (stale, now.AddHours(-1)) }, now, window, me).Should().Be(((string)null, (string)null));
            reports.ClaimForFailure(stale).Should().NotBeNull();
            reports.ClaimForFailure(null).Should().BeNull();

            var inspector = @"C:\af\2026-10-12--03-05-00.json";
            reports.ClaimForSuccess(new[] { (inspector, now), (foreign, now) }, now, window, me).Should().Be((inspector, (string)null));
        }

        [Test]
        public void ReportsBelongToTheirProfile() {
            const string me = "2b5bb7a6-6f0e-4b43-9a59-6f3e8a2a7f10";
            AutofocusReports.ProfileOf($@"C:\af\2026-10-12--03-02-14--{me}.json").Should().Be(me);
            AutofocusReports.ProfileOf(@"C:\af\2026-10-12--03-02-14.json").Should().BeNull();
            AutofocusReports.Mine($@"C:\af\2026-10-12--03-02-14--{me.ToUpperInvariant()}.json", me).Should().BeTrue();
            AutofocusReports.Mine(@"C:\af\2026-10-12--03-02-14.json", me).Should().BeFalse();
            AutofocusReports.Mine(@"C:\af\2026-10-12--03-02-14--0c1d2e3f-0000-4000-8000-000000000001.json", me).Should().BeFalse();
            AutofocusReports.Mine(@"C:\af\x.json", null).Should().BeTrue();
        }
    }

    [TestFixture]
    public class AutofocusStoreTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        [Test]
        public void StartupCreatesTheTableOnce() {
            var store = new SqlCommandStore(db.Connect);
            store.EnsureSchema();
            store.EnsureSchema();
            db.Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'ts_autofocus_run'").Should().Be(1);
            var columns = new List<string>();
            using (var conn = db.Connect())
            using (var cmd = conn.CreateCommand()) {
                cmd.CommandText = "PRAGMA table_info(ts_autofocus_run)";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { columns.Add(r.GetString(1)); }
            }
            columns.Should().Equal("id", "profile_id", "started_at", "finished_at", "duration_seconds", "success", "error", "filter", "temperature", "autofocuser", "method", "fitting",
                "initial_position", "previous_position", "previous_value", "chosen_position", "chosen_value", "points", "fit", "target_name", "target_guid", "source", "recorded_at");
        }

        [Test]
        public void SavesOneRowPerRun() {
            var store = new AutofocusStore(db.Connect);
            store.Recent(10).Should().BeEmpty();
            var start = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
            var report = AutofocusReport.Parse(AutofocusTest.Report);
            store.PreviousSuccess(start).Should().BeNull();
            var earlier = AutofocusRuns.Build(new AutofocusCompletion { Success = true, FinishedAt = start.AddHours(-2), Position = 5010 }, null);
            store.Save(earlier, start.AddHours(-2));
            var failed = AutofocusRuns.Build(new AutofocusCompletion { Success = false, Error = "x", FinishedAt = start.AddHours(-1) }, report);
            store.Save(failed, start.AddHours(-1));
            var previous = store.PreviousSuccess(start);
            previous.Id.Should().Be(earlier.Id);
            previous.ChosenPosition.Should().Be(5010);
            var run = AutofocusRuns.Build(new AutofocusCompletion { Success = true, FinishedAt = start.AddSeconds(135), ProfileId = "profile", TargetName = "M31", TargetGuid = "g31" }, report, previous);
            store.Save(run, start.AddSeconds(136));
            store.Save(run, start.AddSeconds(137));

            db.Count("SELECT COUNT(*) FROM ts_autofocus_run").Should().Be(3);
            db.Count("SELECT COUNT(*) FROM ts_autofocus_run WHERE started_at IS NULL").Should().Be(1);
            db.Count("SELECT COUNT(*) FROM ts_autofocus_run WHERE started_at = '2026-10-12 08:00:01.500' AND finished_at = '2026-10-12 08:02:15.000' AND recorded_at = '2026-10-12 08:02:17.000' AND success = 1").Should().Be(1);
            var got = store.Recent(10).First();
            got.Id.Should().Be(run.Id);
            got.ProfileId.Should().Be("profile");
            got.StartedAt.Should().Be(start.AddSeconds(1.5));
            got.ChosenPosition.Should().Be(5041.7);
            got.ChosenValue.Should().Be(1.92);
            got.PreviousPosition.Should().Be(5010);
            got.Temperature.Should().Be(11.5);
            got.Source.Should().Be("report");
            got.TargetGuid.Should().Be("g31");
            got.Points.Select(p => p.Position).Should().Equal(4900, 5000, 5200);
            got.Fit.Should().Contain("r_squared");

            using var conn = db.Connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT points FROM ts_autofocus_run WHERE id = '" + run.Id + "'";
            ((string)cmd.ExecuteScalar()).Should().Be(@"[{""position"":4900,""value"":3.01,""error"":0.11},{""position"":5000,""value"":2.05,""error"":0.05},{""position"":5200,""value"":2.95,""error"":0.12}]");
        }

        [Test]
        public void ALateSuccessReplacesTheFailedRow() {
            var store = new AutofocusStore(db.Connect);
            var report = AutofocusReport.Parse(AutofocusTest.Report);
            var at = new DateTime(2026, 10, 12, 8, 2, 15, DateTimeKind.Utc);
            var failed = AutofocusRuns.Build(new AutofocusCompletion { RunId = "r1", Success = false, Error = "x", FinishedAt = at }, report);
            store.Save(failed, at);
            var won = AutofocusRuns.Build(new AutofocusCompletion { RunId = "r1", Success = true, FinishedAt = at, Position = 5042 }, report);
            store.Save(won, at.AddSeconds(30));
            db.Count("SELECT COUNT(*) FROM ts_autofocus_run").Should().Be(1);
            var got = store.Recent(5).Single();
            got.Success.Should().BeTrue();
            got.Error.Should().BeNull();
            got.ChosenPosition.Should().Be(5041.7);
        }
    }
}
