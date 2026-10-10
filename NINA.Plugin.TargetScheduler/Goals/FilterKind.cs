using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Plugin.TargetScheduler.Goals {

    public static class FilterKind {
        private static readonly Regex Token = new Regex("[a-z0-9]+", RegexOptions.Compiled);

        private static readonly string[] NarrowTokens = {
            "ha", "halpha", "hydrogen", "oiii", "o3", "oxygen", "sii", "s2", "sulfur", "sulphur", "hb", "hbeta", "nii", "n2",
            "nb", "narrow", "narrowband", "lextreme", "lenhance", "lultimate", "alpt", "nbz", "duo", "tri", "quad", "dualband", "triband",
            "quadband", "hao3", "hoo", "sho", "hos", "ohs"
        };

        public static bool IsNarrowband(string filterName) {
            if (string.IsNullOrWhiteSpace(filterName)) { return false; }

            string lower = filterName.Trim().ToLowerInvariant();
            string squashed = new string(lower.Where(char.IsLetterOrDigit).ToArray());
            if (NarrowTokens.Contains(squashed)) { return true; }
            if (squashed.StartsWith("halpha", StringComparison.Ordinal) || squashed.StartsWith("hbeta", StringComparison.Ordinal)) { return true; }

            foreach (Match m in Token.Matches(lower)) {
                if (NarrowTokens.Contains(m.Value)) { return true; }
            }

            return Regex.IsMatch(lower, @"\b(\d{1,2}(\.\d)?)\s*nm\b");
        }
    }
}
