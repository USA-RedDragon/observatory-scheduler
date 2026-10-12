using NINA.Astrometry;
using NINA.Plugin.TargetScheduler.Bodies;
using NINA.Plugin.TargetScheduler.Planning.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Plugin.TargetScheduler.Planning.Entities;
using System;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Planning {

    public static class BodyRecenter {

        public static string EndReason(ITarget target, DateTime atTime, BodyCenters centers, BodyField field) {
            if (target == null || !target.IsBody()) { return null; }
            Coordinates now = target.CoordinatesAt(atTime);
            if (now == null) { return $"moving body {target.Designation} has no position at {atTime}"; }

            BodyCenter center = centers?.Get(target.Guid, target.Designation);
            if (center == null) { return null; }

            BodyField f = field ?? BodyField.Default;
            double moved = BodyDrift.SeparationDegrees(center.Position, new BodyPosition(now.RADegrees, now.Dec));
            if (moved <= f.RecenterDegrees) { return null; }
            return $"moving body {target.Designation} moved {moved * 3600:0}\" since the center at {center.At:HH:mm:ss}, more than {f.RecenterDegrees * 3600:0}\" ({BodyField.RecenterFraction:P0} of the {f.ShortSideDegrees:0.00} deg short side), re-centering";
        }

        public static bool Required(ITarget target, DateTime atTime, BodyCenters centers, BodyField field) {
            if (target == null || target.IsPreview || !target.IsBody()) { return false; }
            return EndReason(target, atTime, centers, field) != null;
        }

        public static void AfterSetTarget(ITarget target, ITarget previousTarget, SchedulerPlan plan, bool slewCenterEnabled, BodyCenters centers) {
            if (target == null || plan == null || centers == null || !target.IsBody()) { return; }
            if (!slewCenterEnabled) {
                centers.Remove(target.Guid);
                return;
            }

            bool newTarget = previousTarget == null || !target.Equals(previousTarget);
            bool recenter = plan.PlanInstructions?.Any(i => i is PlanSlew) == true;
            if (!newTarget && !recenter) { return; }
            Coordinates coordinates = plan.PlanBodyCoordinates ?? (target.Frame() == null ? plan.PlanCoordinates : null) ?? target.CoordinatesAt(plan.PlanTime);
            if (coordinates == null) { return; }
            centers.Record(target.Guid, target.Designation, new BodyPosition(coordinates.RADegrees, coordinates.Dec), plan.PlanTime);
        }

        public static void RecordCenter(ITarget target, Coordinates coordinates, DateTime atTime) {
            if (target == null || coordinates == null || !target.IsBody()) { return; }
            BodyCenters.Live.Record(target.Guid, target.Designation, new BodyPosition(coordinates.RADegrees, coordinates.Dec), atTime);
        }

        public static void UpdateField(IProfile profile, int? widthPx, int? heightPx) {
            BodyField.Current = Field(profile, widthPx, heightPx);
        }

        public static BodyField Field(IProfile profile, ICameraMediator camera) {
            int? width = null;
            int? height = null;
            try {
                NINA.Equipment.Equipment.MyCamera.CameraInfo info = camera?.GetInfo();
                if (info != null && info.Connected) {
                    width = info.XSize;
                    height = info.YSize;
                }
            } catch (Exception) {
            }
            return Field(profile, width, height);
        }

        public static BodyField Field(IProfile profile, int? widthPx, int? heightPx) {
            double? pixelSize = null;
            double? focalLength = null;
            try {
                pixelSize = profile?.CameraSettings?.PixelSize;
                focalLength = profile?.TelescopeSettings?.FocalLength;
            } catch (Exception) {
            }
            return BodyField.From(pixelSize, focalLength, widthPx, heightPx);
        }
    }
}
