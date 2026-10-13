using FluentAssertions;
using Moq;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Astrometry;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Planning;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Scoring.Rules;
using NINA.Plugin.TargetScheduler.Test.Astrometry;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Test.Planning {

    [TestFixture]
    public class PlannerTest {

        [Test]
        public void testFilterForReadyComplete() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            Mock<IProject> pp2 = PlanMocks.GetMockPlanProject("pp2", ProjectState.Active);
            pt = PlanMocks.GetMockPlanTarget("M31", TestData.M31);
            pf = PlanMocks.GetMockPlanExposure("OIII", 10, 10);
            PlanMocks.AddMockPlanFilter(pt, pf);
            pf = PlanMocks.GetMockPlanExposure("SII", 10, 10);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp2, pt);

            Assert.That(new Planner(new DateTime(2023, 12, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForIncomplete(null), Is.Null);

            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object, pp2.Object);
            projects = new Planner(new DateTime(2023, 12, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForIncomplete(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(2);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();
            IExposure pf1 = pt1.ExposurePlans[0];
            pf1.Rejected.Should().BeFalse();

            pp = projects[1];
            pp.Name.Should().Be("pp2");
            pp.Rejected.Should().BeTrue();
            pp.RejectedReason.Should().Be(Reasons.ProjectComplete);
            pt1 = pp.Targets[0];
            pf1 = pt1.ExposurePlans[0];
            pf1.Rejected.Should().BeTrue();
            pf1.RejectedReason.Should().Be(Reasons.FilterComplete);
            pf1 = pt1.ExposurePlans[1];
            pf1.Rejected.Should().BeTrue();
            pf1.RejectedReason.Should().Be(Reasons.FilterComplete);
        }

        [Test]
        public void testFilterForIncomplete() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            pt = PlanMocks.GetMockPlanTarget("M31", TestData.M31);
            pf = PlanMocks.GetMockPlanExposure("OIII", 10, 10);
            PlanMocks.AddMockPlanFilter(pt, pf);
            pf = PlanMocks.GetMockPlanExposure("SII", 10, 12);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);
            projects = new Planner(new DateTime(2023, 12, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForIncomplete(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();

            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();
            IExposure pf1 = pt1.ExposurePlans[0];
            pf1.Rejected.Should().BeFalse();

            ITarget pt2 = pp.Targets[1];
            pt2.Rejected.Should().BeTrue();
            pt2.RejectedReason.Should().Be(Reasons.TargetComplete);

            IExposure pf2 = pt2.ExposurePlans[0];
            pf2.Rejected.Should().BeTrue();
            pf2.RejectedReason.Should().Be(Reasons.FilterComplete);
            IExposure pf3 = pt2.ExposurePlans[1];
            pf3.Rejected.Should().BeTrue();
            pf3.RejectedReason.Should().Be(Reasons.FilterComplete);
        }

        [Test]
        public void testFilterForIncompleteAllExposuresThrottled() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            ProfilePreference prefs = GetPrefs();
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);

            // Assert setup for throttling
            pp1.Object.EnableGrader.Should().BeFalse();
            prefs.ExposureThrottle.Should().Be(125);

            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            ExposurePlan ep = new ExposurePlan("abcd-1234") { Desired = 12, Acquired = 15 };
            ExposureTemplate et = new ExposureTemplate("abcd-1234", "R", "R");
            IExposure peRed = new PlanningExposure(pt.Object, ep, et);

            ep = new ExposurePlan("abcd-1234") { Desired = 12, Acquired = 15 };
            et = new ExposureTemplate("abcd-1234", "G", "G");
            IExposure peGreen = new PlanningExposure(pt.Object, ep, et);

            ep = new ExposurePlan("abcd-1234") { Desired = 12, Acquired = 10 };
            et = new ExposureTemplate("abcd-1234", "B", "B");
            IExposure peBlue = new PlanningExposure(pt.Object, ep, et);

            pt.Object.ExposurePlans = new List<IExposure>() { peRed, peGreen, peBlue };

            // Blue is not complete ...
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);
            projects = new Planner(new DateTime(2023, 12, 15, 18, 0, 0), profile, prefs, weatherData, false, false).FilterForIncomplete(projects);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeFalse();
            projects[0].Targets[0].Rejected.Should().BeFalse();

            ep = new ExposurePlan("abcd-1234") { Desired = 12, Acquired = 15 };
            et = new ExposureTemplate("abcd-1234", "B", "B");
            peBlue = new PlanningExposure(pt.Object, ep, et);

            pt.Object.ExposurePlans = new List<IExposure>() { peRed, peGreen, peBlue };

            // All are now complete due to throttle
            projects = new Planner(new DateTime(2023, 12, 15, 18, 0, 0), profile, prefs, weatherData, false, false).FilterForIncomplete(projects);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeTrue();
            projects[0].RejectedReason.Should().Be(Reasons.ProjectComplete);
            projects[0].Targets[0].Rejected.Should().BeTrue();
            projects[0].Targets[0].RejectedReason.Should().Be(Reasons.TargetComplete);
        }

        [Test]
        public void testTargetNoExposurePlans() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            pt = PlanMocks.GetMockPlanTarget("M31", TestData.M31);
            PlanMocks.AddMockPlanTarget(pp1, pt);

            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);
            projects = new Planner(new DateTime(2023, 12, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForIncomplete(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();

            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();
            IExposure pf1 = pt1.ExposurePlans[0];
            pf1.Rejected.Should().BeFalse();

            ITarget pt2 = pp.Targets[1];
            pt2.ExposurePlans.Count.Should().Be(0);
            pt2.Rejected.Should().BeTrue();
            pt2.RejectedReason.Should().Be(Reasons.TargetComplete);
        }

        [Test]
        public void testFilterForVisibilityNeverRises() {
            // Southern hemisphere location and IC1805
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.South_Mid_Lat);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("IC1805", TestData.IC1805);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 12, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeTrue();
            pp.RejectedReason.Should().Be(Reasons.ProjectAllTargets);
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeTrue();
            pt1.RejectedReason.Should().Be(Reasons.TargetNeverRises);
        }

        [Test]
        public void testFilterForVisibilityNotNow() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 6, 17, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeTrue();
            pp.RejectedReason.Should().Be(Reasons.ProjectAllTargets);
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeTrue();
            pt1.RejectedReason.Should().Be(Reasons.TargetNotVisible);
        }

        [Test]
        public void testFilterForVisibilityVisible() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            DateTime atTime = new DateTime(2023, 12, 17, 19, 0, 0);
            projects = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();

            TimeSpan precision = TimeSpan.FromSeconds(1);
            pt1.StartTime.Should().BeCloseTo(new DateTime(2023, 12, 17, 18, 59, 54), precision);
            pt1.CulminationTime.Should().BeCloseTo(new DateTime(2023, 12, 18, 0, 5, 45), precision);
            pt1.EndTime.Should().BeCloseTo(new DateTime(2023, 12, 18, 5, 47, 59), precision);
        }

        [Test]
        public void testFilterForVisibilityInMeridianWindow() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            pp1.SetupProperty(m => m.MeridianWindow, 30);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 12, 17, 23, 36, 0), profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();

            TimeSpan precision = TimeSpan.FromSeconds(1);
            pt1.StartTime.Should().BeCloseTo(new DateTime(2023, 12, 17, 23, 35, 54), precision);
            pt1.CulminationTime.Should().BeCloseTo(new DateTime(2023, 12, 18, 0, 5, 45), precision);
            pt1.EndTime.Should().BeCloseTo(new DateTime(2023, 12, 18, 0, 35, 45), precision);
        }

        [Test]
        public void testFilterForVisibilityWaitForMeridianWindow() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            pp1.SetupProperty(m => m.MeridianWindow, 30);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 12, 17, 19, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeTrue();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeTrue();
            pt1.RejectedReason.Should().Be(Reasons.TargetBeforeMeridianWindow);
        }

        [Test]
        public void testFilterForVisibilityMeridianWindowCircumpolar() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Sanikiluaq_NU);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            pp1.SetupProperty(m => m.MeridianWindow, 30);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("IC1805", TestData.IC1805);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 12, 17, 20, 38, 0), profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();

            TimeSpan precision = TimeSpan.FromSeconds(1);
            pt1.StartTime.Should().BeCloseTo(new DateTime(2023, 12, 17, 20, 37, 55), precision);
            pt1.CulminationTime.Should().BeCloseTo(new DateTime(2023, 12, 17, 21, 7, 18), precision);
            pt1.EndTime.Should().BeCloseTo(new DateTime(2023, 12, 17, 21, 37, 18), precision);
        }

        [Test]
        public void testFilterForMaxAltitude() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            pp1.SetupProperty(p => p.MaximumAltitude, 45);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            Mock<IExposure> pf = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt, pf);
            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            DateTime atTime = new DateTime(2024, 12, 18, 1, 0, 0);
            projects = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).FilterForVisibility(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeTrue();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeTrue();
            pt1.RejectedReason.Should().Be(Reasons.TargetMaxAltitude);
        }

        [Test]
        public void testFilterForMoonAvoidance() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);

            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt.SetupProperty(m => m.StartTime, new DateTime(2023, 12, 25, 18, 9, 0));
            pt.SetupProperty(m => m.EndTime, new DateTime(2023, 12, 26, 5, 17, 0));

            Mock<IExposure> pe = PlanMocks.GetMockPlanExposure("L", 10, 0);
            pe.SetupProperty(f => f.MoonAvoidanceEnabled, true);
            pe.SetupProperty(f => f.MoonAvoidanceSeparation, 50);
            pe.SetupProperty(f => f.MoonAvoidanceWidth, 7);
            PlanMocks.AddMockPlanFilter(pt, pe);

            pe = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            pe.SetupProperty(f => f.MoonAvoidanceEnabled, true);
            pe.SetupProperty(f => f.MoonAvoidanceSeparation, 30);
            pe.SetupProperty(f => f.MoonAvoidanceWidth, 7);
            PlanMocks.AddMockPlanFilter(pt, pe);

            PlanMocks.AddMockPlanTarget(pp1, pt);
            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);

            projects = new Planner(new DateTime(2023, 12, 25, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForMoonAvoidance(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);

            IProject pp = projects[0];
            pp.Name.Should().Be("pp1");
            pp.Rejected.Should().BeFalse();
            ITarget pt1 = pp.Targets[0];
            pt1.Rejected.Should().BeFalse();

            IExposure pe1 = pt1.ExposurePlans[0];
            pe1.Rejected.Should().BeTrue();
            pe1.RejectedReason.Should().Be(Reasons.FilterMoonAvoidance);

            pe1 = pt1.ExposurePlans[1];
            pe1.Rejected.Should().BeFalse();
            pe1.MoonAvoidanceScore.Should().BeApproximately(.0833, 0.001);
        }

        [Test]
        public void testFilterForTwilightCivil() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            List<IProject> projects = GetProjectForFilterTest();

            projects = new Planner(new DateTime(2024, 12, 1, 17, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForTwilight(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeTrue();
            projects[0].Targets[0].Rejected.Should().BeTrue();

            projects[0].Targets[0].ExposurePlans[0].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterTwilight);
            projects[0].Targets[0].ExposurePlans[1].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[1].RejectedReason.Should().Be(Reasons.FilterTwilight);
            projects[0].Targets[0].ExposurePlans[2].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[2].RejectedReason.Should().Be(Reasons.FilterTwilight);
        }

        [Test]
        public void testFilterForTwilightNautical() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            List<IProject> projects = GetProjectForFilterTest();

            projects = new Planner(new DateTime(2024, 12, 1, 18, 0, 0), profile, GetPrefs(), weatherData, false, false).FilterForTwilight(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeFalse();
            projects[0].Targets[0].Rejected.Should().BeFalse();

            projects[0].Targets[0].ExposurePlans[0].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterTwilight);
            projects[0].Targets[0].ExposurePlans[1].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[1].RejectedReason.Should().Be(Reasons.FilterTwilight);
            projects[0].Targets[0].ExposurePlans[2].Rejected.Should().BeFalse();
        }

        [Test]
        public void testFilterForTwilightAstronomical() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            List<IProject> projects = GetProjectForFilterTest();

            projects = new Planner(new DateTime(2024, 12, 1, 18, 20, 0), profile, GetPrefs(), weatherData, false, false).FilterForTwilight(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeFalse();
            projects[0].Targets[0].Rejected.Should().BeFalse();

            projects[0].Targets[0].ExposurePlans[0].Rejected.Should().BeTrue();
            projects[0].Targets[0].ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterTwilight);
            projects[0].Targets[0].ExposurePlans[1].Rejected.Should().BeFalse();
            projects[0].Targets[0].ExposurePlans[2].Rejected.Should().BeFalse();
        }

        [Test]
        public void testFilterForTwilightNight() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            List<IProject> projects = GetProjectForFilterTest();

            projects = new Planner(new DateTime(2024, 12, 1, 19, 20, 0), profile, GetPrefs(), weatherData, false, false).FilterForTwilight(projects);
            Assert.That(projects, Is.Not.Null);
            projects.Count.Should().Be(1);
            projects[0].Rejected.Should().BeFalse();
            projects[0].Targets[0].Rejected.Should().BeFalse();

            projects[0].Targets[0].ExposurePlans[0].Rejected.Should().BeFalse();
            projects[0].Targets[0].ExposurePlans[1].Rejected.Should().BeFalse();
            projects[0].Targets[0].ExposurePlans[2].Rejected.Should().BeFalse();
        }

        [Test]
        public void testFilterForHumidity() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            DateTime atTime = new DateTime(2025, 1, 1, 18, 0, 0);

            List<IProject> projects = new List<IProject>();
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt1 = PlanMocks.GetMockPlanTarget("T1", TestData.M42);
            pt1.SetupProperty(t => t.StartTime, atTime.AddHours(1));
            Mock<IExposure> pf1 = PlanMocks.GetMockPlanExposure("R", 10, 0);
            Mock<IExposure> pf2 = PlanMocks.GetMockPlanExposure("G", 10, 0);
            Mock<IExposure> pf3 = PlanMocks.GetMockPlanExposure("B", 10, 0);

            pf1.SetupProperty(m => m.MaximumHumidity, 60);
            pf2.SetupProperty(m => m.MaximumHumidity, 70);
            pf3.SetupProperty(m => m.MaximumHumidity, 80);

            PlanMocks.AddMockPlanFilter(pt1, pf1);
            PlanMocks.AddMockPlanFilter(pt1, pf2);
            PlanMocks.AddMockPlanFilter(pt1, pf3);
            PlanMocks.AddMockPlanTarget(pp1, pt1);
            projects = PlanMocks.ProjectsList(pp1.Object);

            // No weather data connected
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            var sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            List<IProject> result = sut.FilterForHumidity(projects, weatherData);
            result[0].Rejected.Should().BeFalse();
            ITarget target = result[0].Targets[0];
            target.Rejected.Should().BeFalse();
            target.ExposurePlans[0].Rejected.Should().BeFalse();
            target.ExposurePlans[1].Rejected.Should().BeFalse();
            target.ExposurePlans[2].Rejected.Should().BeFalse();

            // Weather data connected but humidity is low
            weatherData = PlanMocks.GetWeatherDataMediator(true, 10);
            sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            result = sut.FilterForHumidity(projects, weatherData);
            result[0].Rejected.Should().BeFalse();
            target = result[0].Targets[0];
            target.Rejected.Should().BeFalse();
            target.ExposurePlans[0].Rejected.Should().BeFalse();
            target.ExposurePlans[1].Rejected.Should().BeFalse();
            target.ExposurePlans[2].Rejected.Should().BeFalse();

            // Should start to reject ...
            weatherData = PlanMocks.GetWeatherDataMediator(true, 65);
            sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            result = sut.FilterForHumidity(projects, weatherData);
            result[0].Rejected.Should().BeFalse();
            target = result[0].Targets[0];
            target.Rejected.Should().BeFalse();
            target.ExposurePlans[0].Rejected.Should().BeTrue();
            target.ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterHumidity);
            target.ExposurePlans[1].Rejected.Should().BeFalse();
            target.ExposurePlans[2].Rejected.Should().BeFalse();

            weatherData = PlanMocks.GetWeatherDataMediator(true, 75);
            sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            result = sut.FilterForHumidity(projects, weatherData);
            result[0].Rejected.Should().BeFalse();
            target = result[0].Targets[0];
            target.Rejected.Should().BeFalse();
            target.ExposurePlans[0].Rejected.Should().BeTrue();
            target.ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterHumidity);
            target.ExposurePlans[1].Rejected.Should().BeTrue();
            target.ExposurePlans[1].RejectedReason.Should().Be(Reasons.FilterHumidity);
            target.ExposurePlans[2].Rejected.Should().BeFalse();

            weatherData = PlanMocks.GetWeatherDataMediator(true, 85);
            sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            result = sut.FilterForHumidity(projects, weatherData);
            result[0].Rejected.Should().BeTrue();
            target = result[0].Targets[0];
            target.Rejected.Should().BeTrue();
            target.RejectedReason.Should().Be(Reasons.TargetHumidity);
            target.ExposurePlans[0].Rejected.Should().BeTrue();
            target.ExposurePlans[0].RejectedReason.Should().Be(Reasons.FilterHumidity);
            target.ExposurePlans[1].Rejected.Should().BeTrue();
            target.ExposurePlans[1].RejectedReason.Should().Be(Reasons.FilterHumidity);
            target.ExposurePlans[2].Rejected.Should().BeTrue();
            target.ExposurePlans[2].RejectedReason.Should().Be(Reasons.FilterHumidity);
        }

        [Test]
        public void testGetTargetExposureRejectReason() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            DateTime atTime = new DateTime(2025, 1, 1, 18, 0, 0);
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            var sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);

            Mock<ITarget> pt1 = PlanMocks.GetMockPlanTarget("T1", TestData.M42);
            Mock<IExposure> pf1 = PlanMocks.GetMockPlanExposure("R", 10, 0);
            Mock<IExposure> pf2 = PlanMocks.GetMockPlanExposure("G", 10, 0);
            Mock<IExposure> pf3 = PlanMocks.GetMockPlanExposure("B", 10, 0);
            PlanMocks.AddMockPlanFilter(pt1, pf1);
            PlanMocks.AddMockPlanFilter(pt1, pf2);
            PlanMocks.AddMockPlanFilter(pt1, pf3);
            ITarget target = pt1.Object;
            IExposure ep1 = pf1.Object;
            IExposure ep2 = pf2.Object;
            IExposure ep3 = pf3.Object;
            ep1.Rejected = true;
            ep2.Rejected = true;
            ep3.Rejected = true;

            ep1.RejectedReason = Reasons.FilterComplete;
            ep2.RejectedReason = Reasons.FilterComplete;
            ep3.RejectedReason = Reasons.FilterComplete;
            sut.GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetComplete);

            ep1.RejectedReason = Reasons.FilterTwilight;
            ep2.RejectedReason = Reasons.FilterTwilight;
            ep3.RejectedReason = Reasons.FilterTwilight;
            sut.GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetTwilight);

            ep1.RejectedReason = Reasons.FilterMoonAvoidance;
            ep2.RejectedReason = Reasons.FilterMoonAvoidance;
            ep3.RejectedReason = Reasons.FilterMoonAvoidance;
            sut.GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetMoonAvoidance);

            ep1.RejectedReason = Reasons.FilterHumidity;
            ep2.RejectedReason = Reasons.FilterHumidity;
            ep3.RejectedReason = Reasons.FilterHumidity;
            sut.GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetHumidity);

            ep1.RejectedReason = Reasons.FilterHumidity;
            ep2.RejectedReason = Reasons.FilterHumidity;
            ep3.RejectedReason = Reasons.FilterComplete;
            sut.GetTargetExposureRejectReason(target).Should().Be(Reasons.TargetAllExposurePlans);
        }

        [Test]
        public void testAllRejectedForHumidity() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2025, 1, 1, 18, 0, 0);
            var sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);

            List<IProject> projects = new List<IProject>();
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt1 = PlanMocks.GetMockPlanTarget("T1", TestData.M42);
            pt1.SetupProperty(t => t.StartTime, atTime.AddHours(1));
            Mock<IExposure> pf1 = PlanMocks.GetMockPlanExposure("R", 10, 0);
            Mock<IExposure> pf2 = PlanMocks.GetMockPlanExposure("G", 10, 0);
            PlanMocks.AddMockPlanFilter(pt1, pf1);
            PlanMocks.AddMockPlanFilter(pt1, pf2);
            PlanMocks.AddMockPlanTarget(pp1, pt1);
            projects = PlanMocks.ProjectsList(pp1.Object);

            pf1.Object.Rejected = true;
            pf1.Object.RejectedReason = Reasons.FilterHumidity;

            projects = sut.PropagateRejections(projects);
            sut.AllRejectedForHumidity(projects).Should().BeFalse();

            pf2.Object.Rejected = true;
            pf2.Object.RejectedReason = Reasons.FilterHumidity;
            pt1.Object.Rejected = false;
            projects = sut.PropagateRejections(projects);
            sut.AllRejectedForHumidity(projects).Should().BeTrue();

            pf1.Object.RejectedReason = Reasons.FilterMoonAvoidance;
            pf2.Object.RejectedReason = Reasons.FilterMoonAvoidance;
            pt1.Object.Rejected = false;
            projects = sut.PropagateRejections(projects);
            sut.AllRejectedForHumidity(projects).Should().BeFalse();
        }

        [Test]
        public void testNextRejectedForHumidity() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2025, 1, 1, 18, 0, 0);
            var sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);

            List<IProject> projects = new List<IProject>();
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> mt1 = PlanMocks.GetMockPlanTarget("T1", TestData.M42);
            Mock<ITarget> mt2 = PlanMocks.GetMockPlanTarget("T2", TestData.M42);
            Mock<ITarget> mt3 = PlanMocks.GetMockPlanTarget("T3", TestData.M42);

            mt1.Object.StartTime = atTime.AddHours(2);
            mt1.Object.Rejected = true;
            mt1.Object.RejectedReason = Reasons.TargetHumidity;

            mt2.Object.StartTime = atTime.AddHours(3);
            mt2.Object.Rejected = true;
            mt2.Object.RejectedReason = Reasons.TargetHumidity;

            mt3.Object.StartTime = atTime.AddHours(1);
            mt3.Object.Rejected = true;
            mt3.Object.RejectedReason = Reasons.TargetHumidity;

            PlanMocks.AddMockPlanTarget(pp1, mt1);
            PlanMocks.AddMockPlanTarget(pp1, mt2);
            PlanMocks.AddMockPlanTarget(pp1, mt3);
            projects = PlanMocks.ProjectsList(pp1.Object);

            ITarget t = sut.NextRejectedForHumidity(projects);
            t.Should().BeNull(); // all in future

            mt3.Object.StartTime = atTime;
            t = sut.NextRejectedForHumidity(projects);
            t.Name.Should().Be("T3");
        }

        [Test]
        public void testTargetsReadyNow() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2023, 12, 25, 18, 0, 0);

            List<IProject> projects = new List<IProject>();
            List<ITarget> targets = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).GetTargetsReadyNow(projects);
            targets.Count.Should().Be(0);

            // 2 targets in future
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt1 = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt1.SetupProperty(t => t.StartTime, atTime.AddHours(1));
            Mock<IExposure> pf1 = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt1, pf1);
            PlanMocks.AddMockPlanTarget(pp1, pt1);

            Mock<ITarget> pt2 = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt2.SetupProperty(t => t.StartTime, atTime.AddHours(2));
            Mock<IExposure> pf2 = PlanMocks.GetMockPlanExposure("S2", 10, 0);
            PlanMocks.AddMockPlanFilter(pt2, pf2);
            PlanMocks.AddMockPlanTarget(pp1, pt2);

            projects = PlanMocks.ProjectsList(pp1.Object);

            targets = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).GetTargetsReadyNow(projects);
            targets.Count.Should().Be(0);

            // 2 targets ready now
            pt1.SetupProperty(t => t.StartTime, atTime.AddSeconds(10));
            pt2.SetupProperty(t => t.StartTime, atTime.AddSeconds(5));

            targets = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).GetTargetsReadyNow(projects);
            targets.Count.Should().Be(2);
        }

        [Test]
        public void testSelectTargetByScore() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2024, 12, 1, 20, 0, 0);

            List<ITarget> readyTargets = new List<ITarget>();
            Action score = () => new Planner(atTime, profile, GetPrefs(), weatherData, false, false).SelectTargetByScore(readyTargets, null);
            score.Should().Throw<ArgumentException>().WithMessage("no ready targets in SelectTargetByScore");

            ITarget t1 = PlanMocks.GetMockPlanTarget("T1", TestData.M31).Object;
            readyTargets.Add(t1);
            ITarget selected = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).SelectTargetByScore(readyTargets, null);
            (t1 == selected).Should().BeTrue();

            ITarget t2 = PlanMocks.GetMockPlanTarget("T2", TestData.M31).Object;
            readyTargets.Add(t2);
            selected = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).SelectTargetByScore(readyTargets, GetTestScoringEngine());

            (t2 == selected).Should().BeTrue();
            t1.Rejected.Should().BeTrue();
            t1.RejectedReason.Should().Be(Reasons.TargetLowerScore);
            t2.Rejected.Should().BeFalse();
        }

        [Test]
        public void testSelectTargetByScoreBreaksTiesById() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2024, 12, 1, 20, 0, 0);
            Mock<IScoringEngine> engine = new Mock<IScoringEngine>();
            engine.SetupAllProperties();
            engine.Setup(e => e.ScoreTarget(It.IsAny<ITarget>())).Returns(0.5);

            List<ITarget> Targets() {
                Mock<IProject> p1 = PlanMocks.GetMockPlanProject("p1", ProjectState.Active);
                p1.SetupProperty(p => p.DatabaseId, 10);
                Mock<ITarget> t1 = PlanMocks.GetMockPlanTarget("T1", TestData.M31);
                t1.SetupProperty(t => t.DatabaseId, 1);
                PlanMocks.AddMockPlanTarget(p1, t1);
                Mock<IProject> p2 = PlanMocks.GetMockPlanProject("p2", ProjectState.Active);
                p2.SetupProperty(p => p.DatabaseId, 20);
                Mock<ITarget> t2 = PlanMocks.GetMockPlanTarget("T2", TestData.M31);
                t2.SetupProperty(t => t.DatabaseId, 2);
                PlanMocks.AddMockPlanTarget(p2, t2);
                return new List<ITarget> { t1.Object, t2.Object };
            }

            Planner sut = new Planner(atTime, profile, GetPrefs(), weatherData, false, false);
            sut.SelectTargetByScore(Targets(), engine.Object).DatabaseId.Should().Be(1);
            List<ITarget> reversed = Targets();
            reversed.Reverse();
            sut.SelectTargetByScore(reversed, engine.Object).DatabaseId.Should().Be(1);
        }

        [Test]
        public void TheTieBandKeepsTheRunningTargetWithThe0217Scores() {
            var (running, challenger, ready) = TieBandTargets("Sharpless 216", "M 15");
            var planner = TieBandPlanner();

            ITarget selected = planner.SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 1.85883), (challenger, 1.85959)));

            selected.Should().BeSameAs(running);
            running.Rejected.Should().BeFalse();
            challenger.Rejected.Should().BeTrue();
            challenger.RejectedReason.Should().Be(Reasons.TargetHeldByRunning);
            running.ScoringResults.Held.Should().Be("M 15 scored only 0.04% higher (1.8596 vs 1.8588), and a switch needs more than 1%, so Sharpless 216 kept running");
        }

        [Test]
        public void AnExactTieThatTheIdOrderGivesTheChallengerKeepsTheRunningTarget() {
            var (challenger, running, ready) = TieBandTargets("A", "B");

            TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 1.5), (challenger, 1.5))).Should().BeSameAs(running);

            challenger.RejectedReason.Should().Be(Reasons.TargetHeldByRunning);
            running.ScoringResults.Held.Should().Be("A scored the same (1.5000 vs 1.5000), and a switch needs more than 1%, so B kept running");
        }

        [Test]
        public void ARunningTargetScoredZeroHoldsOnlyAgainstZero() {
            var (running, challenger, ready) = TieBandTargets("A", "B");
            TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 0.0), (challenger, 0.0001))).Should().BeSameAs(challenger);
        }

        [Test]
        public void TheHeldReasonNamesTheBandTheConstantSets() {
            Reasons.TargetHeldByRunning.Should().Contain($"within {Planner.TieBand * 100:0.#}%");
        }

        [Test]
        public void AChallengerOnePointFivePercentBetterWins() {
            var (running, challenger, ready) = TieBandTargets("A", "B");

            ITarget selected = TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 1.0), (challenger, 1.015)));

            selected.Should().BeSameAs(challenger);
            running.RejectedReason.Should().Be(Reasons.TargetLowerScore);
            challenger.ScoringResults.Held.Should().BeNull();
        }

        [Test]
        public void AChallengerExactlyOnePercentBetterDoesNotWin() {
            var (running, challenger, ready) = TieBandTargets("A", "B");

            TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 2.0), (challenger, 2.02))).Should().BeSameAs(running);
        }

        [Test]
        public void ARunningTargetThatIsNotReadyNeverHolds() {
            var (running, challenger, ready) = TieBandTargets("A", "B");
            ready.Remove(running);
            Mock<ITarget> third = PlanMocks.GetMockPlanTarget("C", TestData.M31);
            third.SetupProperty(t => t.DatabaseId, 3);
            third.Object.Project.DatabaseId = 30;
            ready.Add(third.Object);

            ITarget selected = TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 1.0), (challenger, 1.005), (third.Object, 0.9)));

            selected.Should().BeSameAs(challenger);
            third.Object.RejectedReason.Should().Be(Reasons.TargetLowerScore);
        }

        [Test]
        public void WithNoRunningTargetTheHighestScoreWins() {
            var (running, challenger, ready) = TieBandTargets("A", "B");

            TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(null, (running, 1.0), (challenger, 1.001))).Should().BeSameAs(challenger);
        }

        [Test]
        public void OnlyChallengersAboveTheRunningScoreAreHeldOff() {
            var (running, challenger, ready) = TieBandTargets("A", "B");
            Mock<ITarget> lower = PlanMocks.GetMockPlanTarget("C", TestData.M31);
            lower.SetupProperty(t => t.DatabaseId, 3);
            lower.Object.Project.DatabaseId = 30;
            ready.Add(lower.Object);

            TieBandPlanner().SelectTargetByScore(ready, TieBandEngine(Reloaded(running), (running, 1.0), (challenger, 1.004), (lower.Object, 0.99))).Should().BeSameAs(running);

            challenger.RejectedReason.Should().Be(Reasons.TargetHeldByRunning);
            lower.Object.RejectedReason.Should().Be(Reasons.TargetLowerScore);
        }

        private Planner TieBandPlanner() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            return new Planner(new DateTime(2024, 12, 1, 20, 0, 0), profileMock.Object.ActiveProfile, GetPrefs(), PlanMocks.GetWeatherDataMediator(false, 0), false, false);
        }

        private static (ITarget running, ITarget challenger, List<ITarget> ready) TieBandTargets(string runningName, string challengerName) {
            Mock<ITarget> running = PlanMocks.GetMockPlanTarget(runningName, TestData.M31);
            running.SetupProperty(t => t.DatabaseId, 1);
            running.Object.Project.DatabaseId = 10;
            Mock<ITarget> challenger = PlanMocks.GetMockPlanTarget(challengerName, TestData.M31);
            challenger.SetupProperty(t => t.DatabaseId, 2);
            challenger.Object.Project.DatabaseId = 20;
            return (running.Object, challenger.Object, new List<ITarget> { challenger.Object, running.Object });
        }

        private static ITarget Reloaded(ITarget target) {
            Mock<ITarget> copy = PlanMocks.GetMockPlanTarget(target.Name, TestData.M31);
            copy.SetupProperty(t => t.DatabaseId, target.DatabaseId);
            copy.Object.Project.DatabaseId = target.Project.DatabaseId;
            return copy.Object;
        }

        private static IScoringEngine TieBandEngine(ITarget previous, params (ITarget target, double score)[] scores) {
            Mock<IScoringEngine> engine = new Mock<IScoringEngine>();
            engine.SetupAllProperties();
            engine.Setup(e => e.PreviousPlanTarget).Returns(previous);
            engine.Setup(e => e.ScoreTarget(It.IsAny<ITarget>())).Returns((ITarget t) => {
                double score = scores.First(s => ReferenceEquals(s.target, t)).score;
                t.ScoringResults = new ScoringResults { TotalScore = score };
                return score;
            });
            return engine.Object;
        }

        [Test]
        public void testGetNextPossibleTarget() {
            Mock<IProfileService> profileMock = PlanMocks.GetMockProfileService(TestData.Pittsboro_NC);
            IProfile profile = profileMock.Object.ActiveProfile;
            IWeatherDataMediator weatherData = PlanMocks.GetWeatherDataMediator(false, 0);
            DateTime atTime = new DateTime(2023, 12, 25, 18, 0, 0);

            // 2 targets in future
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt1 = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt1.SetupProperty(t => t.StartTime, atTime.AddHours(1));
            Mock<IExposure> pf1 = PlanMocks.GetMockPlanExposure("Ha", 10, 0);
            PlanMocks.AddMockPlanFilter(pt1, pf1);
            PlanMocks.AddMockPlanTarget(pp1, pt1);

            Mock<ITarget> pt2 = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt2.SetupProperty(t => t.StartTime, atTime.AddHours(2));
            Mock<IExposure> pf2 = PlanMocks.GetMockPlanExposure("S2", 10, 0);
            PlanMocks.AddMockPlanFilter(pt2, pf2);
            PlanMocks.AddMockPlanTarget(pp1, pt2);

            List<IProject> projects = PlanMocks.ProjectsList(pp1.Object);
            ITarget target = new Planner(atTime, profile, GetPrefs(), weatherData, false, false).GetNextPossibleTarget(projects);
            target.Should().Be(pt1.Object);
            target.StartTime.Should().Be(atTime.AddHours(1));
        }

        private ProfilePreference GetPrefs(string profileId = "abcd-1234") {
            return new ProfilePreference(profileId);
        }

        private List<IProject> GetProjectForFilterTest() {
            Mock<IProject> pp1 = PlanMocks.GetMockPlanProject("pp1", ProjectState.Active);
            Mock<ITarget> pt = PlanMocks.GetMockPlanTarget("M42", TestData.M42);
            pt.SetupProperty(m => m.StartTime, new DateTime(2023, 12, 25, 18, 9, 0));
            pt.SetupProperty(m => m.EndTime, new DateTime(2023, 12, 26, 5, 17, 0));

            Mock<IExposure> pe = PlanMocks.GetMockPlanExposure("N", 10, 0);
            pe.SetupProperty(f => f.TwilightLevel, TwilightLevel.Nighttime);
            PlanMocks.AddMockPlanFilter(pt, pe);

            pe = PlanMocks.GetMockPlanExposure("A", 10, 0);
            pe.SetupProperty(f => f.TwilightLevel, TwilightLevel.Astronomical);
            PlanMocks.AddMockPlanFilter(pt, pe);

            pe = PlanMocks.GetMockPlanExposure("N", 10, 0);
            pe.SetupProperty(f => f.TwilightLevel, TwilightLevel.Nautical);
            PlanMocks.AddMockPlanFilter(pt, pe);

            PlanMocks.AddMockPlanTarget(pp1, pt);
            return PlanMocks.ProjectsList(pp1.Object);
        }

        private IScoringEngine GetTestScoringEngine() {
            Mock<IScoringEngine> mock = new Mock<IScoringEngine>();
            mock.SetupAllProperties();
            mock.Setup(p => p.ScoreTarget(It.IsAny<ITarget>())).Returns((ITarget t) => t.Name == "T1" ? .5 : .75);
            return mock.Object;
        }
    }
}