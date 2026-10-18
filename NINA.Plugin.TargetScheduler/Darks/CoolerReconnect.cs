using System;
using System.Globalization;

namespace NINA.Plugin.TargetScheduler.Darks {

    /// <summary>
    /// Remembers the setpoint the cooler last held, from the camera updates. NINA sends the first update of a new connection
    /// before it raises its Connected event, so the setpoint from before the disconnect is kept aside at that first update
    /// (an update after a disconnected one or after a gap of <see cref="ReconnectGap"/>), before the new connection's cooler
    /// state overwrites it. A cooler seen off for <see cref="CoolerOffAfter"/> in one connection was turned off on purpose
    /// and is not restored.
    /// </summary>
    public class SetpointMemory {
        public static readonly TimeSpan ReconnectGap = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan CoolerOffAfter = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ReconnectValidFor = TimeSpan.FromMinutes(1);

        private readonly object sync = new object();
        private double? last;
        private double? atReconnect;
        private DateTime? reconnectAt;
        private DateTime? lastUpdate;
        private bool lastConnected;
        private DateTime? coolerOffSince;

        public double? Last {
            get {
                lock (sync) {
                    return last;
                }
            }
        }

        /// <summary>Observes one camera update; returns true when it is the first update of a new connection.</summary>
        public bool Observe(DateTime now, bool connected, bool canSetTemperature, bool coolerOn, double setpoint) {
            lock (sync) {
                bool boundary = connected && (!lastConnected || !lastUpdate.HasValue || now - lastUpdate.Value > ReconnectGap);
                lastConnected = connected;
                lastUpdate = now;
                if (!connected) {
                    coolerOffSince = null;
                    return false;
                }
                if (boundary) {
                    atReconnect = last;
                    reconnectAt = now;
                    coolerOffSince = null;
                }
                if (!canSetTemperature) {
                    last = null;
                } else if (coolerOn && !double.IsNaN(setpoint)) {
                    last = setpoint;
                    coolerOffSince = null;
                } else if (!coolerOn) {
                    coolerOffSince ??= now;
                    if (now - coolerOffSince.Value > CoolerOffAfter) {
                        last = null;
                    }
                }
                return boundary;
            }
        }

        /// <summary>The setpoint held before the connection that started most recently, as long as that start was recent.</summary>
        public double? BeforeReconnect(DateTime now) {
            lock (sync) {
                if (reconnectAt.HasValue && now - reconnectAt.Value <= ReconnectValidFor) {
                    return atReconnect;
                }
                return last;
            }
        }
    }

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
