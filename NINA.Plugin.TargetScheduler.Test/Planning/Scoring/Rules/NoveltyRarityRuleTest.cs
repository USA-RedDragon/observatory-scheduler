using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Database;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Scoring.Rules {

    [TestFixture]
    public class NoveltyRarityRuleTest {

        private class Reader : ITargetSeasonReader {
            public List<TargetSeasonRow> Rows { get; } = new List<TargetSeasonRow>();
            public int Loads { get; private set; }

            public IList<TargetSeasonRow> LoadAll() {
                Loads++;
                return Rows;
            }
        }

        private Reader reader;
        private static readonly DateTime Today = new DateTime(2026, 10, 9);

        [SetUp]
        public void SetUp() {
            reader = new Reader();
            SeasonWorker.ReaderFactory = () => reader;
            SeasonWorker.Store.Clear();
            RarityRule.Today = () => Today;
        }

        [TearDown]
        public void TearDown() {
            MosaicCompletion.GoalSource = null;
            SeasonWorker.ReaderFactory = ForkDatabase.TargetSeasonReader;
            SeasonWorker.Store.Clear();
            RarityRule.Today = () => DateTime.Now.Date;
        }

        [Test]
        public void RuleIdentity() {
            new NoveltyRule().Name.Should().Be("Novelty");
            new NoveltyRule().DefaultWeight.Should().Be(10);
            new RarityRule().Name.Should().Be("Rarity");
            new RarityRule().DefaultWeight.Should().Be(20);
            ScoringRule.GetAllScoringRules().Should().ContainKeys("Novelty", "Rarity");
            new NoveltyRule().Should().BeAssignableTo<IOptInScoringRule>();
            new RarityRule().Should().BeAssignableTo<IOptInScoringRule>();
            new PercentCompleteRule().Should().NotBeAssignableTo<IOptInScoringRule>();
        }

        [Test]
        public void MissingWeightsLeaveTheNewRulesOff() {
            Dictionary<string, double> old = new Dictionary<string, double>();
            foreach (string name in ScoringRule.GetAllScoringRules().Keys) {
                if (name != NoveltyRule.RULE_NAME && name != RarityRule.RULE_NAME) { old[name] = 1; }
            }

            ScoringEngine engine = new ScoringEngine(null, null, DateTime.Now, null);
            List<IScoringRule> rules = engine.AddAllRules(old);
            rules.Should().NotContain(r => r.Name == NoveltyRule.RULE_NAME || r.Name == RarityRule.RULE_NAME);
        }

        [Test]
        public void NoveltyCountBased() {
            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("t", TestData.M42);
            target.SetupProperty(m => m.Guid, "t1");
            PlanMocks.AddMockPlanFilter(target, PlanMocks.GetMockPlanExposure("L", 10, 2, 600, 1));
            PlanMocks.AddMockPlanFilter(target, PlanMocks.GetMockPlanExposure("R", 10, 6, 600, 2));
            new NoveltyRule().Score(PlanMocks.GetMockScoringEnging().Object, target.Object).Should().BeApproximately(0.8 * 0.8, 0.0001);

            target.Object.ExposurePlans.ForEach(e => e.Accepted = 1);
            new NoveltyRule().Score(PlanMocks.GetMockScoringEnging().Object, target.Object).Should().BeApproximately(0.9, 0.0001);
        }

        [Test]
        public void NoveltyGoalDriven() {
            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("t", TestData.M42);
            target.SetupProperty(m => m.Guid, "t1");
            PlanMocks.AddMockPlanFilter(target, PlanMocks.GetMockPlanExposure("Ha", 10, 50, 300, 1));
            PlanMocks.AddMockPlanFilter(target, PlanMocks.GetMockPlanExposure("OIII", 10, 50, 300, 2));
            Mock<IGoalProgressSource> source = new Mock<IGoalProgressSource>();
            source.Setup(m => m.Get("t1", "Ha")).Returns(new GoalProgress { Progress = 0.7, EffectiveHours = 4 });
            source.Setup(m => m.Get("t1", "OIII")).Returns(new GoalProgress { Progress = 0.3, EffectiveHours = 0.5 });
            MosaicCompletion.GoalSource = source.Object;

            new NoveltyRule().Score(PlanMocks.GetMockScoringEnging().Object, target.Object).Should().BeApproximately(0.7, 0.0001);
        }

        private static (IScoringEngine engine, Mock<ITarget> target) RarityCase(Coordinates coords, DateTime atTime, double minAlt, int minTime) {
            Mock<IScoringEngine> engine = PlanMocks.GetMockScoringEnging();
            engine.SetupProperty(m => m.ActiveProfile, PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile);
            engine.SetupProperty(m => m.AtTime, atTime);

            Mock<IProject> project = PlanMocks.GetMockPlanProject("p", ProjectState.Active);
            project.SetupProperty(m => m.MinimumAltitude, minAlt);
            project.SetupProperty(m => m.HorizonDefinition, new HorizonDefinition(minAlt));
            project.SetupProperty(m => m.MinimumTime, minTime);

            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("t", coords);
            target.SetupProperty(m => m.Guid, "guid-" + coords.RA);
            target.SetupProperty(m => m.DatabaseId, (int)(coords.RA * 1000));
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            exposure.SetupProperty(m => m.TwilightLevel, TwilightLevel.Nighttime);
            PlanMocks.AddMockPlanFilter(target, exposure);
            PlanMocks.AddMockPlanTarget(project, target);
            return (engine.Object, target);
        }

        private static void Put(IScoringEngine engine, ITarget target, int nights, bool outOfSeason, DateTime computedFor) {
            SeasonWorker.Store.Put(new SeasonEntry {
                TargetGuid = target.Guid,
                Key = RarityRule.Key(engine, target),
                Season = new SeasonResult { NightsLeft = nights, OutOfSeason = outOfSeason },
                ComputedFor = computedFor
            });
        }

        [Test]
        public void RarityColdCacheScoresZeroWithoutComputing() {
            var (engine, target) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            Stopwatch sw = Stopwatch.StartNew();
            new RarityRule().Score(engine, target.Object).Should().Be(0);
            sw.ElapsedMilliseconds.Should().BeLessThan(200);
            reader.Loads.Should().Be(1);
            new RarityRule().Score(engine, target.Object).Should().Be(0);
            reader.Loads.Should().Be(1);
        }

        [Test]
        public void RarityReadsTheWorkerCache() {
            var (engine, target) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            Put(engine, target.Object, 10, false, Today);
            new RarityRule().Score(engine, target.Object).Should().Be(1);

            Put(engine, target.Object, 65, false, Today.AddDays(-1));
            new RarityRule().Score(engine, target.Object).Should().BeApproximately(0.5, 0.0001);

            Put(engine, target.Object, 10, false, Today.AddDays(-2));
            new RarityRule().Score(engine, target.Object).Should().Be(0);

            Put(engine, target.Object, 0, true, Today);
            new RarityRule().Score(engine, target.Object).Should().Be(0);
        }

        private static PlanningTarget BodyTarget(IProject project) {
            return new PlanningTarget {
                Name = "161P/Hartley-IRAS", DatabaseId = 9161, Guid = "g161", Designation = "161P", Coordinates = TestData.M42, Project = project,
                ExposurePlans = new List<IExposure>(), AllExposurePlans = new List<IExposure>(), CompletedExposurePlans = new List<IExposure>()
            };
        }

        [Test]
        public void RarityScoresAMovingBodyFromItsSeasonEntry() {
            var (engine, fixedTarget) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            PlanningTarget body = BodyTarget(fixedTarget.Object.Project);
            new RarityRule().Score(engine, body).Should().Be(0);

            string settings = RarityRule.Key(engine, body);
            settings.Should().StartWith("body|161p|");
            SeasonWorker.Store.Put(new SeasonEntry {
                TargetGuid = "G161",
                Key = SeasonStore.BodyKey(settings, "v1"),
                Season = new SeasonResult { NightsLeft = 10, OutOfSeason = false },
                ComputedFor = Today
            });
            new RarityRule().Score(engine, body).Should().Be(1);
            body.Coordinates = TestData.BETELGEUSE;
            new RarityRule().Score(engine, body).Should().Be(1);
            body.Project.MinimumTime = 90;
            new RarityRule().Score(engine, body).Should().Be(0);
            body.Project.MinimumTime = 60;

            SeasonWorker.Store.Put(new SeasonEntry { TargetGuid = "g161", Key = SeasonStore.BodyKey(settings, "v2"), Season = new SeasonResult { OutOfSeason = true }, ComputedFor = Today });
            new RarityRule().Score(engine, body).Should().Be(0);

            body.Designation = "event:e1";
            SeasonWorker.Store.Put(new SeasonEntry { TargetGuid = "g161", Key = SeasonStore.BodyKey(settings, "v3"), Season = new SeasonResult { NightsLeft = 10 }, ComputedFor = Today });
            new RarityRule().Score(engine, body).Should().Be(0);
        }

        [Test]
        public void RarityIgnoresAnEntryForOldTargetSettings() {
            var (engine, target) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            Put(engine, target.Object, 10, false, Today);
            target.Object.Project.MinimumTime = 90;
            new RarityRule().Score(engine, target.Object).Should().Be(0);
        }

        [Test]
        public void RarityFallsBackToTheSeasonTable() {
            var (engine, target) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            reader.Rows.Add(new TargetSeasonRow { TargetGuid = target.Object.Guid.ToUpperInvariant(), NightsLeft = 21, ComputedFor = Today.AddDays(-1) });
            new RarityRule().Score(engine, target.Object).Should().BeApproximately(0.9, 0.0001);

            target.Object.IsPreview = true;
            new RarityRule().Score(engine, target.Object).Should().BeApproximately(0.9, 0.0001);
        }

        [Test]
        public void RarityIgnoresStaleTableRows() {
            var (engine, target) = RarityCase(TestData.M42, Today.AddHours(22), 20, 60);
            reader.Rows.Add(new TargetSeasonRow { TargetGuid = target.Object.Guid, NightsLeft = 21, ComputedFor = Today.AddDays(-3) });
            new RarityRule().Score(engine, target.Object).Should().Be(0);
        }

        [Test]
        public void SeasonScanWithRealAstrometry() {
            DateTime night = new DateTime(2026, 10, 9);
            ObserverInfo observer = TestData.North_Mid_Lat;
            Coordinates rhoOph = new Coordinates(AstroUtil.HMSToDegrees("16:25:35"), AstroUtil.DMSToDegrees("-23:26:50"), Epoch.J2000, Coordinates.RAType.Degrees);
            HorizonDefinition horizon = new HorizonDefinition(20);

            SeasonCalculator.Compute(observer, rhoOph, horizon, TwilightLevel.Nighttime, 60, night).OutOfSeason.Should().BeTrue();
            SeasonCalculator.Compute(observer, rhoOph, horizon, TwilightLevel.Nighttime, 60, new DateTime(2026, 7, 20)).NightsLeft.Should().BeInRange(15, 60);

            Stopwatch sw = Stopwatch.StartNew();
            SeasonResult m42 = SeasonCalculator.Compute(observer, TestData.M42, horizon, TwilightLevel.Nighttime, 60, night);
            long m42Ms = sw.ElapsedMilliseconds;
            m42.OutOfSeason.Should().BeFalse();
            m42.NightsLeft.Should().BeInRange(120, 220);

            sw.Restart();
            SeasonResult polar = SeasonCalculator.Compute(observer, TestData.STAR_NORTH_CIRCP, horizon, TwilightLevel.Nighttime, 60, night);
            long polarMs = sw.ElapsedMilliseconds;
            polar.NightsLeft.Should().Be(365);
            polar.Capped.Should().BeTrue();
            TestContext.WriteLine($"season scan with TS astrometry: M42 {m42Ms}ms, circumpolar {polarMs}ms");
        }
    }
}
