using LinqKit;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Plugin.TargetScheduler.Database.Schema;
using NINA.Plugin.TargetScheduler.Remote.Plugin;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using RelayCommandParam = CommunityToolkit.Mvvm.Input.RelayCommand<object>;

namespace NINA.Plugin.TargetScheduler.Controls.DatabaseManager {

    public class OrphanedExposureTemplatesViewVM : BaseVM {
        private DatabaseManagerVM managerVM;
        private TreeDataItem parentItem;

        public OrphanedExposureTemplatesViewVM(DatabaseManagerVM managerVM, IProfileService profileService, TreeDataItem profileItem, List<ExposureTemplate> orphanedExposureTemplates) : base(profileService) {
            this.managerVM = managerVM;
            parentItem = profileItem;

            exposureTemplates = new ObservableCollection<ExposureTemplate>();
            orphanedExposureTemplates.ForEach(et => exposureTemplates.Add(et));

            MoveExposureTemplateCommand = LocalEditing.Gate(new RelayCommandParam(MoveExposureTemplates));
            DeleteExposureTemplateCommand = LocalEditing.Gate(new RelayCommandParam(DeleteExposureTemplates));

            InitializeProfilesList();
        }

        private ObservableCollection<ExposureTemplate> exposureTemplates;

        public ObservableCollection<ExposureTemplate> ExposureTemplates {
            get => exposureTemplates;
            set {
                exposureTemplates = value;
                RaisePropertyChanged(nameof(ExposureTemplates));
            }
        }

        private AsyncObservableCollection<KeyValuePair<string, string>> profileChoices;

        public AsyncObservableCollection<KeyValuePair<string, string>> ProfileChoices {
            get {
                return profileChoices;
            }
            set {
                profileChoices = value;
            }
        }

        private string selectedProfileId;

        public string SelectedProfileId {
            get => selectedProfileId;
            set {
                selectedProfileId = value;
                RaisePropertyChanged(nameof(SelectedProfileId));
            }
        }

        private void InitializeProfilesList() {
            List<ProfileMeta> profiles = new List<ProfileMeta>();
            profileService.Profiles.ForEach(p => profiles.Add(p));

            ProfileChoices = new AsyncObservableCollection<KeyValuePair<string, string>>();
            profiles.ForEach(p => ProfileChoices.Add(new KeyValuePair<string, string>(p.Id.ToString(), p.Name)));
            RaisePropertyChanged(nameof(ProfileChoices));
        }

        public ICommand MoveExposureTemplateCommand { get; private set; }
        public ICommand DeleteExposureTemplateCommand { get; private set; }

        private void MoveExposureTemplates(object obj) {
            if (!LocalEditing.CanEdit) {
                return;
            }

            if (string.IsNullOrEmpty(SelectedProfileId)) {
                MyMessageBox.Show("You must select a destination profile to move an Exposure Template.", "Oops");
                return;
            }

            ExposureTemplate exposureTemplate = obj as ExposureTemplate;
            if (exposureTemplate != null) {
                string selectedProfileName = ProfileChoices.Where(p => p.Key == SelectedProfileId).FirstOrDefault().Value;
                if (MyMessageBox.Show($"Orphaned Exposure Template '{exposureTemplate.Name}' will be moved to profile '{selectedProfileName}'.  Are you sure?", "Move Project?", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes) {
                    TSLogger.Info($"moving orphaned Exposure Template '{exposureTemplate.Name}' to '{selectedProfileName}'");
                    if (managerVM.MoveOrphanedExposureTemplate(exposureTemplate, SelectedProfileId)) {
                        ExposureTemplates.Remove(exposureTemplate);
                        RaisePropertyChanged(nameof(ExposureTemplates));
                    }
                }
            }
        }

        private void DeleteExposureTemplates(object obj) {
            if (!LocalEditing.CanEdit) {
                return;
            }

            ExposureTemplate exposureTemplate = obj as ExposureTemplate;
            if (exposureTemplate != null) {
                if (MyMessageBox.Show($"Orphaned Exposure Template '{exposureTemplate.Name}' will be permanently deleted.  Are you sure?", "Delete Exposure Template?", MessageBoxButton.YesNo, MessageBoxResult.No) == MessageBoxResult.Yes) {
                    TSLogger.Info($"deleting orphaned Exposure Template '{exposureTemplate.Name}'");
                    if (managerVM.DeleteOrphanedExposureTemplate(exposureTemplate)) {
                        ExposureTemplates.Remove(exposureTemplate);
                        RaisePropertyChanged(nameof(ExposureTemplates));
                    }
                }
            }
        }
    }
}