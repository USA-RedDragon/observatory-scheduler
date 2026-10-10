using FluentAssertions;
using Moq;
using NINA.Core.Model.Equipment;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NUnit.Framework;

namespace NINA.Plugin.TargetScheduler.Test.Planning.Entities {

    [TestFixture]
    public class PlanningExposureTest {

        [Test]
        public void TestPlanningExposure() {
            Mock<ITarget> targetMock = new Mock<ITarget>();
            targetMock.SetupAllProperties();
            targetMock.SetupProperty(m => m.Name, "mock target");

            ExposurePlan ep = GetExposurePlan(101, 60, 10, 2, 1);
            ExposureTemplate et = GetExposureTemplate("Ha", 120, 10, 20, new BinningMode(2, 2), 2, TwilightLevel.Astronomical);

            PlanningExposure sut = new PlanningExposure(targetMock.Object, ep, et);

            sut.PlanId.Should().NotBeNull();
            sut.DatabaseId.Should().Be(101);
            sut.Guid.Should().Be(ep.Guid).And.NotBeNullOrEmpty();
            sut.FilterName.Should().Be("Ha");
            sut.TwilightLevel.Should().Be(TwilightLevel.Astronomical);
            sut.ExposureLength.Should().Be(60);
            sut.Gain.Should().Be(10);
            sut.Offset.Should().Be(20);
            sut.BinningMode.X.Should().Be(2);
            sut.ReadoutMode.Should().Be(2);
            sut.Desired.Should().Be(10);
            sut.Accepted.Should().Be(2);
            sut.Acquired.Should().Be(1);
            sut.MoonAvoidanceEnabled.Should().BeFalse();
            sut.PreDither.Should().BeFalse();
            sut.Rejected.Should().BeFalse();
            sut.PlanTarget.Should().Be(targetMock.Object);
        }

        private ExposurePlan GetExposurePlan(int id, double exposure, int desired, int accepted, int acquired) {
            ExposurePlan ep = new ExposurePlan();
            ep.Id = id;
            ep.Guid = System.Guid.NewGuid().ToString();
            ep.Exposure = exposure;
            ep.Desired = desired;
            ep.Accepted = accepted;
            ep.Acquired = acquired;
            return ep;
        }

        private ExposureTemplate GetExposureTemplate(string filterName, double exposure, int gain, int offset, BinningMode binningMode, int readoutMode, TwilightLevel twilightLevel) {
            ExposureTemplate et = new ExposureTemplate();
            et.FilterName = filterName;
            et.TwilightLevel = twilightLevel;
            et.ReadoutMode = readoutMode;
            et.DefaultExposure = exposure;
            et.Gain = gain;
            et.Offset = offset;
            et.BinningMode = binningMode;
            et.ReadoutMode = readoutMode;
            et.MoonAvoidanceEnabled = false;
            return et;
        }
    }
}