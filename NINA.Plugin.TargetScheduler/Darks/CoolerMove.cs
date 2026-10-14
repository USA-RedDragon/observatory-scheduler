using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class CoolerReading {
        public DateTime At { get; set; }
        public bool Connected { get; set; }
        public double Sensor { get; set; } = double.NaN;
        public double Setpoint { get; set; } = double.NaN;
        public double Power { get; set; } = double.NaN;
    }

    public interface ICoolerDevice {

        CoolerReading Read();

        /// <summary>Ramps the setpoint at 1 °C per minute. A cancel stops the wait, not the ramp.</summary>
        Task<bool> Ramp(double target, CancellationToken token);

        /// <summary>The next camera reading, or the current one when none arrives in time.</summary>
        Task<CoolerReading> Next(CancellationToken token);
    }

    public class CoolerMoveResult {
        public bool Reached { get; set; }
        public string Reason { get; set; }
        public TimeSpan Ramp { get; set; }
        public TimeSpan Took { get; set; }
        public double? Sensor { get; set; }
    }

    /// <summary>Moves the setpoint by the 1 °C per minute ramp and holds imaging until the sensor is within 0.5 °C.</summary>
    public static class CoolerMove {
        public static readonly TimeSpan SettleLimit = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(3);
        public const double ProgressC = 0.2;

        public static async Task<CoolerMoveResult> To(ICoolerDevice device, double target, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            CoolerReading start = device.Read();
            if (start == null || !start.Connected) {
                return new CoolerMoveResult { Reason = DarkPlanner.CameraDisconnected };
            }
            if (double.IsNaN(start.Sensor)) {
                return new CoolerMoveResult { Reason = DarkPlanner.SensorUnknown };
            }

            TimeSpan ramp = DarkPlanner.Ramp(start.Sensor, target);
            bool ok = await device.Ramp(target, token);
            if (!ok) {
                CoolerReading r = device.Read();
                return new CoolerMoveResult { Ramp = ramp, Took = (r?.At ?? start.At) - start.At, Sensor = r?.Sensor, Reason = string.Format(CultureInfo.InvariantCulture, "NINA could not ramp the setpoint to {0} °C", target) };
            }

            CoolerMoveResult held = await Settle(device, target, start.At + ramp + SettleLimit, token);
            held.Ramp = ramp;
            held.Took = (device.Read()?.At ?? start.At) - start.At;
            return held;
        }

        /// <summary>Waits until the sensor is within 0.5 °C of the target, failing on a stall or at the deadline.</summary>
        public static async Task<CoolerMoveResult> Settle(ICoolerDevice device, double target, DateTime deadline, CancellationToken token) {
            CoolerReading now = device.Read() ?? new CoolerReading { At = DateTime.UtcNow, Connected = false };
            double best = Math.Abs(now.Sensor - target);
            DateTime lastProgress = now.At;
            while (!(now.Connected && !double.IsNaN(now.Sensor) && Math.Abs(now.Sensor - target) <= CoolerController.ReadyToleranceC)) {
                if (!now.Connected) {
                    return new CoolerMoveResult { Reason = DarkPlanner.CameraDisconnected };
                }
                if (now.At >= deadline) {
                    return new CoolerMoveResult { Sensor = now.Sensor, Reason = string.Format(CultureInfo.InvariantCulture,
                        "the sensor was still {0:0.0} °C from {1} °C at {2:HH:mm:ss}, at {3:0}% cooler power", Math.Abs(now.Sensor - target), target, deadline.ToLocalTime(), now.Power) };
                }
                if (now.At - lastProgress > StallLimit) {
                    return new CoolerMoveResult { Sensor = now.Sensor, Reason = string.Format(CultureInfo.InvariantCulture,
                        "the sensor stayed {0:0.0} °C from {1} °C for {2:0} min at {3:0}% cooler power", Math.Abs(now.Sensor - target), target, StallLimit.TotalMinutes, now.Power) };
                }
                now = await device.Next(token) ?? new CoolerReading { At = now.At, Connected = false };
                double gap = Math.Abs(now.Sensor - target);
                if (!double.IsNaN(gap) && gap <= best - ProgressC) {
                    best = gap;
                    lastProgress = now.At;
                }
            }

            return new CoolerMoveResult { Reached = true, Sensor = now.Sensor };
        }
    }
}
