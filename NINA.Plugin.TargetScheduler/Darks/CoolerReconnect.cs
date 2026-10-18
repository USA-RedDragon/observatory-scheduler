using System;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// What to do when the camera connects again while the plugin owns the cooler: the cooler may come back off, or on at the
    /// old cold setpoint, either of which moves the sensor fast. The setpoint is ramped from the sensor temperature back to the
    /// last setpoint at the darks' 1 °C per minute instead.
    /// </summary>
    public static class ReconnectRestore {
        public const double CloseEnoughC = 1.0;

        /// <summary>The setpoint to ramp back to, or null with the reason to leave the camera alone.</summary>
        public static (double? Target, string Reason) Decide(bool owned, double? lastSetpoint, bool connected, bool canSetTemperature, bool coolerOn, double sensor, double setpoint) {
            if (!owned) {
                return (null, "the plugin does not own the cooler");
            }
            if (!lastSetpoint.HasValue || double.IsNaN(lastSetpoint.Value)) {
                return (null, "no setpoint was seen before the disconnect");
            }
            if (!connected || !canSetTemperature) {
                return (null, connected ? "the camera cannot set its temperature" : "the camera is not connected");
            }
            if (double.IsNaN(sensor)) {
                return (null, DarkPlanner.SensorUnknown);
            }
            if (coolerOn && DarkPlanner.SameSetpoint(setpoint, lastSetpoint.Value) && Math.Abs(sensor - lastSetpoint.Value) <= CloseEnoughC) {
                return (null, string.Format(CultureInfo.InvariantCulture, "the cooler is on at {0} °C and the sensor is at {1:0.0} °C", setpoint, sensor));
            }
            return (lastSetpoint.Value, string.Format(CultureInfo.InvariantCulture, "the cooler came back {0}, sensor {1:0.0} °C; ramping from the sensor to {2} °C over {3:0.##} min",
                coolerOn ? string.Format(CultureInfo.InvariantCulture, "on at {0} °C", setpoint) : "off", sensor, lastSetpoint.Value, DarkPlanner.Ramp(sensor, lastSetpoint.Value).TotalMinutes));
        }
    }
}
