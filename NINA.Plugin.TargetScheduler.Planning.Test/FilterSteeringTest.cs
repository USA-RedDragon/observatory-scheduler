using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System.Collections.Generic;

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
    }
}
