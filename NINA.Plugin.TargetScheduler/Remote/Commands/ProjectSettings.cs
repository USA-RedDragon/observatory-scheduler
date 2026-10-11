using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Commands {

    public static class ProjectSettings {
        public const string DitherEvery = "ditherevery";
        public const string FilterSwitchFrequency = "filterswitchfrequency";
        public const string FlatsHandling = "flatshandling";
        public const string SmartExposureOrder = "smartexposureorder";
        public const string MeridianWindow = "meridianwindow";
        public const string UseCustomHorizon = "usecustomhorizon";
        public const string HorizonOffset = "horizonoffset";
        public const string MaximumAltitude = "maximumaltitude";
        public const string EnableGrader = "enablegrader";

        public static readonly IReadOnlyList<EditableColumn> Columns = new[] {
            new EditableColumn(DitherEvery, "ditherevery", ColumnType.Integer, 0, 999),
            new EditableColumn(FilterSwitchFrequency, "filterswitchfrequency", ColumnType.Integer, 0, 999),
            new EditableColumn(FlatsHandling, "flatsHandling", ColumnType.Integer, 0, 99, 100, 200),
            new EditableColumn(SmartExposureOrder, "smartexposureorder", ColumnType.Boolean),
            new EditableColumn(MeridianWindow, "meridianwindow", ColumnType.Integer, 0, 720),
            new EditableColumn(UseCustomHorizon, "usecustomhorizon", ColumnType.Boolean),
            new EditableColumn(HorizonOffset, "horizonoffset", ColumnType.Real, 0, 90),
            new EditableColumn(MaximumAltitude, "maximumAltitude", ColumnType.Real, 0, 90),
            new EditableColumn(EnableGrader, "enablegrader", ColumnType.Boolean),
        };

        public static readonly IReadOnlyDictionary<string, double> TargetSchedulerDefaults = new Dictionary<string, double> {
            [DitherEvery] = 0,
            [FilterSwitchFrequency] = 0,
            [FlatsHandling] = 0,
            [SmartExposureOrder] = 0,
            [MeridianWindow] = 0,
            [UseCustomHorizon] = 0,
            [HorizonOffset] = 0,
            [MaximumAltitude] = 0,
            [EnableGrader] = 1,
        };

        public static EditableColumn Column(string field) {
            return Columns.First(c => c.Field == field);
        }

        public static Dictionary<string, double> Given(NewProject p) {
            var d = new Dictionary<string, double>();
            void Add(string f, double? v) {
                if (v.HasValue) {
                    d[f] = v.Value;
                }
            }
            Add(DitherEvery, p.DitherEvery);
            Add(FilterSwitchFrequency, p.FilterSwitchFrequency);
            Add(FlatsHandling, p.FlatsHandling);
            Add(SmartExposureOrder, p.SmartExposureOrder.HasValue ? (p.SmartExposureOrder.Value ? 1 : 0) : null);
            Add(MeridianWindow, p.MeridianWindow);
            Add(UseCustomHorizon, p.UseCustomHorizon.HasValue ? (p.UseCustomHorizon.Value ? 1 : 0) : null);
            Add(HorizonOffset, p.HorizonOffset);
            Add(MaximumAltitude, p.MaximumAltitude);
            Add(EnableGrader, p.EnableGrader.HasValue ? (p.EnableGrader.Value ? 1 : 0) : null);
            return d;
        }

        public static string Check(NewProject p) {
            foreach (var kv in Given(p)) {
                var error = Column(kv.Key).Check(kv.Value);
                if (error != null) {
                    return error;
                }
            }
            return null;
        }
    }
}
