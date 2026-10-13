using FluentAssertions;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class SunConditionModelTest {

        [Test]
        public void Nina31MeasuresTheRefractedSun() {
            SunConditionModel.IsGeometric(new Version(3, 1, 2, 9001)).Should().BeFalse();
        }

        [Test]
        public void Nina32AndLaterMeasureTheGeometricSun() {
            SunConditionModel.IsGeometric(new Version(3, 2, 0, 9001)).Should().BeTrue();
            SunConditionModel.IsGeometric(new Version(3, 3, 0, 1066)).Should().BeTrue();
        }

        [Test]
        public void AnUnknownVersionIsTreatedAsNina31() {
            SunConditionModel.IsGeometric(null).Should().BeFalse();
        }

        [Test]
        public void Nina32ReadsAZeroOffsetAsSunrise() {
            SunConditionModel.EffectiveOffset(0.0, true).Should().Be(-0.833);
            SunConditionModel.EffectiveOffset(0.0, false).Should().Be(0.0);
        }

        [Test]
        public void OtherOffsetsAreUnchanged() {
            SunConditionModel.EffectiveOffset(-14.0, true).Should().Be(-14.0);
            SunConditionModel.EffectiveOffset(-14.0, false).Should().Be(-14.0);
        }
    }
}
