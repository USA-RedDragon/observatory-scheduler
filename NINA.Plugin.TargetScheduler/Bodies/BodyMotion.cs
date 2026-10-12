using System;
using System.Collections.Concurrent;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public sealed class BodyField {
        public const double DefaultImageScale = 1.91;
        public const double DefaultShortSideDegrees = 2.2;
        public const double RecenterFraction = 0.1;
        public const double MaxTrailPixels = 1.0;

        public static readonly BodyField Default = new BodyField(DefaultImageScale, DefaultShortSideDegrees, null, null);

        private static volatile BodyField current = Default;

        public static BodyField Current {
            get => current;
            set => current = value ?? Default;
        }

        public double ImageScale { get; }
        public double ShortSideDegrees { get; }
        public int? WidthPx { get; }
        public int? HeightPx { get; }

        public double RecenterDegrees => ShortSideDegrees * RecenterFraction;

        private BodyField(double imageScale, double shortSideDegrees, int? widthPx, int? heightPx) {
            ImageScale = imageScale;
            ShortSideDegrees = shortSideDegrees;
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        public static BodyField From(double? pixelSizeUm, double? focalLengthMm, int? widthPx, int? heightPx) {
            int? width = widthPx > 0 ? widthPx : null;
            int? height = heightPx > 0 ? heightPx : null;
            if (!Positive(pixelSizeUm) || !Positive(focalLengthMm)) {
                return new BodyField(DefaultImageScale, DefaultShortSideDegrees, width, height);
            }

            double scale = 206.265 * pixelSizeUm.Value / focalLengthMm.Value;
            double shortSide = width.HasValue && height.HasValue ? Math.Min(width.Value, height.Value) * scale / 3600 : DefaultShortSideDegrees;
            return new BodyField(scale, shortSide, width, height);
        }

        private static bool Positive(double? v) {
            return v.HasValue && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value) && v.Value > 0;
        }
    }

    public static class BodyDrift {

        public static double SeparationDegrees(BodyPosition a, BodyPosition b) {
            double r = Math.PI / 180;
            double sd = Math.Sin((b.Dec - a.Dec) * r / 2);
            double sa = Math.Sin((b.Ra - a.Ra) * r / 2);
            double h = sd * sd + Math.Cos(a.Dec * r) * Math.Cos(b.Dec * r) * sa * sa;
            return 2 * Math.Asin(Math.Min(1, Math.Sqrt(h))) / r;
        }

        public static bool NeedsRecenter(BodyPosition centered, BodyPosition now, BodyField field) {
            return SeparationDegrees(centered, now) > (field ?? BodyField.Default).RecenterDegrees;
        }
    }

    public sealed class BodyCenter {
        public string Designation { get; set; }
        public BodyPosition Position { get; set; }
        public DateTime At { get; set; }
    }

    public class BodyCenters {
        public static BodyCenters Live { get; } = new BodyCenters();

        private readonly ConcurrentDictionary<string, BodyCenter> centers = new ConcurrentDictionary<string, BodyCenter>(StringComparer.Ordinal);

        public void Record(string targetGuid, string designation, BodyPosition position, DateTime at) {
            if (string.IsNullOrWhiteSpace(targetGuid) || string.IsNullOrWhiteSpace(designation)) { return; }
            centers[TargetBodyTable.Key(targetGuid)] = new BodyCenter { Designation = designation.Trim(), Position = position, At = at };
        }

        public BodyCenter Get(string targetGuid, string designation) {
            if (string.IsNullOrWhiteSpace(targetGuid)) { return null; }
            return centers.TryGetValue(TargetBodyTable.Key(targetGuid), out BodyCenter c) && TargetBodyTable.SameDesignation(c.Designation, designation) ? c : null;
        }

        public void Remove(string targetGuid) {
            if (string.IsNullOrWhiteSpace(targetGuid)) { return; }
            centers.TryRemove(TargetBodyTable.Key(targetGuid), out _);
        }

        public void Clear() {
            centers.Clear();
        }
    }
}
