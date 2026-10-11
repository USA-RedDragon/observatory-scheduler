using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public class LocalEditing : INotifyPropertyChanged {
        public static LocalEditing Instance { get; } = new LocalEditing();

        private bool allowed = true;
        private static volatile bool commandsBound;

        public bool Allowed {
            get => allowed;
            set {
                if (allowed == value) {
                    return;
                }
                allowed = value;
                Raise();
                Raise(nameof(BannerVisibility));
                Requery();
            }
        }

        /// <summary>
        /// True when the local Target Scheduler editors may write. Write actions return early when this is false.
        /// </summary>
        public static bool CanEdit => Instance.Allowed;

        /// <summary>
        /// Wraps a write command so it can only run while local editing is allowed.
        /// </summary>
        public static ICommand Gate(ICommand command) => new GatedCommand(command);

        public Visibility BannerVisibility => allowed ? Visibility.Collapsed : Visibility.Visible;

        public string Banner => "Web editing is on, so this editor is read-only. Edit projects, targets and plans in the web app.";

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

        private static void Requery() {
            if (!commandsBound) {
                return;
            }
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) {
                CommandManager.InvalidateRequerySuggested();
            } else {
                dispatcher.BeginInvoke(new Action(CommandManager.InvalidateRequerySuggested));
            }
        }

        private sealed class GatedCommand : ICommand {
            private readonly ICommand inner;

            public GatedCommand(ICommand inner) {
                this.inner = inner;
            }

            public event EventHandler CanExecuteChanged {
                add {
                    commandsBound = true;
                    inner.CanExecuteChanged += value;
                    CommandManager.RequerySuggested += value;
                }
                remove {
                    inner.CanExecuteChanged -= value;
                    CommandManager.RequerySuggested -= value;
                }
            }

            public bool CanExecute(object parameter) => CanEdit && inner.CanExecute(parameter);

            public void Execute(object parameter) {
                if (!CanEdit) {
                    return;
                }
                inner.Execute(parameter);
            }
        }
    }
}
