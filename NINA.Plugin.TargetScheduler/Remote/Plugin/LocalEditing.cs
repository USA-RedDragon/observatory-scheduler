using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class LocalEditing : INotifyPropertyChanged {
        public static LocalEditing Instance { get; } = new LocalEditing();

        private bool allowed = true;

        public bool Allowed {
            get => allowed;
            set {
                if (allowed == value) {
                    return;
                }
                allowed = value;
                Raise();
                Raise(nameof(BannerVisibility));
            }
        }

        public Visibility BannerVisibility => allowed ? Visibility.Collapsed : Visibility.Visible;

        public string Banner => "Web editing is on, so this editor is read-only. Edit projects, targets and plans in the web UI, or turn web editing off in the Web control section below.";

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise([CallerMemberName] string name = null) {
            var h = PropertyChanged;
            if (h == null) {
                return;
            }
            var app = Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess()) {
                app.Dispatcher.BeginInvoke(() => h(this, new PropertyChangedEventArgs(name)));
            } else {
                h(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
