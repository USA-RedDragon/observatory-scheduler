using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
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

        private class Writer : ITargetSeasonWriter {
            public List<TargetSeasonRow> Rows { get; } = new List<TargetSeasonRow>();

            public void Upsert(TargetSeasonRow row) => Rows.Add(row);
        }

        private Writer writer;

        [SetUp]
        public void SetUp() {
            writer = new Writer();
            RarityRule.SeasonWriter = writer;
            RarityRule.WriteInBackground = false;
            RarityRule.ClearCache();
        }

        [TearDown]
        public void TearDown() {
            MosaicCompletion.GoalSource = null;
            RarityRule.SeasonWriter = null;
            RarityRule.WriteInBackground = true;
            RarityRule.ClearCache();
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

        [Test]
        public void RarityWithRealAstrometry() {
            DateTime night = new DateTime(2026, 10, 9, 22, 0, 0);
            Coordinates rhoOph = new Coordinates(AstroUtil.HMSToDegrees("16:25:35"), AstroUtil.DMSToDegrees("-23:26:50"), Epoch.J2000, Coordinates.RAType.Degrees);

            var (e1, oph) = RarityCase(rhoOph, night, 20, 60);
            new RarityRule().Score(e1, oph.Object).Should().Be(0);
            RarityRule.GetSeason(e1, oph.Object).OutOfSeason.Should().BeTrue();

            Stopwatch sw = Stopwatch.StartNew();
            var (e2, m42) = RarityCase(TestData.M42, night, 20, 60);
            SeasonResult season = RarityRule.GetSeason(e2, m42.Object);
            long firstMs = sw.ElapsedMilliseconds;
            season.OutOfSeason.Should().BeFalse();
            season.NightsLeft.Should().BeInRange(120, 220);
            new RarityRule().Score(e2, m42.Object).Should().Be(0);

            var (e3, polar) = RarityCase(TestData.STAR_NORTH_CIRCP, night, 20, 60);
            sw.Restart();
            SeasonResult circumpolar = RarityRule.GetSeason(e3, polar.Object);
            long polarMs = sw.ElapsedMilliseconds;
            circumpolar.NightsLeft.Should().Be(365);
            circumpolar.Capped.Should().BeTrue();

            sw.Restart();
            RarityRule.GetSeason(e3, polar.Object);
            sw.ElapsedMilliseconds.Should().BeLessThan(50);
            TestContext.WriteLine($"rarity scan: M42 {firstMs}ms, circumpolar {polarMs}ms");

            writer.Rows.Should().HaveCount(3);
            writer.Rows[1].TargetGuid.Should().Be(m42.Object.Guid);
            writer.Rows[1].ComputedFor.Should().Be(new DateTime(2026, 10, 9));
            writer.Rows[1].SeasonEnd.Should().NotBeNull();
        }

        [Test]
        public void RarityLateInSeasonScoresHigh() {
            DateTime night = new DateTime(2026, 7, 20, 22, 0, 0);
            Coordinates rhoOph = new Coordinates(AstroUtil.HMSToDegrees("16:25:35"), AstroUtil.DMSToDegrees("-23:26:50"), Epoch.J2000, Coordinates.RAType.Degrees);
            var (engine, oph) = RarityCase(rhoOph, night, 20, 60);
            SeasonResult season = RarityRule.GetSeason(engine, oph.Object);
            season.OutOfSeason.Should().BeFalse();
            season.NightsLeft.Should().BeLessThan(60);
            new RarityRule().Score(engine, oph.Object).Should().BeGreaterThan(0.5);
        }

        [Test]
        public void RarityDoesNotWriteForPreviews() {
            var (engine, target) = RarityCase(TestData.M42, new DateTime(2026, 10, 9, 22, 0, 0), 20, 60);
            target.Object.IsPreview = true;
            RarityRule.GetSeason(engine, target.Object);
            writer.Rows.Should().BeEmpty();
        }
    }
}
