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
    public class EventWindowSubLengthTest {
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
        public void APlanExposureWinsOverASystemSubLengthForAWindowedTarget() {
            SubLengthTable.Current = new SubLengthTable(new[] {
                new SubLengthRow { TargetGuid = "ev", Filter = "L", Gain = 100, Bucket = SubLength.Dark, Exposure = 300 },
                new SubLengthRow { TargetGuid = "dso", Filter = "L", Gain = 100, Bucket = SubLength.Dark, Exposure = 300 }
            });
            Window("ev", false);
            foreach (string guid in new[] { "ev", "dso" }) {
                Mock<IExposure> exposure = PlanMocks.GetMockPlanExposure("L", 10, 0);
                exposure.SetupProperty(e => e.PlanExposureLength, 5);
                SubLengthChoice choice = SubLengthPlanning.Apply(exposure.Object, guid, "L", 100, 600, 0.0);
                choice.Seconds.Should().Be(guid == "ev" ? 5 : 300, guid);
                exposure.Object.ExposureLength.Should().Be(guid == "ev" ? 5 : 300, guid);
            }
        }
    }
}
