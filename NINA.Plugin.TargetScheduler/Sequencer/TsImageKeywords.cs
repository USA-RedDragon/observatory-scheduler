using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    public static class TsImageKeywords {
        public const string PROJECT_KEY = "TSPROJ";
        public const string TARGET_KEY = "TSTARGET";
        public const string EXPOSURE_PLAN_KEY = "TSEXPPLN";
        public const string PANEL_KEY = "TSPANEL";

        public const string PROJECT_COMMENT = "Target Scheduler project guid";
        public const string TARGET_COMMENT = "Target Scheduler target guid";
        public const string EXPOSURE_PLAN_COMMENT = "Target Scheduler exposure plan guid";
        public const string PANEL_COMMENT = "Mosaic panel number from the target name";

        private static readonly Regex PanelPattern = new Regex(@"(?i)\bpanel\s*(\d+)\s*$", RegexOptions.CultureInvariant);

        public static int? PanelNumber(string targetName) {
            if (string.IsNullOrWhiteSpace(targetName)) {
                return null;
            }

            Match match = PanelPattern.Match(targetName);
            if (!match.Success) {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int panel) ? panel : null;
        }

        public static List<(string Key, object Value, string Comment)> Build(string projectGuid, string targetGuid, string exposurePlanGuid, string targetName) {
            List<(string Key, object Value, string Comment)> keywords = new List<(string Key, object Value, string Comment)>();

            if (!string.IsNullOrWhiteSpace(projectGuid)) {
                keywords.Add((PROJECT_KEY, projectGuid, PROJECT_COMMENT));
            }

            if (!string.IsNullOrWhiteSpace(targetGuid)) {
                keywords.Add((TARGET_KEY, targetGuid, TARGET_COMMENT));
            }

            if (!string.IsNullOrWhiteSpace(exposurePlanGuid)) {
                keywords.Add((EXPOSURE_PLAN_KEY, exposurePlanGuid, EXPOSURE_PLAN_COMMENT));
            }

            int? panel = PanelNumber(targetName);
            if (panel.HasValue) {
                keywords.Add((PANEL_KEY, panel.Value, PANEL_COMMENT));
            }

            return keywords;
        }
    }
}
