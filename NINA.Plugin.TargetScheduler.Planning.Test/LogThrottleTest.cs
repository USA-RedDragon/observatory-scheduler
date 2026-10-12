using FluentAssertions;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NUnit.Framework;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Test {

    [TestFixture]
    public class LogThrottleTest {
        private static readonly DateTime Now = new DateTime(2026, 10, 13, 2, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ARepeatedWarningIsLoggedOncePerInterval() {
            LogThrottle t = new LogThrottle(TimeSpan.FromMinutes(30));
            string first = LogThrottle.Key("event probe", "TargetImagingExpert.cs", 110, "Target not visible at all Events/Mars on 10/13/2026 02:00:00 at latitude 40.1");
            string later = LogThrottle.Key("event probe", "TargetImagingExpert.cs", 110, "Target not visible at all Events/Mars on 10/13/2026 02:05:10 at latitude 40.1");
            first.Should().Be(later);
            t.Allow(first, Now).Should().BeTrue();
            t.Allow(later, Now.AddMinutes(5)).Should().BeFalse();
            t.Allow(later, Now.AddMinutes(29)).Should().BeFalse();
            t.Allow(later, Now.AddMinutes(31)).Should().BeTrue();
            t.Allow(later, Now.AddMinutes(40)).Should().BeFalse();
        }

        [Test]
        public void DifferentTargetsLinesAndScopesAreSeparate() {
            LogThrottle t = new LogThrottle(TimeSpan.FromMinutes(30));
            t.Allow(LogThrottle.Key("event probe", "a.cs", 1, "target Events/Mars never rises"), Now).Should().BeTrue();
            t.Allow(LogThrottle.Key("event probe", "a.cs", 1, "target Events/Venus never rises"), Now).Should().BeTrue();
            t.Allow(LogThrottle.Key("event probe", "a.cs", 2, "target Events/Mars never rises"), Now).Should().BeTrue();
            t.Allow(LogThrottle.Key("other", "a.cs", 1, "target Events/Mars never rises"), Now).Should().BeTrue();
            t.Allow(LogThrottle.Key("event probe", "a.cs", 1, null), Now).Should().BeTrue();
        }

        [Test]
        public void AClockStepBackwardsDoesNotSilenceForever() {
            LogThrottle t = new LogThrottle(TimeSpan.FromMinutes(30));
            t.Allow("k", Now).Should().BeTrue();
            t.Allow("k", Now.AddHours(-2)).Should().BeTrue();
            t.Allow("k", Now.AddHours(-2).AddMinutes(1)).Should().BeFalse();
        }

        [Test]
        public void TheKeyTableIsBounded() {
            LogThrottle t = new LogThrottle(TimeSpan.FromMinutes(30));
            for (int i = 0; i < LogThrottle.MaxKeys; i++) {
                t.Allow("k" + new string('x', i % 7) + i.ToString("X"), Now).Should().BeTrue();
            }
            t.Allow("first", Now).Should().BeTrue();
            t.Allow("first", Now.AddMinutes(1)).Should().BeFalse();
        }
    }
}
