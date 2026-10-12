using System;

namespace NINA.Plugin.TargetScheduler.Bodies {

    public sealed class BodyFrame {
        public const double MaxOffsetFrac = 0.5;

        public double RotationDeg { get; }
        public double OffsetFrac { get; }

        private BodyFrame(double rotationDeg, double offsetFrac) {
            RotationDeg = rotationDeg;
            OffsetFrac = offsetFrac;
        }

        public static string Check(double? rotationDeg, double? offsetFrac) {
            if (!rotationDeg.HasValue && !offsetFrac.HasValue) { return null; }
            if (!rotationDeg.HasValue || !offsetFrac.HasValue) { return "rotation_deg and offset_frac go together"; }
            if (!Finite(rotationDeg.Value) || rotationDeg.Value < 0 || rotationDeg.Value >= 360) { return "rotation_deg runs from 0 to under 360"; }
            if (!Finite(offsetFrac.Value) || Math.Abs(offsetFrac.Value) > MaxOffsetFrac) { return $"offset_frac runs from -{MaxOffsetFrac} to {MaxOffsetFrac}"; }
            return null;
        }

        public static BodyFrame From(double? rotationDeg, double? offsetFrac) {
            if (!rotationDeg.HasValue || !offsetFrac.HasValue || Check(rotationDeg, offsetFrac) != null) { return null; }
            return new BodyFrame(rotationDeg.Value, offsetFrac.Value);
        }

        public BodyPosition Center(BodyPosition body, double longSideDegrees) {
            return BodyFraming.Move(body, RotationDeg, -OffsetFrac * longSideDegrees);
        }

        public double NinaPositionAngle(int? widthPx, int? heightPx) {
            bool tall = widthPx.HasValue && heightPx.HasValue && heightPx.Value > widthPx.Value;
            return BodyFraming.Normalize(tall ? RotationDeg : RotationDeg + 90);
        }

        private static bool Finite(double v) {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }

    public static class BodyFraming {

        public static double Normalize(double degrees) {
            double d = degrees % 360;
            if (d < 0) { d += 360; }
            return d == 360 ? 0 : d;
        }

        public static BodyPosition Move(BodyPosition position, double paDegrees, double distanceDegrees) {
            if (distanceDegrees == 0) { return position; }
            double r = Math.PI / 180;
            double dec = position.Dec * r;
            double d = distanceDegrees * r;
            double pa = paDegrees * r;
            double sinDec = Math.Sin(dec) * Math.Cos(d) + Math.Cos(dec) * Math.Sin(d) * Math.Cos(pa);
            double dec2 = Math.Asin(Math.Max(-1, Math.Min(1, sinDec)));
            double dra = Math.Atan2(Math.Sin(pa) * Math.Sin(d) * Math.Cos(dec), Math.Cos(d) - Math.Sin(dec) * Math.Sin(dec2));
            return new BodyPosition(Normalize(position.Ra + dra / r), dec2 / r);
        }

        public static double PositionAngle(BodyPosition a, BodyPosition b) {
            double r = Math.PI / 180;
            double dra = (b.Ra - a.Ra) * r;
            double y = Math.Sin(dra) * Math.Cos(b.Dec * r);
            double x = Math.Cos(a.Dec * r) * Math.Sin(b.Dec * r) - Math.Sin(a.Dec * r) * Math.Cos(b.Dec * r) * Math.Cos(dra);
            return Normalize(Math.Atan2(y, x) / r);
        }
    }
}
