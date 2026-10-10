using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    [Export(typeof(ResourceDictionary))]
    public partial class TargetSchedulerDarksReadyTemplate {

        public TargetSchedulerDarksReadyTemplate() {
            InitializeComponent();
        }
    }
}