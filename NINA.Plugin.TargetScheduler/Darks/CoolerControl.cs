using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Darks {

    public class CoolerSettingsData {
        public bool Enabled { get; set; } = true;
        public double TargetPower { get; set; } = 80;
        public double LowPower { get; set; } = 70;
        public double HighPower { get; set; } = 88;
        public double MaxPredictedPower { get; set; } = 85;

        public string Problem() {
            if (!(LowPower > 0 && LowPower < TargetPower)) {
                return string.Format(CultureInfo.InvariantCulture, "the colder threshold {0:0}% must be above 0% and below the target {1:0}%", LowPower, TargetPower);
            }
            if (!(TargetPower < MaxPredictedPower && MaxPredictedPower <= HighPower)) {
                return string.Format(CultureInfo.InvariantCulture, "the predicted-power limit {0:0}% must be above the target {1:0}% and at most the warmer threshold {2:0}%", MaxPredictedPower, TargetPower, HighPower);
            }
            if (!(HighPower < 100)) {
                return string.Format(CultureInfo.InvariantCulture, "the warmer threshold {0:0}% must be below 100%", HighPower);
            }
            return null;
        }

        public CoolerSettingsData Copy() {
            return (CoolerSettingsData)MemberwiseClone();
        }
    }

    public class CoolerSample {
        public DateTime At { get; set; }
        public double Power { get; set; }
        public double Setpoint { get; set; }
        public double Sensor { get; set; }
        public double? Ambient { get; set; }
    }

    /// <summary>One camera reading with the state that decides whether its cooler power counts.</summary>
    public class CoolerObservation {
        public DateTime At { get; set; }
        public bool Connected { get; set; }
        public bool CoolerOn { get; set; } = true;
        public double Power { get; set; } = double.NaN;
        public double Sensor { get; set; } = double.NaN;
        public double Setpoint { get; set; } = double.NaN;
        public double? Ambient { get; set; }
        public string Paused { get; set; }
    }

    /// <summary>Cooler power as a line in the drop from ambient to the setpoint.</summary>
    public class CoolerFit {
        public const double PriorIntercept = -37.4;
        public const double PriorSlope = 3.77;
        public const double MinSlope = 2;
        public const double MaxSlope = 6;
        public const int MinSamples = 10;
        public const int MinSamplesForSlope = 30;
        public const double MinSpreadForSlopeC = 2;

        public const string PriorBasis = "ZWO ASI2600MM Pro lights on 17 nights from 2026-09-17: power = -37.4 + 3.77 x (ambient - setpoint), R² 0.98, residual SD 1.2 points";

        public double Intercept { get; set; }
        public double Slope { get; set; }
        public int Samples { get; set; }
        public string Basis { get; set; }

        public double Power(double ambient, double setpoint) {
            return Intercept + Slope * (ambient - setpoint);
        }

        public double DropFor(double power) {
            return (power - Intercept) / Slope;
        }

        public static CoolerFit Prior() {
            return new CoolerFit { Intercept = PriorIntercept, Slope = PriorSlope, Samples = 0, Basis = "the measured fit (" + PriorBasis + ")" };
        }

        /// <summary>
        /// Refits from tonight's samples. With a wide enough spread in the drop, both terms are fitted, and the slope
        /// is kept only inside 2 to 6 points per °C; otherwise only the offset moves and the measured slope stays.
        /// </summary>
        public static CoolerFit Refit(IEnumerable<CoolerSample> samples) {
            List<CoolerSample> s = (samples ?? Enumerable.Empty<CoolerSample>())
                .Where(x => x != null && x.Ambient.HasValue && !double.IsNaN(x.Power) && !double.IsNaN(x.Setpoint))
                .ToList();
            if (s.Count < MinSamples) {
                CoolerFit prior = Prior();
                if (s.Count > 0) {
                    prior.Basis += string.Format(CultureInfo.InvariantCulture, "; only {0} of the {1} samples needed for a refit tonight", s.Count, MinSamples);
                }
                return prior;
            }

            double[] x = s.Select(v => v.Ambient.Value - v.Setpoint).ToArray();
            double[] y = s.Select(v => v.Power).ToArray();
            double mx = x.Average();
            double my = y.Average();
            double spread = x.Max() - x.Min();
            if (s.Count >= MinSamplesForSlope && spread >= MinSpreadForSlopeC) {
                double sxx = x.Sum(v => (v - mx) * (v - mx));
                double sxy = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
                double slope = sxx > 0 ? sxy / sxx : double.NaN;
                if (!double.IsNaN(slope) && slope >= MinSlope && slope <= MaxSlope) {
                    return new CoolerFit {
                        Intercept = my - slope * mx,
                        Slope = slope,
                        Samples = s.Count,
                        Basis = string.Format(CultureInfo.InvariantCulture, "refit from {0} samples tonight over drops of {1:0.0} to {2:0.0} °C", s.Count, x.Min(), x.Max())
                    };
                }
            }

            return new CoolerFit {
                Intercept = my - PriorSlope * mx,
                Slope = PriorSlope,
                Samples = s.Count,
                Basis = string.Format(CultureInfo.InvariantCulture, "offset refit from {0} samples tonight with the measured slope {1} points per °C", s.Count, PriorSlope)
            };
        }

        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "power = {0:0.0} + {1:0.00} x (ambient - setpoint), {2}", Intercept, Slope, Basis);
        }
    }

    public enum CoolerAction {
        None = 0,
        Start = 1,
        Warmer = 2,
        Colder = 3,
        Hold = 4
    }

    public class CoolerDecision {
        public CoolerAction Action { get; set; }
        public double? From { get; set; }
        public double? Target { get; set; }
        public double? Power { get; set; }
        public double? Ambient { get; set; }
        public double? Predicted { get; set; }
        public CoolerFit Fit { get; set; }
        public string Reason { get; set; }

        public bool Moves => (Action == CoolerAction.Start || Action == CoolerAction.Warmer || Action == CoolerAction.Colder)
            && Target.HasValue && !(From.HasValue && DarkPlanner.SameSetpoint(From.Value, Target.Value));

        public string Name => Action switch {
            CoolerAction.Start => "start",
            CoolerAction.Warmer => "warmer",
            CoolerAction.Colder => "colder",
            CoolerAction.Hold => "hold",
            _ => "none"
        };

        public override string ToString() {
            string power = Power.HasValue ? string.Format(CultureInfo.InvariantCulture, "power {0:0.0}%", Power.Value) : "power not measured yet";
            string ambient = Ambient.HasValue ? string.Format(CultureInfo.InvariantCulture, "ambient {0:0.0} °C", Ambient.Value) : "ambient unknown";
            string predicted = Predicted.HasValue ? string.Format(CultureInfo.InvariantCulture, ", predicted {0:0.0}%", Predicted.Value) : "";
            string move = Target.HasValue
                ? (Moves ? string.Format(CultureInfo.InvariantCulture, "{0} -> {1} °C", From.HasValue ? From.Value.ToString("0.#", CultureInfo.InvariantCulture) : "?", Target.Value) : string.Format(CultureInfo.InvariantCulture, "{0} °C", Target.Value))
                : "";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} ({2}, {3}{4}): {5}", Name, move, power, ambient, predicted, Reason);
        }
    }

    public static class CoolerGrid {
        public const double StepC = 2;

        public static double Snap(double setpoint) {
            return Math.Floor(setpoint / StepC + 0.5) * StepC;
        }

        public static bool OnGrid(double setpoint) {
            return Math.Abs(setpoint - Snap(setpoint)) < 0.05;
        }

        public static double BelowAmbient(double setpoint, double ambient) {
            while (setpoint > ambient + 0.05) {
                setpoint -= StepC;
            }
            return setpoint;
        }
    }

    /// <summary>
    /// Keeps the camera cooler near a target power: picks a starting setpoint on the 2 °C grid from the fit, steps
    /// 2 °C warmer when the 5-minute median power is above the high threshold, and 2 °C colder when it has stayed
    /// below the low threshold for 15 minutes and the refit predicts the colder setpoint stays within the limit.
    /// It only decides; the caller moves the setpoint between exposures and reports back with <see cref="Moved"/>.
    /// </summary>
    public class CoolerController {
        public const double ReadyToleranceC = 0.5;

        /// <summary>The power counted for a reading more than 0.5 °C warmer than the setpoint: the cooler cannot hold it.</summary>
        public const double SaturatedPower = 100;

        public static readonly TimeSpan HighWindow = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan LowWindow = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan StepSpacing = TimeSpan.FromMinutes(20);
        public static readonly TimeSpan AmbientWindow = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan SampleSpacing = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan FitSpacing = TimeSpan.FromMinutes(1);

        private readonly List<CoolerSample> run = new List<CoolerSample>();
        private readonly List<CoolerSample> night = new List<CoolerSample>();
        private readonly List<(DateTime At, double Ambient)> ambients = new List<(DateTime, double)>();
        private DateTime? runStart;
        private DateTime? lastValid;

        public string Night { get; private set; }
        public double? Setpoint { get; private set; }
        public DateTime? LastStepAt { get; private set; }
        public DateTime? StartedAt { get; private set; }
        public bool Verified { get; private set; }
        public string ColderBlocked { get; private set; }
        public string Paused { get; private set; }

        public IReadOnlyList<CoolerSample> Run => run;
        public IReadOnlyList<CoolerSample> NightSamples => night;

        public void Observe(CoolerObservation o) {
            if (o == null) {
                return;
            }
            if (o.Ambient.HasValue && !double.IsNaN(o.Ambient.Value)) {
                ambients.Add((o.At, o.Ambient.Value));
                ambients.RemoveAll(a => a.At < o.At - AmbientWindow);
            }

            Paused = o.Paused;
            if (o.Paused != null || !o.Connected || !o.CoolerOn || double.IsNaN(o.Power) || double.IsNaN(o.Sensor) || double.IsNaN(o.Setpoint)
                || !Setpoint.HasValue || !DarkPlanner.SameSetpoint(o.Setpoint, Setpoint.Value)
                || o.Sensor < o.Setpoint - ReadyToleranceC) {
                Break();
                return;
            }
            double power = o.Sensor > o.Setpoint + ReadyToleranceC ? Math.Max(o.Power, SaturatedPower) : o.Power;

            if (lastValid.HasValue && o.At - lastValid.Value > MaxGap) {
                Break();
            }
            lastValid = o.At;
            runStart ??= o.At;
            if (o.At - runStart.Value < Settle) {
                return;
            }

            var sample = new CoolerSample { At = o.At, Power = power, Setpoint = o.Setpoint, Sensor = o.Sensor, Ambient = AmbientAt(o.At) };
            if (run.Count == 0 || o.At - run[run.Count - 1].At >= SampleSpacing) {
                run.Add(sample);
                run.RemoveAll(s => s.At < o.At - LowWindow - HighWindow);
            }
            if (sample.Ambient.HasValue && power == o.Power && (night.Count == 0 || o.At - night[night.Count - 1].At >= FitSpacing)) {
                night.Add(sample);
            }
        }

        private void Break() {
            run.Clear();
            runStart = null;
            lastValid = null;
        }

        private double? AmbientAt(DateTime at) {
            List<double> v = ambients.Where(a => a.At <= at && a.At >= at - AmbientWindow).Select(a => a.Ambient).ToList();
            return v.Count == 0 ? null : Median(v);
        }

        public double? Ambient(DateTime now) {
            return AmbientAt(now);
        }

        public DateTime? SettledSince => runStart.HasValue ? runStart.Value + Settle : (DateTime?)null;

        public double? MedianPower(DateTime end, TimeSpan window) {
            if (!SettledSince.HasValue || SettledSince.Value > end - window) {
                return null;
            }
            List<double> p = run.Where(s => s.At > end - window && s.At <= end).Select(s => s.Power).ToList();
            return p.Count == 0 ? null : Median(p);
        }

        /// <summary>The highest 5-minute median over the last 15 minutes, or null without 15 settled minutes.</summary>
        public double? LowWindowPeak(DateTime now) {
            if (!SettledSince.HasValue || SettledSince.Value > now - LowWindow) {
                return null;
            }
            double? peak = null;
            foreach (CoolerSample s in run.Where(s => s.At >= now - LowWindow + HighWindow && s.At <= now)) {
                double? m = MedianPower(s.At, HighWindow);
                if (m.HasValue && (!peak.HasValue || m.Value > peak.Value)) {
                    peak = m;
                }
            }
            return peak;
        }

        public CoolerFit Fit() {
            return CoolerFit.Refit(night);
        }

        public void NewNight(string nightOf) {
            if (Night == nightOf) {
                return;
            }
            Night = nightOf;
            Setpoint = null;
            LastStepAt = null;
            StartedAt = null;
            Verified = false;
            ColderBlocked = null;
            night.Clear();
            Break();
        }

        public static CoolerDecision StartSetpoint(double ambient, CoolerFit fit, CoolerSettingsData settings, IEnumerable<CoolerHold> holds, IEnumerable<CoolerLimit> limits, DateTime? now = null) {
            double drop = fit.DropFor(settings.TargetPower);
            double setpoint = CoolerGrid.BelowAmbient(CoolerGrid.Snap(ambient - drop), ambient);
            var why = new List<string> {
                string.Format(CultureInfo.InvariantCulture, "{0:0}% power is {1:0.0} °C below the {2:0.0} °C ambient by {3}, nearest 2 °C grid setpoint {4} °C",
                    settings.TargetPower, drop, ambient, fit, setpoint)
            };

            for (int i = 0; i < 40; i++) {
                string limit = LearnedReason(setpoint, ambient, settings, holds, limits, now);
                double predicted = fit.Power(ambient, setpoint);
                if (limit == null && predicted <= settings.MaxPredictedPower + 1e-9) {
                    break;
                }
                why.Add(limit ?? string.Format(CultureInfo.InvariantCulture, "{0} °C is predicted at {1:0.0}%, above {2:0}%", setpoint, predicted, settings.MaxPredictedPower));
                if (setpoint + CoolerGrid.StepC > ambient + 0.05) {
                    break;
                }
                setpoint += CoolerGrid.StepC;
            }

            return new CoolerDecision {
                Action = CoolerAction.Start,
                Target = setpoint,
                Ambient = ambient,
                Predicted = fit.Power(ambient, setpoint),
                Fit = fit,
                Reason = string.Join("; ", why)
            };
        }

        /// <summary>
        /// Why the learned cooler data rules out a setpoint at this ambient: a hold that needed more than the
        /// predicted-power limit at the same or a smaller drop below ambient in the last 30 days, or a recorded failure to reach it.
        /// </summary>
        public static string LearnedReason(double setpoint, double ambient, CoolerSettingsData settings, IEnumerable<CoolerHold> holds, IEnumerable<CoolerLimit> limits, DateTime? now = null) {
            double drop = ambient - setpoint;
            CoolerHold hot = (holds ?? Enumerable.Empty<CoolerHold>())
                .Where(h => h != null && h.PeakPower.HasValue && h.PeakPower.Value > settings.MaxPredictedPower && h.Ambient - h.SetTemp <= drop + 0.05
                    && (!now.HasValue || h.At >= now.Value - DarkState.LimitMaxAge))
                .OrderBy(h => h.Ambient - h.SetTemp)
                .FirstOrDefault();
            if (hot != null) {
                return string.Format(CultureInfo.InvariantCulture,
                    "the cooler needed {0:0}% to hold {1} °C at {2:0.0} °C ambient ({3:0.0} °C below it) on {4:yyyy-MM-dd}, so {5} °C ({6:0.0} °C below {7:0.0} °C) is too cold",
                    hot.PeakPower.Value, hot.SetTemp, hot.Ambient, hot.Ambient - hot.SetTemp, hot.At, setpoint, drop, ambient);
            }
            IList<CoolerLimit> list = (limits ?? Enumerable.Empty<CoolerLimit>()).ToList();
            return DarkPlanner.CapacityReason(DarkPlanner.Capacity(holds, list), setpoint, ambient)
                ?? DarkPlanner.LimitReason(list, setpoint, ambient, double.NaN, null);
        }

        /// <summary>Replaces the pause reason from the last camera reading with one read just now.</summary>
        public void SetPaused(string paused) {
            Paused = paused;
            if (paused != null) {
                Break();
            }
        }

        /// <summary>Decides at an exposure boundary.</summary>
        public CoolerDecision Decide(DateTime now, double cameraSetpoint, CoolerSettingsData settings, IEnumerable<CoolerHold> holds, IEnumerable<CoolerLimit> limits) {
            double? ambient = Ambient(now);
            CoolerFit fit = Fit();
            CoolerDecision Make(CoolerAction a, string reason, double? target = null, double? power = null, double? predicted = null) {
                return new CoolerDecision { Action = a, From = double.IsNaN(cameraSetpoint) ? (double?)null : cameraSetpoint, Target = target, Power = power, Ambient = ambient, Predicted = predicted, Fit = fit, Reason = reason };
            }

            if (Paused != null) {
                return Make(CoolerAction.Hold, $"paused: {Paused}", Setpoint);
            }
            if (!ambient.HasValue) {
                return Make(CoolerAction.Hold, "the ambient temperature is not known", Setpoint);
            }

            if (!Setpoint.HasValue || double.IsNaN(cameraSetpoint) || !DarkPlanner.SameSetpoint(cameraSetpoint, Setpoint.Value)) {
                CoolerDecision start = StartSetpoint(ambient.Value, fit, settings, holds, limits, now);
                start.From = double.IsNaN(cameraSetpoint) ? (double?)null : cameraSetpoint;
                if (Setpoint.HasValue) {
                    start.Reason = string.Format(CultureInfo.InvariantCulture, "the setpoint was changed to {0} °C outside the controller, which had {1} °C, so it is chosen again: {2}",
                        cameraSetpoint, Setpoint.Value, start.Reason);
                }
                return start;
            }

            double sp = Setpoint.Value;
            double? power5 = MedianPower(now, HighWindow);
            if (!power5.HasValue) {
                return Make(CoolerAction.Hold, string.Format(CultureInfo.InvariantCulture, "fewer than {0:0} settled minutes at {1} °C (settled since {2})",
                    HighWindow.TotalMinutes, sp, SettledSince.HasValue ? SettledSince.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "not yet"), sp, fit.Power(ambient.Value, sp));
            }

            string verified = "";
            if (!Verified) {
                Verified = true;
                verified = string.Format(CultureInfo.InvariantCulture, "first settled 5-minute power at {0} °C is {1:0.0}% against {2:0.0}% predicted; ", sp, power5.Value, fit.Power(ambient.Value, sp));
            }

            DateTime? nextStep = LastStepAt.HasValue ? LastStepAt.Value + StepSpacing : (DateTime?)null;
            bool spaced = !nextStep.HasValue || now >= nextStep.Value;
            string wait = nextStep.HasValue ? string.Format(CultureInfo.InvariantCulture, "the last step was at {0:HH:mm:ss}, and steps are at least {1:0} min apart", LastStepAt.Value.ToLocalTime(), StepSpacing.TotalMinutes) : "";

            if (power5.Value > settings.HighPower) {
                double warmer = sp + CoolerGrid.StepC;
                if (!spaced) {
                    return Make(CoolerAction.Hold, verified + string.Format(CultureInfo.InvariantCulture, "5-minute median {0:0.0}% is above {1:0}%, but {2}", power5.Value, settings.HighPower, wait), sp, power5, fit.Power(ambient.Value, sp));
                }
                if (warmer > ambient.Value + 0.05) {
                    return Make(CoolerAction.Hold, verified + string.Format(CultureInfo.InvariantCulture, "5-minute median {0:0.0}% is above {1:0}%, but {2} °C would be above the {3:0.0} °C ambient", power5.Value, settings.HighPower, warmer, ambient.Value), sp, power5);
                }
                return Make(CoolerAction.Warmer, verified + string.Format(CultureInfo.InvariantCulture, "5-minute median {0:0.0}% is above {1:0}%", power5.Value, settings.HighPower), warmer, power5, fit.Power(ambient.Value, warmer));
            }

            double? low = LowWindowPeak(now);
            if (low.HasValue && low.Value < settings.LowPower) {
                double colder = sp - CoolerGrid.StepC;
                double predicted = fit.Power(ambient.Value, colder);
                string lowText = string.Format(CultureInfo.InvariantCulture, "every 5-minute median over the last {0:0} min is below {1:0}% (highest {2:0.0}%)", LowWindow.TotalMinutes, settings.LowPower, low.Value);
                if (ColderBlocked != null) {
                    return Make(CoolerAction.Hold, verified + lowText + ", but colder steps are off tonight: " + ColderBlocked, sp, power5, predicted);
                }
                if (predicted > settings.MaxPredictedPower) {
                    return Make(CoolerAction.Hold, verified + lowText + string.Format(CultureInfo.InvariantCulture, ", but {0} °C is predicted at {1:0.0}%, above {2:0}% ({3})", colder, predicted, settings.MaxPredictedPower, fit), sp, power5, predicted);
                }
                string failed = DarkPlanner.LimitReason(limits, colder, ambient.Value, double.NaN, null);
                if (failed != null) {
                    return Make(CoolerAction.Hold, verified + lowText + ", but " + failed, sp, power5, predicted);
                }
                if (!spaced) {
                    return Make(CoolerAction.Hold, verified + lowText + ", but " + wait, sp, power5, predicted);
                }
                return Make(CoolerAction.Colder, verified + lowText + string.Format(CultureInfo.InvariantCulture, "; {0} °C is predicted at {1:0.0}% ({2})", colder, predicted, fit), colder, power5, predicted);
            }

            string band = low.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "5-minute median {0:0.0}% is within {1:0}-{2:0}%", power5.Value, settings.LowPower, settings.HighPower)
                : string.Format(CultureInfo.InvariantCulture, "5-minute median {0:0.0}% is at most {1:0}%, and 15 settled minutes are needed before a colder step", power5.Value, settings.HighPower);
            return Make(CoolerAction.None, verified + band, sp, power5, fit.Power(ambient.Value, sp));
        }

        /// <summary>Records the setpoint the controller now owns after a move that reached it.</summary>
        public void Moved(CoolerDecision d, DateTime now) {
            if (!d.Target.HasValue) {
                return;
            }
            bool changed = !Setpoint.HasValue || !DarkPlanner.SameSetpoint(Setpoint.Value, d.Target.Value);
            Setpoint = d.Target;
            if (d.Action == CoolerAction.Start) {
                StartedAt = now;
                Verified = false;
            } else if (d.Action == CoolerAction.Warmer || d.Action == CoolerAction.Colder) {
                LastStepAt = now;
            }
            if (changed) {
                Break();
            }
        }

        /// <summary>Records a colder move that did not reach its target; colder steps stop for the night.</summary>
        public void ColderFailed(string reason) {
            ColderBlocked = reason;
        }

        public static double CeilGrid(double t) {
            return Math.Ceiling(t / CoolerGrid.StepC - 1e-9) * CoolerGrid.StepC;
        }

        public static double Median(IList<double> values) {
            List<double> v = values.OrderBy(x => x).ToList();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }
    }
}
