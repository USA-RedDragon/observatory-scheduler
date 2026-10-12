using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NINA.Plugin.TargetScheduler.Shared.Utility {

    public sealed class LogThrottle {
        public const int MaxKeys = 5000;

        private static readonly Regex Digits = new Regex(@"\d+", RegexOptions.CultureInvariant);

        private readonly TimeSpan interval;
        private readonly ConcurrentDictionary<string, DateTime> last = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        public LogThrottle(TimeSpan interval) {
            this.interval = interval;
        }

        public static string Key(string scope, string source, int line, string message) {
            return $"{scope}|{source}|{line}|{Digits.Replace(message ?? "", "#")}";
        }

        public bool Allow(string key, DateTime nowUtc) {
            if (last.TryGetValue(key, out DateTime previous) && nowUtc - previous < interval && nowUtc >= previous) {
                return false;
            }
            if (last.Count >= MaxKeys) {
                last.Clear();
            }
            last[key] = nowUtc;
            return true;
        }
    }
}
