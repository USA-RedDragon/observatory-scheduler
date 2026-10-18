using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class FilterSteeringTest {

        private static SteeringCandidate Goal(int index, string filter, double progress, double gain = 5, double hours = 2, bool plateau = false, bool narrow = false, double glare = 0) {
            return new SteeringCandidate {
                Index = index,
                Filter = filter,
                Goal = new GoalProgress { Filter = filter, Progress = progress, GainPerHourPct = gain, EffectiveHours = hours, Plateau = plateau },
                Narrowband = narrow,
                MoonGlare = glare
            };
        }

        [Test]
        public void WithoutGoalsItFallsBack() {
            var candidates = new List<SteeringCandidate> {
                new SteeringCandidate { Index = 0, Filter = "L", PercentComplete = 10 },
                new SteeringCandidate { Index = 1, Filter = "R", PercentComplete = 90 }
            };
            FilterSteering.Applies(candidates).Should().BeFalse();
            FilterSteering.Choose(candidates, 1).Should().Be(-1);
            FilterSteering.Choose(null, 0).Should().Be(-1);
        }

        [Test]
        public void PicksTheLargestDeficit() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 0.8),
                Goal(1, "OIII", 0.3),
                Goal(2, "SII", 0.5)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(1);
        }

        [Test]
        public void BetterMarginalGainBreaksNearDeficits() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 0.40, gain: 1),
                Goal(1, "OIII", 0.45, gain: 8)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(1);
        }

        [Test]
        public void APlateauedFilterYieldsToOneStillGaining() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 0.2, plateau: true),
                Goal(1, "OIII", 0.5)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(1);
        }

        [Test]
        public void AFreshFilterCountsAsFullGain() {
            var c = Goal(0, "Ha", 0, gain: 0, hours: 0);
            FilterSteering.Gain(c).Should().Be(FilterSteering.FreshGainPct);
            FilterSteering.Gain(new SteeringCandidate { Filter = "L" }).Should().Be(FilterSteering.FreshGainPct);
        }

        [Test]
        public void BrightMoonSteersAwayFromBroadband() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.2, glare: 0.9),
                Goal(1, "Ha", 0.6, narrow: true, glare: 0.9)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(1);

            candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.2),
                Goal(1, "Ha", 0.6, narrow: true)
            };
            FilterSteering.Choose(candidates, 1).Should().Be(0);
        }

        [Test]
        public void KeepsTheCurrentFilterWhenItIsWithinTwoPercent() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 0.505),
                Goal(1, "OIII", 0.5)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(0);
            FilterSteering.Choose(candidates, 3).Should().Be(1);
        }

        [Test]
        public void PlansWithoutGoalsInAGoalTargetUseTheirPercentComplete() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 0.7),
                new SteeringCandidate { Index = 1, Filter = "L", PercentComplete = 10 }
            };
            FilterSteering.Choose(candidates, 0).Should().Be(1);
        }

        [Test]
        public void NothingLeftToGainFallsBack() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Ha", 1.0),
                new SteeringCandidate { Index = 1, Filter = "Ha", Goal = new GoalProgress { Done = true } }
            };
            FilterSteering.Choose(candidates, 0).Should().Be(-1);
        }

        [Test]
        public void PoorSkyPrefersNarrowbandThatStillNeedsData() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.1),
                Goal(1, "R", 0.2),
                Goal(2, "Ha", 0.7, narrow: true),
                Goal(3, "OIII", 0.5, narrow: true)
            };
            FilterSteering.Choose(candidates, 0, 0.45).Should().Be(3);
            FilterSteering.Choose(candidates, 0, null).Should().Be(0);
            FilterSteering.Choose(candidates, 0, 0.5).Should().Be(0);
        }

        [Test]
        public void GoodSkyPrefersBroadbandEvenWhenNarrowbandIsFurtherBehind() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "R", 0.6),
                Goal(1, "G", 0.4),
                Goal(2, "Ha", 0.0, narrow: true)
            };
            FilterSteering.Choose(candidates, 2, 0.9).Should().Be(1);
        }

        [Test]
        public void BrightMoonPrefersNarrowbandWithTheLargestDeficit() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.0, glare: 0.6),
                Goal(1, "Ha", 0.8, narrow: true, glare: 0.6),
                Goal(2, "SII", 0.3, narrow: true, glare: 0.6)
            };
            FilterSteering.Choose(candidates, 0).Should().Be(2);
            FilterSteering.Choose(candidates, 0, 1).Should().Be(2);

            foreach (var c in candidates) { c.MoonGlare = 0.4; }
            FilterSteering.Choose(candidates, 1, 1).Should().Be(0);
            FilterSteering.Choose(candidates, 1, 0.8).Should().Be(2);
        }

        [Test]
        public void FallsBackWhenNoFavouredFilterNeedsData() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.3),
                Goal(1, "R", 0.6),
                new SteeringCandidate { Index = 2, Filter = "Ha", Narrowband = true, Goal = new GoalProgress { Done = true } }
            };
            FilterSteering.Choose(candidates, 1, 0.2).Should().Be(0);

            candidates = new List<SteeringCandidate> {
                Goal(0, "L", 1.0),
                Goal(1, "Ha", 0.2, narrow: true)
            };
            FilterSteering.Choose(candidates, 0, 0.9).Should().Be(1);
        }

        [Test]
        public void BroadbandOnlyTargetsKeepTheDeficitOrderInPoorSky() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "L", 0.31),
                Goal(1, "R", 0.06),
                Goal(2, "G", 0.08),
                Goal(3, "B", 0.11)
            };
            FilterSteering.Choose(candidates, 0, 0.45).Should().Be(FilterSteering.Choose(candidates, 0, null));
            FilterSteering.Choose(candidates, 0, 0.45).Should().Be(1);
        }

        [Test]
        public void KeepsTheCurrentFilterOnlyInsideTheFavouredGroup() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "R", 0.5),
                Goal(1, "Ha", 0.5, narrow: true)
            };
            FilterSteering.Choose(candidates, 0, 0.3).Should().Be(1);
            FilterSteering.Choose(candidates, 1, 0.9).Should().Be(0);
        }

        [Test]
        public void SteeringAndConditionMatchFavourTheSameKind() {
            foreach (double glare in new[] { 0, 0.3, 0.5, 0.7, 1 }) {
                foreach (double? q in new double?[] { null, 0, 0.3, 0.5, 0.7, 1 }) {
                    double broad = ConditionMatch.FilterScore(new FilterCondition { Narrowband = false, MoonGlare = glare }, q);
                    ConditionMatch.NarrowbandFavored(glare, q).Should().Be(broad < ConditionMatch.BroadbandSuitableScore);
                    var candidates = new List<SteeringCandidate> {
                        Goal(0, "L", 0.5, glare: glare),
                        Goal(1, "Ha", 0.5, narrow: true, glare: glare)
                    };
                    FilterSteering.Choose(candidates, -1, q).Should().Be(ConditionMatch.NarrowbandFavored(glare, q) ? 1 : 0);
                }
            }
        }

        private static SteeringCandidate Fresh(int index, string filter, double? worth = null, int stackSubs = 0, GoalState state = GoalState.Collecting) {
            return new SteeringCandidate {
                Index = index,
                Filter = filter,
                Goal = new GoalProgress { Filter = filter, State = state, StackSubs = stackSubs, MinSubs = 8 },
                Narrowband = FilterKind.IsNarrowband(filter),
                Worth = worth
            };
        }

        private static List<string> Night(List<SteeringCandidate> candidates, int exposures, double? quality, double glare = 0) {
            candidates.ForEach(c => c.MoonGlare = glare);
            var taken = new List<string>();
            int cadence = 0;
            for (int i = 0; i < exposures; i++) {
                int chosen = FilterSteering.Choose(candidates, cadence, quality);
                if (chosen < 0) { chosen = cadence; }
                taken.Add(candidates[chosen].Filter);
                cadence = (chosen + 1) % candidates.Count;
            }
            return taken;
        }

        private static List<SteeringCandidate> Sculptor() {
            return new List<SteeringCandidate> {
                Fresh(0, "Red"), Fresh(1, "Green"), Fresh(2, "Blue"), Fresh(3, "H-a"), Fresh(4, "Luminance")
            };
        }

        [Test]
        public void APoorSkyNoLongerHoldsAFreshTargetOnHydrogenAlpha() {
            Night(Sculptor(), 8, 0.10).Should().Equal("Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue", "Luminance");
        }

        [Test]
        public void TheMoonKeepWorthNoLongerHoldsAFreshTargetOnOneFilter() {
            var candidates = new List<SteeringCandidate> {
                Fresh(0, "Luminance", 0.63), Fresh(1, "Red", 0.67), Fresh(2, "Green", 0.66), Fresh(3, "Blue", 0.60)
            };
            Night(candidates, 8, null, glare: 0.4).Should().Equal("Luminance", "Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue");
        }

        [Test]
        public void OneBootstrappingFilterSharesTheTurnsWithTheOpenMeasuredOnes() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Luminance", 0.015), Goal(1, "Red", 0.002), Goal(2, "Green", 0.003), Fresh(3, "Blue", stackSubs: 2), Goal(4, "H-a", 0.42, narrow: true)
            };
            Night(candidates, 8, 0.15).Should().Equal("Luminance", "Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue");
        }

        [Test]
        public void APendingGoalBootstrapsLikeACollectingOne() {
            var candidates = new List<SteeringCandidate> {
                Fresh(0, "Red", state: GoalState.Pending), Fresh(1, "H-a", state: GoalState.Pending), Fresh(2, "O-III", state: GoalState.Pending)
            };
            Night(candidates, 4, 0.10).Should().Equal("Red", "O-III", "Red", "O-III");
        }

        [Test]
        public void OnlyHydrogenAlphaBootstrappingLeavesTheUsualSteering() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Luminance", 0.2), Fresh(1, "H-a")
            };
            FilterSteering.Evaluate(candidates, 0.10);
            FilterSteering.Bootstrap(candidates, 0).Should().Be(-1);
            Night(candidates, 3, 0.10).Should().Equal("H-a", "H-a", "H-a");
        }

        [Test]
        public void MeasuredGoalsKeepTheUsualSteering() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Luminance", 0.2, glare: 0.9), Goal(1, "H-a", 0.6, narrow: true, glare: 0.9)
            };
            FilterSteering.Evaluate(candidates);
            FilterSteering.Bootstrap(candidates, 0).Should().Be(-1);
            FilterSteering.Choose(candidates, 0).Should().Be(1);
        }

        [Test]
        public void AFilterTheConditionsRuleOutSitsOutItsTurn() {
            var candidates = new List<SteeringCandidate> {
                Fresh(0, "Red", 0.67), Fresh(1, "Green", 0), Fresh(2, "Blue", 0.60)
            };
            Night(candidates, 4, null, glare: 0.4).Should().Equal("Red", "Blue", "Red", "Blue");
        }

        [Test]
        public void TheRotationStartsAtTheCadencePosition() {
            var candidates = Sculptor();
            FilterSteering.Choose(candidates, 2, 0.10).Should().Be(2);
            FilterSteering.Choose(candidates, 3, 0.10).Should().Be(4);
            FilterSteering.Choose(candidates, -1, 0.10).Should().Be(0);
        }

        [Test]
        public void HydrogenAlphaIsKnownByAnyOfItsNames() {
            foreach (string name in new[] { "H-a", "Ha", "HA", "H-alpha", "Halpha", "Hydrogen Alpha", "Ha 7nm", "H-alpha 3nm", "Hα" }) {
                FilterSteering.IsHydrogenAlpha(name).Should().BeTrue(name);
            }
            foreach (string name in new[] { "O-III", "S-II", "Luminance", "Red", "Hb", "Hb 5nm", "Halo", null, "" }) {
                FilterSteering.IsHydrogenAlpha(name).Should().BeFalse(name ?? "null");
            }
        }

        private const double SculptorBroadbandCut = 0.27;

        private static double BroadbandExpected(double quality) {
            return quality <= 0.15 ? 0.11 : quality;
        }

        private static List<string> GradedNight(List<SteeringCandidate> candidates, IList<double?> qualities, Func<SteeringCandidate, double, (double? cut, double? expected)> stacker) {
            var taken = new List<string>();
            int cadence = 0;
            foreach (double? quality in qualities) {
                foreach (SteeringCandidate c in candidates) {
                    (double? cut, double? expected) = quality.HasValue ? stacker(c, quality.Value) : (null, null);
                    c.Goal.ScoreCut = cut;
                    c.Goal.ExpectedScore = expected;
                }
                int chosen = FilterSteering.Choose(candidates, cadence, quality);
                if (chosen < 0) { chosen = cadence; }
                taken.Add(candidates[chosen].Filter);
                cadence = (chosen + 1) % candidates.Count;
            }
            return taken;
        }

        private static (double? cut, double? expected) SculptorStacker(SteeringCandidate c, double quality) {
            if (c.Narrowband) { return (null, null); }
            return (SculptorBroadbandCut, BroadbandExpected(quality));
        }

        private static List<double?> Repeat(double? quality, int count) {
            return Enumerable.Repeat(quality, count).ToList();
        }

        [Test]
        public void APoorSkyTakesHydrogenAlphaWhileTheStackerExpectsBroadbandToFailItsCut() {
            GradedNight(Sculptor(), Repeat(0.10, 6), SculptorStacker).Should().Equal(Enumerable.Repeat("H-a", 6));
        }

        [Test]
        public void AGoodSkyRotatesTheBroadbandFiltersOfAFreshTarget() {
            GradedNight(Sculptor(), Repeat(0.70, 8), SculptorStacker).Should().Equal("Red", "Green", "Blue", "Luminance", "Red", "Green", "Blue", "Luminance");
        }

        [Test]
        public void TheRotationResumesWhenTheSkyRecovers() {
            var qualities = Repeat(0.10, 3).Concat(Repeat(0.15, 2)).Concat(Repeat(0.60, 5)).ToList();
            GradedNight(Sculptor(), qualities, SculptorStacker).Should().Equal("H-a", "H-a", "H-a", "H-a", "H-a", "Luminance", "Red", "Green", "Blue", "Luminance");
        }

        [Test]
        public void HydrogenAlphaStillTakesAPoorSkyWhenItWouldFailItsCutToo() {
            GradedNight(Sculptor(), Repeat(0.10, 4), (c, q) => c.Narrowband ? (0.18, 0.10) : SculptorStacker(c, q)).Should().Equal(Enumerable.Repeat("H-a", 4));
        }

        [Test]
        public void OnlyTheFiltersUnderTheirCutSitOutTheRotation() {
            GradedNight(Sculptor(), Repeat(0.30, 6), (c, q) => c.Filter == "Blue" ? (0.25, 0.20) : SculptorStacker(c, q)).Should().Equal("Red", "Green", "Luminance", "Red", "Green", "Luminance");
        }

        [Test]
        public void AnUnknownNightQualityKeepsTheRotation() {
            List<SteeringCandidate> candidates = Sculptor();
            foreach (SteeringCandidate c in candidates.Where(c => !c.Narrowband)) {
                c.Goal.ScoreCut = SculptorBroadbandCut;
                c.Goal.ExpectedScore = 0.11;
            }
            Night(candidates, 4, null).Should().Equal("Red", "Green", "Blue", "Luminance");
            FilterSteering.BelowCut(candidates[2], null).Should().BeFalse();
            FilterSteering.BelowCut(candidates[2], 0.10).Should().BeTrue();
        }

        [Test]
        public void ABroadbandFilterWithoutAnEstimateDoesNotTakeAPoorSkyFromHydrogenAlpha() {
            var candidates = new List<SteeringCandidate> {
                Goal(0, "Blue", 0.3), Goal(1, "H-a", 0.3, narrow: true)
            };
            candidates[1].Goal.ScoreCut = 0.27;
            candidates[1].Goal.ExpectedScore = 0.12;
            FilterSteering.BelowCut(candidates[1], 0.10).Should().BeFalse();
            Night(candidates, 3, 0.10).Should().Equal("H-a", "H-a", "H-a");
        }

        [Test]
        public void AnExpectedScoreForAnotherNightQualityIsNotUsed() {
            SteeringCandidate blue = Fresh(2, "Blue");
            blue.Goal.ScoreCut = 0.27;
            blue.Goal.ExpectedScore = 0.11;
            blue.Goal.ExpectedAtQuality = 0.10;
            FilterSteering.BelowCut(blue, 0.10).Should().BeTrue();
            FilterSteering.BelowCut(blue, 0.12).Should().BeTrue();
            FilterSteering.BelowCut(blue, 0.15).Should().BeFalse();
            FilterSteering.BelowCut(blue, 0.60).Should().BeFalse();
        }

        [Test]
        public void WithoutTheStackersNumbersTheRotationIsUnchanged() {
            GradedNight(Sculptor(), Repeat(0.10, 4), (c, q) => (null, null)).Should().Equal("Red", "Green", "Blue", "Luminance");
            GradedNight(Sculptor(), Repeat(0.10, 4), (c, q) => (0.27, null)).Should().Equal("Red", "Green", "Blue", "Luminance");
        }

        private static List<SteeringCandidate> BarnardsE() {
            return new List<SteeringCandidate> {
                Goal(0, "Luminance", 0.4), Goal(1, "Red", 0.3), Goal(2, "Green", 0.35), Fresh(3, "Blue", stackSubs: 4)
            };
        }

        [Test]
        public void BlueUnderItsCutIsSkippedOnBarnardsE() {
            List<string> taken = GradedNight(BarnardsE(), Repeat(0.15, 9), (c, q) => c.Filter == "Blue" ? (0.25, 0.11) : (0.25, 0.30));
            taken.Should().NotContain("Blue");
            taken.Should().HaveCount(9);
        }

        [Test]
        public void BlueTakesItsTurnOnBarnardsEOnceItCanPass() {
            GradedNight(BarnardsE(), Repeat(0.60, 4), (c, q) => (0.25, 0.60)).Should().Equal("Luminance", "Red", "Green", "Blue");
        }

        [Test]
        public void WhenEveryFilterIsUnderItsCutTheUsualSteeringStillPicks() {
            List<SteeringCandidate> candidates = BarnardsE();
            List<string> taken = GradedNight(candidates, Repeat(0.10, 3), (c, q) => (0.25, 0.05));
            taken.Should().HaveCount(3);
            taken.Should().OnlyContain(f => f == "Blue");
        }
    }
}
