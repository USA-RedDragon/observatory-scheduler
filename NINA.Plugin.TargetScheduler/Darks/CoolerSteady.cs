using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Darks {

    public enum CoolerSteadyState {
        Waiting = 0,
        Within = 1,
        Over = 2
    }

    public class CoolerSteadyVerdict {
        public CoolerSteadyState State { get; set; }
        public double? Power { get; set; }
        public DateTime? Since { get; set; }
        public string Reason { get; set; }

        public override string ToString() {
            return $"{State}: {Reason}";
        }
    }

    /// <summary>
    /// Judges the cooler power at a setpoint only at steady state. The sensor must stay within
    /// <see cref="BandC"/> of the setpoint for <see cref="StableFor"/>, and the steady power is the median
    /// of the last two minutes. Power at or below the limit passes. Power above it fails only once it has
    /// stopped falling (the last minute's median is at most <see cref="SettledDropPoints"/> below the minute
    /// before) or the sensor has held the band for <see cref="MaxWait"/>; a cooler that is still coming
    /// down from the pull-down peak is waited for. The run restarts on a setpoint change, a reading
    /// outside the band, or a gap in the readings.
    /// </summary>
    public class CoolerSteady {
        public const double BandC = 0.3;
        public const double SettledDropPoints = 2;
        public static readonly TimeSpan StableFor = TimeSpan.FromMinutes(3);
        public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan PowerWindow = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly List<(DateTime At, double Power)> run = new List<(DateTime, double)>();
        private double? setpoint;
        private DateTime? inSince;
        private DateTime? last;

        /// <summary>The setpoint the current run is at, or null.</summary>
        public double? Setpoint {
            get {
                lock (sync) {
                    return setpoint;
                }
            }
        }

        public DateTime? InSince {
            get {
                lock (sync) {
                    return inSince;
                }
            }
        }

        public void Observe(DateTime at, bool connected, double sensor, double cameraSetpoint, double power) {
            lock (sync) {
                if (last.HasValue && at < last.Value) {
                    return;
                }
                bool gap = last.HasValue && at - last.Value > MaxGap;
                last = at;
                if (!connected || double.IsNaN(sensor) || double.IsNaN(cameraSetpoint)) {
                    Reset(null);
                    return;
                }
                if (gap || !setpoint.HasValue || !DarkPlanner.SameSetpoint(setpoint.Value, cameraSetpoint)) {
                    Reset(cameraSetpoint);
                }
                if (Math.Abs(sensor - cameraSetpoint) > BandC + 1e-9) {
                    run.Clear();
                    inSince = null;
                    return;
                }
                inSince ??= at;
                if (!double.IsNaN(power)) {
                    run.Add((at, power));
                    run.RemoveAll(r => r.At < at - MaxWait);
                }
            }
        }

        private void Reset(double? to) {
            setpoint = to;
            inSince = null;
            run.Clear();
        }

        public CoolerSteadyVerdict Verdict(DateTime now, double setTemp, double limit) {
            lock (sync) {
                if (!setpoint.HasValue || !DarkPlanner.SameSetpoint(setpoint.Value, setTemp)) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Waiting, Reason = string.Format(CultureInfo.InvariantCulture, "the setpoint is not at {0} °C yet", setTemp) };
                }
                if (!inSince.HasValue) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Waiting, Reason = string.Format(CultureInfo.InvariantCulture, "the sensor is not within {0} °C of {1} °C yet", BandC, setTemp) };
                }
                TimeSpan held = now - inSince.Value;
                if (held < StableFor) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Waiting, Since = inSince, Reason = string.Format(CultureInfo.InvariantCulture, "the sensor has been within {0} °C of {1} °C for {2:0} s of the {3:0} s needed", BandC, setTemp, held.TotalSeconds, StableFor.TotalSeconds) };
                }
                double? steady = Median(now - PowerWindow, now);
                if (!steady.HasValue) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Within, Since = inSince, Reason = string.Format(CultureInfo.InvariantCulture, "the sensor held {0} °C within {1} °C for {2:0} s; the camera reports no cooler power", setTemp, BandC, held.TotalSeconds) };
                }
                if (steady.Value <= limit) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Within, Power = steady, Since = inSince, Reason = string.Format(CultureInfo.InvariantCulture, "the sensor held {0} °C within {1} °C for {2:0} s at a steady {3:0}% cooler power, at most {4:0}%", setTemp, BandC, held.TotalSeconds, steady.Value, limit) };
                }
                double? recent = Median(now - TimeSpan.FromMinutes(1), now);
                double? before = Median(now - PowerWindow, now - TimeSpan.FromMinutes(1));
                bool falling = recent.HasValue && before.HasValue && before.Value - recent.Value > SettledDropPoints;
                if (falling && held < MaxWait) {
                    return new CoolerSteadyVerdict { State = CoolerSteadyState.Waiting, Power = steady, Since = inSince, Reason = string.Format(CultureInfo.InvariantCulture, "the cooler power is still falling at {0} °C ({1:0}% then {2:0}% over the last two minutes)", setTemp, before.Value, recent.Value) };
                }
                return new CoolerSteadyVerdict { State = CoolerSteadyState.Over, Power = steady, Since = inSince, Reason = string.Format(CultureInfo.InvariantCulture, "the sensor held {0} °C within {1} °C for {2:0} s at a steady {3:0}% cooler power, above {4:0}%", setTemp, BandC, held.TotalSeconds, steady.Value, limit) };
            }
        }

        private double? Median(DateTime from, DateTime to) {
            List<double> p = run.Where(r => r.At > from && r.At <= to).Select(r => r.Power).ToList();
            return p.Count == 0 ? (double?)null : CoolerController.Median(p);
        }
    }
}
