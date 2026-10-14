using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// One look at the camera for <see cref="SensorGate"/>. PendingSetpoint is the setpoint a
    /// background ramp is still moving to; while it runs, the camera's own setpoint is only a step.
    /// ConnectedAt is when the camera last connected: a reconnect stops the cooler, and the first
    /// readings after it can still show the old temperature while the sensor warms.
    /// </summary>
    public readonly struct SensorReading {

        public SensorReading(bool connected, bool canSetTemperature, bool coolerOn, double temperature, double setpoint, double? pendingSetpoint, DateTime? connectedAt = null) {
            Connected = connected;
            CanSetTemperature = canSetTemperature;
            CoolerOn = coolerOn;
            Temperature = temperature;
            Setpoint = setpoint;
            PendingSetpoint = pendingSetpoint;
            ConnectedAt = connectedAt;
        }

        public DateTime? ConnectedAt { get; }

        public bool Connected { get; }
        public bool CanSetTemperature { get; }
        public bool CoolerOn { get; }
        public double Temperature { get; }
        public double Setpoint { get; }
        public double? PendingSetpoint { get; }

        /// <summary>
        /// The setpoint a light must be taken at, or null when the camera does not regulate its
        /// temperature (not connected, no cooler, or the cooler off with no ramp running).
        /// </summary>
        public double? Reference {
            get {
                if (PendingSetpoint.HasValue) {
                    return PendingSetpoint;
                }
                if (Connected && CanSetTemperature && CoolerOn && !double.IsNaN(Setpoint)) {
                    return Setpoint;
                }
                return null;
            }
        }
    }

    public enum SensorGateOutcome {
        /// <summary>The sensor was at the setpoint at the first look.</summary>
        Ready,

        /// <summary>The sensor was off the setpoint and then held at it for the hold time.</summary>
        Settled,

        /// <summary>The sensor did not hold at the setpoint before the timeout.</summary>
        TimedOut,
    }

    /// <summary>
    /// Keeps a light from starting while the sensor is more than <see cref="ToleranceC"/> off the
    /// setpoint. A sensor that is at the setpoint at the first look passes at once, unless the camera
    /// connected less than the hold time ago. Otherwise the sensor must be within the tolerance for
    /// the hold time without a break, so the undershoot of a cooler that is catching up, or a reading
    /// taken before a reconnected sensor warmed, does not let a light through.
    /// </summary>
    public sealed class SensorGate {
        public const double ToleranceC = 0.5;
        public static readonly TimeSpan DefaultHold = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan DefaultPoll = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);

        private readonly Func<SensorReading> read;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Func<DateTime> now;
        private readonly TimeSpan hold;
        private readonly TimeSpan poll;
        private readonly TimeSpan timeout;

        public SensorGate(Func<SensorReading> read, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTime> now, TimeSpan hold, TimeSpan poll, TimeSpan timeout) {
            this.read = read;
            this.delay = delay;
            this.now = now;
            this.hold = hold;
            this.poll = poll;
            this.timeout = timeout;
        }

        public static bool AtSetpoint(SensorReading r) {
            if (!(r.Reference is double setpoint)) {
                return true;
            }
            return !double.IsNaN(r.Temperature) && Math.Abs(r.Temperature - setpoint) <= ToleranceC;
        }

        public static string Describe(SensorReading r) {
            return r.Reference is double setpoint
                ? $"sensor {Num(r.Temperature)} °C, setpoint {Num(setpoint)} °C, {Num(Math.Abs(r.Temperature - setpoint))} °C off, the limit is {Num(ToleranceC)} °C"
                : "the camera does not regulate its temperature";
        }

        public async Task<SensorGateOutcome> WaitAsync(Action<string> log, CancellationToken token) {
            SensorReading r = read();
            if (AtSetpoint(r) && !(r.Reference.HasValue && r.ConnectedAt is DateTime connected && now() - connected < hold)) {
                return SensorGateOutcome.Ready;
            }

            DateTime start = now();
            DateTime? inSince = null;
            log?.Invoke(AtSetpoint(r)
                ? $"waiting {hold.TotalSeconds:0} s for the sensor to hold the setpoint after the camera connected at {r.ConnectedAt:HH:mm:ss}: {Describe(r)}"
                : $"waiting for the sensor to reach the setpoint before the light: {Describe(r)}");
            while (true) {
                token.ThrowIfCancellationRequested();
                DateTime t = now();
                if (AtSetpoint(r)) {
                    if (!inSince.HasValue || (r.ConnectedAt is DateTime c && c > inSince.Value)) {
                        inSince = t;
                    }
                    if (t - inSince.Value >= hold) {
                        log?.Invoke($"the sensor held the setpoint for {hold.TotalSeconds:0} s after {(t - start).TotalSeconds:0} s: {Describe(r)}");
                        return SensorGateOutcome.Settled;
                    }
                } else {
                    inSince = null;
                }
                if (t - start >= timeout) {
                    log?.Invoke($"the sensor did not hold the setpoint within {timeout.TotalMinutes:0} min: {Describe(r)}");
                    return SensorGateOutcome.TimedOut;
                }
                await delay(poll, token);
                r = read();
            }
        }

        private static string Num(double v) {
            return double.IsNaN(v) ? "unknown" : v.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
