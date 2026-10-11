using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    [NonParallelizable]
    public class PanelHistoryReaderTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            SqlPanelHistoryReader.Forget();
            db = new TestDb().WithPlanning();
            db.Exec(@"UPDATE project SET isMosaic = 1 WHERE Id = 5;
INSERT INTO acquiredimage (Id, projectId, targetId, acquireddate, filtername, gradingStatus, metadata) VALUES
 (1, 5, 12, 1760000000, 'L', 1, '{""ExposureDuration"":300.0,""FilterName"":""L""}'),
 (2, 5, 12, 1760000400, 'L', 2, '{""ExposureDuration"":300.0}'),
 (3, 5, 13, 1760000800, 'Ha', 0, '{""ExposureDuration"":600}'),
 (4, 7, 14, 1760001000, 'L', 1, '{""ExposureDuration"":120}'),
 (5, 5, 13, 1760001200, 'Ha', 1, 'not json');");
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
            SqlPanelHistoryReader.Forget();
        }

        private PanelHistory Load() {
            using var conn = db.Connect();
            return new SqlPanelHistoryReader(conn).Load();
        }

        [Test]
        public void LoadsKeptSubsOfActiveMosaicsWithTheirExposureLength() {
            PanelHistory h = Load();

            h.Count.Should().Be(2);
            PanelExposure l = h.For(12).Should().ContainSingle().Subject;
            l.Filter.Should().Be("L");
            l.Seconds.Should().Be(300);
            l.At.Should().Be(new DateTime(2025, 10, 9, 8, 53, 20, DateTimeKind.Utc));
            l.At.Kind.Should().Be(DateTimeKind.Utc);
            h.For(13).Should().ContainSingle().Which.Seconds.Should().Be(600);
            h.For(14).Should().BeEmpty();
        }

        [Test]
        public void ReloadUsesCachedLengthsAndPicksUpNewAndNewlyEligibleSubs() {
            Load().Count.Should().Be(2);

            db.Exec(@"INSERT INTO acquiredimage (Id, projectId, targetId, acquireddate, filtername, gradingStatus, metadata) VALUES (6, 5, 12, 1760002000, 'R', 1, '{""ExposureDuration"":180}');
UPDATE acquiredimage SET metadata = '{}' WHERE Id = 1;
UPDATE project SET isMosaic = 1 WHERE Id = 7;");
            PanelHistory h = Load();

            h.For(12).Select(e => e.Seconds).Should().BeEquivalentTo(new[] { 300.0, 180.0 });
            h.For(14).Should().ContainSingle().Which.Seconds.Should().Be(120);
        }

        [Test]
        public void RejectionsAfterTheFirstLoadDropOut() {
            Load().For(13).Should().HaveCount(1);
            db.Exec("UPDATE acquiredimage SET gradingStatus = 2 WHERE Id = 3");
            Load().For(13).Should().BeEmpty();
        }

        [Test]
        public void ExposureSecondsReadsTheMetadataDuration() {
            SqlPanelHistoryReader.ExposureSeconds("{\"ExposureDuration\":240.5}").Should().Be(240.5);
            SqlPanelHistoryReader.ExposureSeconds("{\"ExposureDuration\":\"x\"}").Should().Be(0);
            SqlPanelHistoryReader.ExposureSeconds("[]").Should().Be(0);
            SqlPanelHistoryReader.ExposureSeconds("{").Should().Be(0);
            SqlPanelHistoryReader.ExposureSeconds(null).Should().Be(0);
        }
    }
}
