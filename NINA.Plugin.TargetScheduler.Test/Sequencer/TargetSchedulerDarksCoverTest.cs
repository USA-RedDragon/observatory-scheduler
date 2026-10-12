using FluentAssertions;
using Moq;
using NINA.Equipment.Equipment.MyFlatDevice;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Sequencer;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Test.Sequencer {

    [TestFixture]
    public class TargetSchedulerDarksCoverTest {

        private static TargetSchedulerDarks Darks(string flatId, Mock<IFlatDeviceMediator> flat) {
            var settings = new Mock<IFlatDeviceSettings>();
            settings.SetupGet(m => m.Id).Returns(flatId);
            var profile = new Mock<IProfile>();
            profile.SetupGet(m => m.FlatDeviceSettings).Returns(settings.Object);
            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(m => m.ActiveProfile).Returns(profile.Object);
            return new TargetSchedulerDarks(profileService.Object,
                new Mock<ICameraMediator>().Object,
                new Mock<IImagingMediator>().Object,
                new Mock<IImageSaveMediator>().Object,
                new Mock<IImageHistoryVM>().Object,
                new Mock<ISafetyMonitorMediator>().Object,
                new Mock<IWeatherDataMediator>().Object,
                flat.Object);
        }

        private static Mock<IFlatDeviceMediator> Flat(bool connected, CoverState state) {
            var flat = new Mock<IFlatDeviceMediator>(MockBehavior.Strict);
            flat.Setup(m => m.GetInfo()).Returns(new FlatDeviceInfo { Connected = connected, CoverState = state, Name = "DeepSkyDad FP" });
            return flat;
        }

        [Test]
        public void EveryNinaCoverStateMapsByName() {
            foreach (CoverState state in Enum.GetValues(typeof(CoverState))) {
                DarkCover c = Darks("ASCOM.DeepSkyDad.FP.CoverCalibrator1", Flat(true, state)).ReadCover();
                c.State.ToString().Should().Be(state.ToString());
            }
        }

        [Test]
        public void OnlyAConnectedClosedCoverAllowsDarks() {
            foreach (CoverState state in Enum.GetValues(typeof(CoverState))) {
                var flat = Flat(true, state);
                string reason = DarkPlanner.CoverStopReason(Darks("ASCOM.DeepSkyDad.FP.CoverCalibrator1", flat).ReadCover());
                if (state == CoverState.Closed) {
                    reason.Should().BeNull();
                } else {
                    reason.Should().Contain("not Closed");
                }
                flat.Verify(m => m.GetInfo(), Times.Once);
                flat.VerifyNoOtherCalls();
            }
        }

        [Test]
        public void DisconnectedFlatDeviceStopsDarks() {
            var flat = Flat(false, CoverState.Closed);
            DarkCover c = Darks("ASCOM.DeepSkyDad.FP.CoverCalibrator1", flat).ReadCover();
            c.Configured.Should().BeTrue();
            DarkPlanner.CoverStopReason(c).Should().Contain("not connected");
            flat.Verify(m => m.GetInfo(), Times.Once);
            flat.VerifyNoOtherCalls();
        }

        [Test]
        public void ThrowingFlatDeviceStopsDarks() {
            var flat = new Mock<IFlatDeviceMediator>(MockBehavior.Strict);
            flat.Setup(m => m.GetInfo()).Throws(new InvalidOperationException("driver threw"));
            DarkPlanner.CoverStopReason(Darks("ASCOM.DeepSkyDad.FP.CoverCalibrator1", flat).ReadCover()).Should().Contain("driver threw");
        }

        [TestCase("No_Device")]
        [TestCase("")]
        [TestCase(null)]
        public void NoFlatDeviceInTheProfileDoesNotReadIt(string id) {
            var flat = new Mock<IFlatDeviceMediator>(MockBehavior.Strict);
            DarkCover c = Darks(id, flat).ReadCover();
            c.Configured.Should().BeFalse();
            DarkPlanner.CoverStopReason(c).Should().BeNull();
            flat.VerifyNoOtherCalls();
        }
    }
}
