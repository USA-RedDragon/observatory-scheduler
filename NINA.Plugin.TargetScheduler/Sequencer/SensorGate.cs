using System;
using System.Globalization;
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
        /// <summary>The sensor had already held the setpoint for the hold time at the first look.</summary>
        Ready,

        /// <summary>The gate waited, and the sensor then held the setpoint for the hold time.</summary>
        Settled,

        /// <summary>The sensor did not hold at the setpoint before the timeout.</summary>
        TimedOut,
    }

    /// <summary>
    /// Tracks since when the sensor has been strictly within <see cref="SensorGate.ToleranceC"/> of
    /// the final setpoint without a break. The hold restarts on the first reading, a gap in the
    /// readings longer than the allowed gap, a camera connect, the start or end of a ramp, the cooler
    /// starting to regulate, a setpoint change, and a reading outside the band. It does not run while
    /// a ramp runs.
    /// </summary>
    public sealed class SensorHold {
        public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly TimeSpan maxGap;
        private DateTime? last;
        private double? lastReference;
        private bool lastPending;
        private DateTime? lastConnectedAt;
        private DateTime? inSince;
        private DateTime? resetAt;
        private string resetWhy;

        public SensorHold(TimeSpan maxGap) {
            this.maxGap = maxGap;
        }

        /// <summary>When the current unbroken run within the band started, or null.</summary>
        public DateTime? InSince {
            get {
                lock (sync) {
                    return inSince;
                }
            }
        }

        /// <summary>When and why the hold last restarted.</summary>
        public (DateTime? At, string Why) LastReset {
            get {
                lock (sync) {
                    return (resetAt, resetWhy);
                }
            }
        }

        public static bool InBand(SensorReading r) {
            return r.Reference is double setpoint && !double.IsNaN(r.Temperature) && Math.Abs(r.Temperature - setpoint) < SensorGate.ToleranceC;
        }

        public void Observe(DateTime at, SensorReading r) {
            lock (sync) {
                if (last.HasValue && at < last.Value) {
                    return;
                }
                double? reference = r.Reference;
                bool pending = r.PendingSetpoint.HasValue;
                string why = Change(at, r, reference, pending);
                if (why != null) {
                    inSince = null;
                    resetAt = at;
                    resetWhy = why;
                }

                if (pending || !InBand(r)) {
                    if (inSince.HasValue) {
                        resetAt = at;
                        resetWhy = !reference.HasValue
                            ? "the camera stopped regulating"
                            : $"the sensor left the band at {SensorGate.Num(r.Temperature)} °C, {SensorGate.Num(Math.Abs(r.Temperature - reference.Value))} °C off {SensorGate.Num(reference.Value)} °C";
                    }
                    inSince = null;
                } else if (!inSince.HasValue) {
                    inSince = at;
                }

                last = at;
                lastReference = reference;
                lastPending = pending;
                lastConnectedAt = r.ConnectedAt;
            }
        }

        private string Change(DateTime at, SensorReading r, double? reference, bool pending) {
            if (!last.HasValue) {
                return "the first reading";
            }
            if (at - last.Value > maxGap) {
                return $"no reading for {(at - last.Value).TotalSeconds:0} s";
            }
            if (r.ConnectedAt.HasValue && r.ConnectedAt != lastConnectedAt) {
                return $"the camera connected at {SensorGate.Clock(r.ConnectedAt.Value)}";
            }
            if (pending && !lastPending) {
                return $"a ramp to {SensorGate.Num(r.PendingSetpoint.Value)} °C started";
            }
            if (!pending && lastPending) {
                return $"the ramp to {SensorGate.Num(lastReference ?? double.NaN)} °C ended";
            }
            if (!reference.HasValue) {
                return null;
            }
            if (!lastReference.HasValue) {
                return "the cooler started regulating";
            }
            if (Math.Abs(reference.Value - lastReference.Value) >= 0.05) {
                return $"the setpoint changed from {SensorGate.Num(lastReference.Value)} °C to {SensorGate.Num(reference.Value)} °C";
            }
            return null;
        }

        public bool Held(DateTime now, TimeSpan hold) {
            lock (sync) {
                return inSince.HasValue && now - inSince.Value >= hold;
            }
        }
    }

    /// <summary>
    /// Keeps a light from starting until the sensor has been strictly within
    /// <see cref="ToleranceC"/> of the final setpoint for the hold time without a break, counted
    /// from the last setpoint change, ramp end or camera connect (see <see cref="SensorHold"/>).
    /// A camera that does not regulate its temperature passes at once.
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
        private readonly SensorHold tracker;

        /// <param name="tracker">The hold shared with the camera updates; a gate without one keeps its own.</param>
        public SensorGate(Func<SensorReading> read, Func<TimeSpan, CancellationToken, Task> delay, Func<DateTime> now, TimeSpan hold, TimeSpan poll, TimeSpan timeout, SensorHold tracker = null) {
            this.read = read;
            this.delay = delay;
            this.now = now;
            this.hold = hold;
            this.poll = poll;
            this.timeout = timeout;
            this.tracker = tracker ?? new SensorHold(SensorHold.DefaultMaxGap);
        }

        /// <summary>True when the camera does not regulate, or the sensor is strictly within the tolerance.</summary>
        public static bool AtSetpoint(SensorReading r) {
            return !r.Reference.HasValue || SensorHold.InBand(r);
        }

        public static string Describe(SensorReading r) {
            return r.Reference is double setpoint
                ? $"sensor {Num(r.Temperature)} °C, setpoint {Num(setpoint)} °C, {Num(Math.Abs(r.Temperature - setpoint))} °C off, the limit is under {Num(ToleranceC)} °C"
                : "the camera does not regulate its temperature";
        }

        /// <summary>How long the sensor has held the setpoint, for a light that passes at once.</summary>
        public string Status(SensorReading r) {
            DateTime? since = tracker.InSince;
            return r.Reference.HasValue && since.HasValue
                ? $"held since {Clock(since.Value)} ({(now() - since.Value).TotalSeconds:0} s): {Describe(r)}"
                : Describe(r);
        }

        public async Task<SensorGateOutcome> WaitAsync(Action<string> log, CancellationToken token) {
            SensorReading r = read();
            DateTime t = now();
            tracker.Observe(t, r);
            if (!r.Reference.HasValue || tracker.Held(t, hold)) {
                return SensorGateOutcome.Ready;
            }

            DateTime start = t;
            log?.Invoke($"waiting from {Clock(start)} for the sensor to hold within {Num(ToleranceC)} °C of {Num(r.Reference.Value)} °C for {hold.TotalSeconds:0} s, {Why(start)}: {Describe(r)}");
            while (true) {
                token.ThrowIfCancellationRequested();
                if (t - start >= timeout) {
                    log?.Invoke($"the sensor did not hold the setpoint within {timeout.TotalMinutes:0} min, giving up at {Clock(t)}: {Describe(r)}");
                    return SensorGateOutcome.TimedOut;
                }
                await delay(poll, token);
                r = read();
                t = now();
                tracker.Observe(t, r);
                if (!r.Reference.HasValue) {
                    log?.Invoke($"released at {Clock(t)} after {(t - start).TotalSeconds:0} s: {Describe(r)}");
                    return SensorGateOutcome.Settled;
                }
                if (tracker.Held(t, hold)) {
                    DateTime since = tracker.InSince.Value;
                    log?.Invoke($"released at {Clock(t)} after {(t - start).TotalSeconds:0} s: the sensor held within {Num(ToleranceC)} °C of {Num(r.Reference.Value)} °C since {Clock(since)} ({(t - since).TotalSeconds:0} s): {Describe(r)}");
                    return SensorGateOutcome.Settled;
                }
            }
        }

        private string Why(DateTime t) {
            DateTime? since = tracker.InSince;
            var (at, why) = tracker.LastReset;
            string reset = at.HasValue ? $"the hold restarted at {Clock(at.Value)} on {why}" : "no reading yet";
            return since.HasValue ? $"{reset}, in the band since {Clock(since.Value)} ({(t - since.Value).TotalSeconds:0} s)" : reset;
        }

        /// <summary>A time for the log: UTC times are shown in local time, so the clock can run on UTC across a DST change.</summary>
        internal static string Clock(DateTime d) {
            return (d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        internal static string Num(double v) {
            return double.IsNaN(v) ? "unknown" : v.ToString("0.0#", CultureInfo.InvariantCulture);
        }
    }
}
