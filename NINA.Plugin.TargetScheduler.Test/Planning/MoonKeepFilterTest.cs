using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class MoonKeepFilterTest {

        [TearDown]
        public void TearDown() {
            MoonKeepTable.Current = MoonKeepTable.Empty;
        }

        private static void SkipEveryMoonUp() {
            MoonKeepTable.Current = new MoonKeepTable(new[] {
                new MoonKeepRow { Filter = "", MoonDown = true, IllumTo = 1, AltFrom = -90, SepTo = 180, Subs = 100, Kept = 80, Nights = 10, Rate = 0.8, RateHigh = 0.85, Worth = 1, Usable = true },
                new MoonKeepRow { Filter = "", IllumTo = 1, AltTo = 90, SepTo = 180, Subs = 100, Kept = 0, Nights = 10, Rate = 0.01, RateHigh = 0.02, Worth = 0.01, Usable = true, Skips = true }
            });
        }

        private static DateTime MoonUp() {
            DateTime t = new DateTime(2023, 12, 27, 0, 0, 0, DateTimeKind.Local);
            for (int i = 0; i < 48; i++, t = t.AddMinutes(30)) {
                if (AstroUtil.GetMoonAltitude(t.AddSeconds(15), TestData.North_Mid_Lat) > 5) { return t; }
            }
            throw new Exception("no Moon-up time found");
        }

        private static (Mock<ITarget>, Mock<IExposure>) Plan(bool managed) {
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("Red", 10, 0);
            exposure.SetupProperty(e => e.IsManaged, managed);
            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("Spica", TestData.SPICA);
            target.Object.ExposurePlans.Add(exposure.Object);
            return (target, exposure);
        }

        private static TargetImagingExpert Expert() {
            return new TargetImagingExpert(PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), false);
        }

        [Test]
        public void ManagedExposuresAreLeftUnderASkipRow() {
            DateTime at = MoonUp();
            var (target, exposure) = Plan(true);
            Expert().MoonKeepRejects(at, target.Object, exposure.Object).Should().BeFalse("no table, no change");
            SkipEveryMoonUp();
            Expert().MoonKeepRejects(at, target.Object, exposure.Object).Should().BeTrue();
            Expert().MoonAvoidanceFilter(at, target.Object, new Mock<IMoonAvoidanceExpert>().Object);
            exposure.Object.RejectedReason.Should().Be(Reasons.FilterMoonKeep);
            new Planner(at, PlanMocks.GetMockProfileService(TestData.North_Mid_Lat).Object.ActiveProfile, new ProfilePreference("abcd-1234"), PlanMocks.GetWeatherDataMediator(false, 0), false, false)
                .GetTargetExposureRejectReason(target.Object).Should().Be(Reasons.TargetMoonKeep);
        }

        [Test]
        public void TemplatesThatAreNotManagedKeepTheirRules() {
            SkipEveryMoonUp();
            var (target, exposure) = Plan(false);
            Expert().MoonKeepRejects(MoonUp(), target.Object, exposure.Object).Should().BeFalse();
            SkyConditions.KeepApplies(target.Object, exposure.Object).Should().BeFalse("the weighting keeps the glare rule too");
            exposure.Object.IsManaged = true;
            SkyConditions.KeepApplies(target.Object, exposure.Object).Should().BeTrue();
        }
    }
}
