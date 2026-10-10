using CommunityToolkit.Mvvm.Input;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class RemoteOptionsVM : INotifyPropertyChanged {

        public RemoteOptionsVM() {
            NewTokenCommand = new RelayCommand(() => {
                if (MessageBox.Show("Make a new API token? The web UI stops working until its token is updated to match.", "Observatory Scheduler", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                    Update(d => d.ApiToken = RemoteSettings.NewToken());
                }
            });
            CopyTokenCommand = new RelayCommand(() => Clipboard.SetText(ApiToken ?? ""));
            var host = RemoteHost.Instance;
            if (host != null) {
                host.Settings.Changed += () => RaiseAll();
            }
        }

        private static RemoteSettingsData Data => RemoteHost.Instance?.Settings.Snapshot() ?? new RemoteSettingsData();

        private void Update(System.Action<RemoteSettingsData> change) {
            RemoteHost.Instance?.Settings.Update(change);
            RaiseAll();
        }

        public bool Available => RemoteHost.Instance != null;

        public bool ApiEnabled {
            get => Data.ApiEnabled;
            set => Update(d => d.ApiEnabled = value);
        }

        public int ApiPort {
            get => Data.ApiPort;
            set => Update(d => d.ApiPort = value);
        }

        public string ApiToken => Data.ApiToken;

        public bool WebEditing {
            get => Data.WebEditing;
            set => Update(d => d.WebEditing = value);
        }

        public string Summary {
            get {
                if (!Available) {
                    return "Web control failed to start; see the Target Scheduler log.";
                }
                var d = Data;
                var api = d.ApiEnabled ? $"The API listens on port {d.ApiPort} (http://<this PC>:{d.ApiPort}/os/v1)." : "The API is off.";
                var edit = d.WebEditing ? "Web editing is on: the web UI edits projects, targets and plans, and the Target Management editor here is read-only." : "Web editing is off: the web UI can skip, pause and re-plan, but edits are refused and the editor here works as usual.";
                return api + " " + edit;
            }
        }

        public ICommand NewTokenCommand { get; }
        public ICommand CopyTokenCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaiseAll() {
            void Fire() {
                foreach (var n in new[] { nameof(ApiEnabled), nameof(ApiPort), nameof(ApiToken), nameof(WebEditing), nameof(Summary), nameof(Available) }) {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
                }
            }
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) {
                app.Dispatcher.BeginInvoke(Fire);
            } else {
                Fire();
            }
        }
    }
}
