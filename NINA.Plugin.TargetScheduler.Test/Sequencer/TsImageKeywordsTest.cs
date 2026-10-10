using FluentAssertions;
using NINA.Plugin.TargetScheduler.Sequencer;
using NUnit.Framework;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Sequencer {

    [TestFixture]
    public class TsImageKeywordsTest {

        [Test]
        public void testKeyLengthsFitFits() {
            TsImageKeywords.PROJECT_KEY.Length.Should().BeLessThanOrEqualTo(8);
            TsImageKeywords.TARGET_KEY.Length.Should().BeLessThanOrEqualTo(8);
            TsImageKeywords.EXPOSURE_PLAN_KEY.Length.Should().BeLessThanOrEqualTo(8);
            TsImageKeywords.PANEL_KEY.Length.Should().BeLessThanOrEqualTo(8);
            TsImageKeywords.EXPOSURE_PLAN_KEY.Should().Be("TSEXPPLN");
        }

        [Test]
        public void testAllGuids() {
            var sut = TsImageKeywords.Build("p-guid", "t-guid", "e-guid", "M31");
            sut.Select(k => k.Key).Should().Equal("TSPROJ", "TSTARGET", "TSEXPPLN");
            sut[0].Value.Should().Be("p-guid");
            sut[1].Value.Should().Be("t-guid");
            sut[2].Value.Should().Be("e-guid");
            sut.Should().OnlyContain(k => !string.IsNullOrEmpty(k.Comment));
        }

        [Test]
        public void testSkipsEmptyGuids() {
            TsImageKeywords.Build(null, "", "  ", null).Should().BeEmpty();
            TsImageKeywords.Build(null, "t-guid", null, "M31").Select(k => k.Key).Should().Equal("TSTARGET");
        }

        [Test]
        public void testPanelKeyword() {
            var sut = TsImageKeywords.Build("p", "t", "e", "M31 Panel 3");
            sut.Select(k => k.Key).Should().Equal("TSPROJ", "TSTARGET", "TSEXPPLN", "TSPANEL");
            sut[3].Value.Should().BeOfType<int>().And.Be(3);
        }

        [TestCase("M31 Panel 1", 1)]
        [TestCase("M31 panel 12", 12)]
        [TestCase("M31 PANEL7", 7)]
        [TestCase("M31_Panel 2", null)]
        [TestCase("Sh2-129 Panel 4  ", 4)]
        [TestCase("M31 Panel 1 Ha", null)]
        [TestCase("Panel 5", 5)]
        [TestCase("M31 Subpanel 5", null)]
        [TestCase("M31", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        [TestCase("M31 Panel 99999999999", null)]
        public void testPanelNumber(string targetName, int? expected) {
            TsImageKeywords.PanelNumber(targetName).Should().Be(expected);
        }

        [Test]
        public void testNoPanelKeywordWithoutMatch() {
            TsImageKeywords.Build("p", "t", "e", "M31 Mosaic").Should().NotContain(k => k.Key == TsImageKeywords.PANEL_KEY);
        }
    }
}
