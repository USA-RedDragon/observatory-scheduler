using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Goals;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Exposures;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class EventWindowMoonTest {
        private static readonly DateTime Night = new DateTime(2023, 12, 17, 23, 0, 0);

        [TearDown]
        public void TearDown() {
            TargetWindowTable.Current = TargetWindowTable.Empty;
            SubLengthTable.Current = SubLengthTable.Empty;
        }

        private static void Window(string guid, bool ignoreMoon) {
            TargetWindowTable.Current = new TargetWindowTable(new[] {
                new TargetWindowRow { TargetGuid = guid, StartUtc = Night.AddHours(-1).ToUniversalTime(), EndUtc = Night.AddHours(2).ToUniversalTime(), Priority = "normal", IgnoreMoon = ignoreMoon }
            });
        }

        [Test]
        public void IgnoreMoonSkipsMoonAvoidance() {
            Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
            exposure.SetupProperty(e => e.MoonAvoidanceEnabled, true);
            exposure.SetupProperty(e => e.MoonAvoidanceSeparation, 120.0);
            exposure.SetupProperty(e => e.MoonAvoidanceWidth, 14);
            MoonAvoidanceExpert sut = new MoonAvoidanceExpertMock(TestData.North_Mid_Lat) { Altitude = 20, MoonAge = 14, SeparationAngle = 5 };
            Mock<ITarget> target = PlanMocks.GetMockPlanTarget("Moon and Mars", TestData.M42);
            target.SetupProperty(t => t.Guid, "ev");

            sut.IsRejected(Night, target.Object, exposure.Object).Should().BeTrue();
            Window("ev", false);
            sut.IsRejected(Night, target.Object, exposure.Object).Should().BeTrue();
            Window("ev", true);
            sut.IsRejected(Night, target.Object, exposure.Object).Should().BeFalse();
            exposure.Object.MoonAvoidanceScore.Should().Be(MoonAvoidanceExpert.SCORE_OFF);
        }
    }
}
