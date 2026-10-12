using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using System;

namespace NINA.Plugin.TargetScheduler.Planning.Interfaces {

    public static class TargetCoordinates {

        public static bool IsBody(this ITarget target) {
            return !string.IsNullOrWhiteSpace(target?.Designation);
        }

        public static TargetWindowRow Window(this ITarget target) {
            return target == null ? null : TargetWindowTable.Current.Get(target.Guid);
        }

        public static BodyEphemeris Ephemeris(this ITarget target) {
            if (!target.IsBody()) { return null; }
            return target.BodyEphemeris ?? BodyPositionTable.Current.Ephemeris(target.Guid, target.Designation);
        }

        public static string PositionUnknownReason(this ITarget target, DateTime time) {
            if (target == null || !target.IsBody() || target.CoordinatesAt(time) != null) { return null; }
            return $"moving body {target.Name} ({target.Designation}) has no position at {time}";
        }

        public static Coordinates CoordinatesAt(this ITarget target, DateTime time) {
            if (target == null) { return null; }
            if (!target.IsBody()) { return target.Coordinates; }
            BodyPosition? position = target.Ephemeris()?.At(time);
            return position.HasValue ? new Coordinates(Angle.ByDegree(position.Value.Ra), Angle.ByDegree(position.Value.Dec), Epoch.J2000) : null;
        }
    }
}
