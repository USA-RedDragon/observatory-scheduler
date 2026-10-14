using System;
using System.ComponentModel;
using System.Windows;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class CoolerOptionsVM : INotifyPropertyChanged {
        private string problem;

        public CoolerOptionsVM() {
            CoolerRuntime.Settings.Changed += RaiseAll;
        }

        private static CoolerSettingsData Data => CoolerRuntime.Settings.Snapshot();

        private void Update(Action<CoolerSettingsData> change) {
            Problem = CoolerRuntime.Settings.Update(change);
            RaiseAll();
        }

        public bool Enabled {
            get => Data.Enabled;
            set => Update(d => d.Enabled = value);
        }

        public double TargetPower {
            get => Data.TargetPower;
            set => Update(d => d.TargetPower = value);
        }

        public double LowPower {
            get => Data.LowPower;
            set => Update(d => d.LowPower = value);
        }

        public double HighPower {
            get => Data.HighPower;
            set => Update(d => d.HighPower = value);
        }

        public double MaxPredictedPower {
            get => Data.MaxPredictedPower;
            set => Update(d => d.MaxPredictedPower = value);
        }

        public string Problem {
            get => problem;
            private set {
                problem = value == null ? null : $"Not saved: {value}.";
                Raise(nameof(Problem));
                Raise(nameof(HasProblem));
            }
        }

        public bool HasProblem => problem != null;

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void RaiseAll() {
            void Fire() {
                foreach (var n in new[] { nameof(Enabled), nameof(TargetPower), nameof(LowPower), nameof(HighPower), nameof(MaxPredictedPower) }) {
                    Raise(n);
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
