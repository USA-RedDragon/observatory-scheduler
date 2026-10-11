using FluentAssertions;
using Moq;
using NINA.Plugin.TargetScheduler.Controls.DatabaseManager;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Remote.Plugin;
using NINA.Profile.Interfaces;
using NUnit.Framework;
using System.Reflection;
using System.Runtime.CompilerServices;

using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using RelayCommandParam = CommunityToolkit.Mvvm.Input.RelayCommand<object>;

namespace NINA.Plugin.TargetScheduler.Test.Controls.DatabaseManager {

    [TestFixture]
    [NonParallelizable]
    public class LocalEditingTest {

        [TearDown]
        public void TearDown() {
            LocalEditing.Instance.Allowed = true;
        }

        [Test]
        public void testGateFollowsAllowed() {
            int runs = 0;
            var cmd = LocalEditing.Gate(new RelayCommand(() => runs++));

            LocalEditing.CanEdit.Should().BeTrue();
            cmd.CanExecute(null).Should().BeTrue();
            cmd.Execute(null);
            runs.Should().Be(1);

            LocalEditing.Instance.Allowed = false;
            LocalEditing.CanEdit.Should().BeFalse();
            cmd.CanExecute(null).Should().BeFalse();
            cmd.Execute(null);
            runs.Should().Be(1);

            LocalEditing.Instance.Allowed = true;
            cmd.CanExecute(null).Should().BeTrue();
            cmd.Execute(null);
            runs.Should().Be(2);
        }

        [Test]
        public void testGateKeepsInnerCanExecute() {
            var cmd = LocalEditing.Gate(new RelayCommandParam(_ => { }, p => p != null));
            cmd.CanExecute(null).Should().BeFalse();
            cmd.CanExecute("x").Should().BeTrue();

            LocalEditing.Instance.Allowed = false;
            cmd.CanExecute("x").Should().BeFalse();
        }

        [Test]
        public void testProfilePreferencesRefusedWhenNotAllowed() {
            var sut = new ProfilePreferencesViewVM(null, new Mock<IProfileService>().Object, new ProfilePreference("p1"), "p");

            LocalEditing.Instance.Allowed = false;
            sut.EditCommand.CanExecute(null).Should().BeFalse();
            sut.SaveCommand.CanExecute(null).Should().BeFalse();
            sut.CancelCommand.CanExecute(null).Should().BeTrue();

            sut.EditCommand.Execute(null);
            Invoke(sut, "Edit");
            sut.ShowEditView.Should().BeFalse();

            sut.SaveCommand.Execute(null);
            Invoke(sut, "Save");
            sut.ShowEditView.Should().BeFalse();
        }

        [Test]
        public void testProfilePreferencesEditWhenAllowed() {
            var manager = NewManager();
            var sut = new ProfilePreferencesViewVM(manager, new Mock<IProfileService>().Object, new ProfilePreference("p1"), "p");

            sut.EditCommand.CanExecute(null).Should().BeTrue();
            sut.EditCommand.Execute(null);
            sut.ShowEditView.Should().BeTrue();
            manager.TreeViewEabled.Should().BeFalse();
        }

        [Test]
        public void testCancelLocalEditsDiscardsEditInProgress() {
            var manager = NewManager();
            var sut = new ProfilePreferencesViewVM(manager, new Mock<IProfileService>().Object, new ProfilePreference("p1"), "p");
            manager.ProfilePreferencesViewVM = sut;

            sut.EditCommand.Execute(null);
            sut.ProfilePreferenceProxy.Proxy.ParkOnWait = true;
            sut.ItemEdited.Should().BeTrue();

            LocalEditing.Instance.Allowed = false;
            Invoke(sut, "Save");
            sut.ShowEditView.Should().BeTrue();
            sut.ProfilePreferenceProxy.Original.ParkOnWait.Should().BeFalse();

            manager.CancelLocalEdits();
            sut.ShowEditView.Should().BeFalse();
            sut.ItemEdited.Should().BeFalse();
            sut.ProfilePreferenceProxy.Proxy.ParkOnWait.Should().BeFalse();
            manager.TreeViewEabled.Should().BeTrue();
        }

        [Test]
        public void testManagerWritesReturnEarlyWhenNotAllowed() {
            var manager = NewManager();

            LocalEditing.Instance.Allowed = false;
            manager.AddNewProject(null).Should().BeNull();
            manager.ResetTarget(null).Should().BeNull();
            manager.ToggleExposurePlan(null, null).Should().BeNull();
            manager.DeleteExposurePlan(null, null).Should().BeNull();
            manager.DeleteAllExposurePlans(null).Should().BeNull();
            manager.MoveOrphanedProject(null, "x").Should().BeFalse();
            manager.DeleteOrphanedProject(null).Should().BeFalse();
            manager.MoveOrphanedExposureTemplate(null, "x").Should().BeFalse();
            manager.DeleteOrphanedExposureTemplate(null).Should().BeFalse();
            manager.PasteProject(null);
            manager.SaveProject(null);
            manager.DeleteProject(null);
            manager.AddNewTarget(null);
            manager.AddTargets(null, null);
            manager.SaveTarget(null);
            manager.PasteTarget(null);
            manager.MoveTarget(null);
            manager.GradeTarget(null);
            manager.ResetProfile(null);
            manager.ResetProjectTargets(null);
            manager.DeleteTarget(null, true);
            manager.AddNewExposureTemplate(null);
            manager.PasteExposureTemplate(null);
            manager.SaveExposureTemplate(null);
            manager.DeleteExposureTemplate(null);
            manager.SaveProfilePreference(null);
            manager.CopyItem();
        }

        private static DatabaseManagerVM NewManager() {
            return (DatabaseManagerVM)RuntimeHelpers.GetUninitializedObject(typeof(DatabaseManagerVM));
        }

        private static void Invoke(object target, string method) {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        }
    }
}
