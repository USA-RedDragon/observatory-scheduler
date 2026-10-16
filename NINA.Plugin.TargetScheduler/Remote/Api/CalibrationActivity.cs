using NINA.Plugin.TargetScheduler.Darks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class CalibrationText {

        public static string Num(double v) {
            return Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Dark(double exposure, double setTemp, double gain) {
            return $"{Num(exposure)} s at {Num(setTemp)} °C, gain {Num(gain)}";
        }

        public static string Bias(double gain, double offset) {
            return $"bias at gain {Num(gain)}, offset {Num(offset)}";
        }

        public static string Flat(string filter, string target) {
            return string.IsNullOrWhiteSpace(target) ? $"{filter} flats" : $"{filter} flats for {target}";
        }

        public static string Describe(StatusCalibrationQueueItem i) {
            switch (i.Kind) {
                case CalibrationActivity.KindBias:
                    return Bias(i.Gain ?? 0, i.Offset ?? 0);

                case CalibrationActivity.KindFlats:
                    return Flat(i.Filter, i.TargetName);

                default:
                    return Dark(i.Exposure ?? 0, i.SetTemp ?? 0, i.Gain ?? 0);
            }
        }
    }

    public class CalibrationPlanContext {
        public TimeSpan Overhead { get; set; }
        public Func<double, double, TimeSpan> Move { get; set; }
        public DateTime? Limit { get; set; }
    }

    public static class CalibrationPlan {

        public static bool Open(StatusCalibrationQueueItem i) {
            return i.Status == CalibrationActivity.Taking || i.Status == CalibrationActivity.Next || i.Status == CalibrationActivity.Waiting;
        }

        /// <summary>
        /// Fills the planned start, end and the frames that fit before the limit for each open item, in queue order,
        /// starting at <paramref name="start"/> with the camera at <paramref name="setpoint"/>. The item being taken
        /// starts when its current frame ends, and that frame counts as fitting.
        /// </summary>
        public static void Fill(IList<StatusCalibrationQueueItem> items, DateTime start, double? setpoint, CalibrationPlanContext ctx, StatusCalibrationFrame current) {
            if (items == null || ctx == null) {
                return;
            }
            DateTime t = start;
            double? sp = setpoint;
            foreach (StatusCalibrationQueueItem i in items) {
                if (!Open(i)) {
                    i.PlannedStart = null;
                    i.PlannedEnd = null;
                    i.FitFrames = null;
                    continue;
                }
                bool taking = i.Status == CalibrationActivity.Taking;
                if (i.Kind == CalibrationActivity.KindDarks && i.SetTemp.HasValue) {
                    if (sp.HasValue && !(taking && current != null) && ctx.Move != null) {
                        t += ctx.Move(sp.Value, i.SetTemp.Value);
                    }
                    sp = i.SetTemp.Value;
                }
                double per = Math.Max(0, i.Exposure ?? 0) + Math.Max(0, ctx.Overhead.TotalSeconds);
                int frames = Math.Max(0, i.Remaining);
                int counted = 0;
                DateTime from = t;
                if (taking && current != null && frames > 0) {
                    from = current.StartedAt;
                    t = current.EndsAt > t ? current.EndsAt : t;
                    frames--;
                    counted = 1;
                }
                int fit = frames;
                if (ctx.Limit.HasValue && per > 0) {
                    double room = (ctx.Limit.Value - t).TotalSeconds;
                    fit = room <= 0 ? 0 : Math.Min(frames, (int)Math.Floor(room / per));
                }
                DateTime end = t + TimeSpan.FromSeconds(per * frames);
                i.PlannedStart = from;
                i.PlannedEnd = end;
                i.FitFrames = fit + counted;
                t = end;
            }
        }
    }

    public static class CoolingEstimate {
        public const double AtTargetC = 0.5;

        /// <summary>
        /// The rate the setpoint ramps at, in °C a minute: the slope of the setpoint over the samples when they span
        /// at least 30 s and moved, else the cool-down's planned rate from its start temperature and duration.
        /// </summary>
        public static double? Rate(IList<(DateTime At, double Setpoint)> samples, double? from, double target, double? durationMinutes) {
            if (samples != null && samples.Count >= 2) {
                var first = samples[0];
                var last = samples[samples.Count - 1];
                double minutes = (last.At - first.At).TotalMinutes;
                double moved = Math.Abs(last.Setpoint - first.Setpoint);
                if (minutes >= 0.5 && moved >= 0.05) {
                    return moved / minutes;
                }
            }
            if (from.HasValue && durationMinutes.HasValue && durationMinutes.Value > 0) {
                double r = Math.Abs(from.Value - target) / durationMinutes.Value;
                return r > 0 ? r : (double?)null;
            }
            return null;
        }

        public static DateTime? EndsAt(DateTime now, double? setpoint, double target, double? rate) {
            if (!setpoint.HasValue || double.IsNaN(setpoint.Value)) {
                return null;
            }
            double left = Math.Abs(setpoint.Value - target);
            if (left <= AtTargetC) {
                return now;
            }
            if (!rate.HasValue || rate.Value <= 0) {
                return null;
            }
            return now + TimeSpan.FromMinutes(left / rate.Value);
        }
    }

    /// <summary>
    /// What the darks, bias and flats instructions and the sequence's camera cool-downs are doing, kept for the status
    /// API. The instructions report each step; the snapshot fills in the plan from the current time.
    /// </summary>
    public sealed class CalibrationActivity {
        public const string KindCooling = "cooling";
        public const string KindDarks = "darks";
        public const string KindBias = "bias";
        public const string KindFlats = "flats";
        public const string KindDone = "done";

        public const string Taking = "taking";
        public const string Next = "next";
        public const string Waiting = "waiting";
        public const string Done = "done";
        public const string Skipped = "skipped";

        public const int MaxLog = 20;
        public const int MaxSamples = 120;

        public static CalibrationActivity Current { get; } = new CalibrationActivity(() => DateTime.UtcNow);

        private readonly Func<DateTime> clock;
        private readonly object sync = new object();

        private string kind;
        private string runKind;
        private string placement;
        private string purpose;
        private DateTime? since;
        private bool running;
        private StatusCalibrationCooling cooling;
        private double? coolingDuration;
        private readonly List<(DateTime At, double Setpoint)> samples = new List<(DateTime, double)>();
        private bool sequenceCooling;
        private string kindBeforeCooling;
        private DateTime? sinceBeforeCooling;
        private StatusCalibrationSet set;
        private StatusCalibrationFrame frame;
        private List<StatusCalibrationQueueItem> queue = new List<StatusCalibrationQueueItem>();
        private List<StatusCalibrationQueueItem> planned = new List<StatusCalibrationQueueItem>();
        private DateTime? readyAt;
        private string lastCheck;
        private CalibrationPlanContext plan;
        private readonly List<StatusCalibrationTaken> taken = new List<StatusCalibrationTaken>();
        private StatusCalibrationTaken takenNow;
        private StatusCalibrationStop stop;
        private readonly List<StatusCalibrationLog> log = new List<StatusCalibrationLog>();
        private string night;
        private readonly List<string> runLines = new List<string>();
        private int runFrames;
        private string lastQuietRun;
        private double? ambient;
        private DateTime? sunLimitAt;

        public event Action Changed;

        public CalibrationActivity(Func<DateTime> clock) {
            this.clock = clock;
        }

        public bool Running {
            get {
                lock (sync) {
                    return running || (sequenceCooling && purpose == "darks");
                }
            }
        }

        public bool SequenceCooling {
            get {
                lock (sync) {
                    return sequenceCooling;
                }
            }
        }

        public static string NightOf(DateTime utc) {
            return utc.ToLocalTime().AddHours(-12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private DateTime Now() {
            DateTime now = clock();
            string n = NightOf(now);
            if (n != night) {
                night = n;
                taken.Clear();
                log.Clear();
                lastCheck = null;
            }
            return now;
        }

        private void Log(DateTime now, string text) {
            if (running) {
                runLines.Add(text);
            }
            log.Add(new StatusCalibrationLog { At = now, Text = text });
            while (log.Count > MaxLog) {
                log.RemoveAt(0);
            }
        }

        private void Raise() {
            try {
                Changed?.Invoke();
            } catch (Exception) {
            }
        }

        private void SetKind(DateTime now, string k) {
            if (kind != k) {
                kind = k;
                since = now;
            }
        }

        public void DarksChecked(bool ready, string reason, IList<StatusCalibrationQueueItem> queueIfReady, double? ambientNow, DateTime? sunLimit) {
            lock (sync) {
                DateTime now = Now();
                string text;
                if (ready) {
                    int darks = queueIfReady?.Count(q => q.Kind == KindDarks) ?? 0;
                    int bias = queueIfReady?.Count(q => q.Kind == KindBias) ?? 0;
                    text = darks > 0
                        ? $"Darks check passed: {darks} dark {(darks == 1 ? "set is" : "sets are")} wanted and at least one fits before dawn."
                        : $"Darks check passed for bias frames: {bias} bias {(bias == 1 ? "set is" : "sets are")} wanted.";
                    readyAt = now;
                    planned = (queueIfReady ?? new List<StatusCalibrationQueueItem>()).Select(q => q.Copy()).ToList();
                    ambient = ambientNow;
                    sunLimitAt = sunLimit;
                } else {
                    text = $"Darks check: not now, {reason}.";
                    readyAt = null;
                    planned = new List<StatusCalibrationQueueItem>();
                }
                string key = ready ? text : "not ready";
                if (key != lastCheck) {
                    lastCheck = key;
                    Log(now, text);
                }
            }
            Raise();
        }

        public void SequenceCoolingSeen(double target, double? durationMinutes, double? sensor, double? setpoint, CalibrationPlanContext ctx) {
            bool started = false;
            lock (sync) {
                DateTime now = Now();
                if (running) {
                    return;
                }
                if (!sequenceCooling || cooling == null || Math.Abs(cooling.Target - target) >= 0.05) {
                    started = true;
                    if (!sequenceCooling) {
                        kindBeforeCooling = kind;
                        sinceBeforeCooling = since;
                    }
                    sequenceCooling = true;
                    bool forDarks = readyAt.HasValue && now - readyAt.Value < TimeSpan.FromHours(3) && planned.Count > 0;
                    purpose = forDarks ? "darks" : "imaging";
                    placement = null;
                    SetKind(now, KindCooling);
                    cooling = new StatusCalibrationCooling { Target = target, From = sensor, StartedAt = now, Source = "sequence" };
                    coolingDuration = durationMinutes;
                    samples.Clear();
                    set = null;
                    frame = null;
                    if (forDarks) {
                        queue = planned.Select(q => q.Copy()).ToList();
                        plan = ctx;
                        if (plan != null && plan.Limit == null) {
                            plan.Limit = sunLimitAt;
                        }
                        stop = null;
                    }
                    Log(now, $"Cooling the camera to {CalibrationText.Num(target)} °C{(forDarks ? " for darks" : "")}.");
                }
                if (setpoint.HasValue && !double.IsNaN(setpoint.Value)) {
                    samples.Add((now, setpoint.Value));
                    while (samples.Count > MaxSamples) {
                        samples.RemoveAt(0);
                    }
                }
                double? rate = CoolingEstimate.Rate(samples, cooling.From, target, coolingDuration);
                cooling.RateCPerMin = rate.HasValue ? Math.Round(rate.Value, 3) : null;
                cooling.EndsAt = CoolingEstimate.EndsAt(now, setpoint, target, rate);
            }
            if (started) {
                Raise();
            }
        }

        public void SequenceCoolingEnded(double? setpoint) {
            lock (sync) {
                if (!sequenceCooling) {
                    return;
                }
                DateTime now = Now();
                sequenceCooling = false;
                if (cooling != null) {
                    bool reached = setpoint.HasValue && Math.Abs(setpoint.Value - cooling.Target) <= CoolingEstimate.AtTargetC;
                    Log(now, reached ? $"The camera setpoint reached {CalibrationText.Num(cooling.Target)} °C." : $"The cool-down to {CalibrationText.Num(cooling.Target)} °C ended.");
                }
                cooling = null;
                samples.Clear();
                kind = kindBeforeCooling;
                since = kind == null ? null : sinceBeforeCooling ?? now;
                purpose = null;
            }
            Raise();
        }

        public void RunStarted(string runKindName, string place, IList<StatusCalibrationQueueItem> items, double? ambientNow, DateTime? sunLimit, CalibrationPlanContext ctx) {
            lock (sync) {
                DateTime now = Now();
                running = true;
                sequenceCooling = false;
                if (runKindName != KindFlats) {
                    readyAt = null;
                    planned = new List<StatusCalibrationQueueItem>();
                }
                runKind = runKindName;
                placement = place;
                purpose = null;
                cooling = null;
                samples.Clear();
                set = null;
                frame = null;
                stop = null;
                takenNow = null;
                queue = (items ?? new List<StatusCalibrationQueueItem>()).Select(q => q.Copy()).ToList();
                for (int i = 0; i < queue.Count; i++) {
                    queue[i].Status = i == 0 ? Next : Waiting;
                }
                plan = ctx;
                ambient = ambientNow;
                sunLimitAt = sunLimit;
                runLines.Clear();
                runFrames = 0;
                SetKind(now, queue.FirstOrDefault()?.Kind ?? runKindName);
                if (runKindName == KindFlats) {
                    Log(now, $"Flats start: {queue.Count} {(queue.Count == 1 ? "set" : "sets")}.");
                } else {
                    int darks = queue.Count(q => q.Kind == KindDarks);
                    int bias = queue.Count(q => q.Kind == KindBias);
                    var parts = new List<string>();
                    if (bias > 0) {
                        parts.Add($"{bias} bias {(bias == 1 ? "set" : "sets")}");
                    }
                    if (darks > 0) {
                        parts.Add($"{darks} dark {(darks == 1 ? "set" : "sets")}");
                    }
                    Log(now, $"Darks start: {string.Join(", then ", parts)}{(place == "while_unsafe" && darks > 1 ? ", warmest first" : "")}.");
                }
            }
            Raise();
        }

        public void MoveStarted(double target, double? from, TimeSpan? estimate, string why) {
            lock (sync) {
                DateTime now = Now();
                purpose = why;
                SetKind(now, KindCooling);
                cooling = new StatusCalibrationCooling {
                    Target = target,
                    From = from,
                    StartedAt = now,
                    EndsAt = estimate.HasValue ? now + estimate.Value : null,
                    RateCPerMin = from.HasValue && estimate.HasValue && estimate.Value.TotalMinutes > 0 ? Math.Round(Math.Abs(from.Value - target) / estimate.Value.TotalMinutes, 3) : null,
                    Source = "darks"
                };
                frame = null;
            }
            Raise();
        }

        public void MoveEnded() {
            lock (sync) {
                DateTime now = Now();
                cooling = null;
                purpose = null;
                SetKind(now, set != null ? KindOf(set) : runKind ?? KindDarks);
            }
            Raise();
        }

        private string KindOf(StatusCalibrationSet s) {
            StatusCalibrationQueueItem item = queue.FirstOrDefault(q => q.ComboKey == s.ComboKey);
            return item?.Kind ?? runKind ?? KindDarks;
        }

        public void SetStarted(string comboKey, int framesDone, int index, Action<StatusCalibrationSet> fill = null) {
            lock (sync) {
                DateTime now = Now();
                StatusCalibrationQueueItem item = queue.FirstOrDefault(q => q.ComboKey == comboKey);
                if (set == null || set.ComboKey != comboKey) {
                    set = new StatusCalibrationSet {
                        ComboKey = comboKey,
                        FramesBefore = framesDone,
                        StartedAt = now,
                        Count = queue.Count,
                    };
                    if (item != null) {
                        set.Exposure = item.Exposure;
                        set.Gain = item.Gain;
                        set.Offset = item.Offset;
                        set.Binning = item.Binning;
                        set.SetTemp = item.SetTemp;
                        set.Filter = item.Filter;
                        set.TargetName = item.TargetName;
                        set.FramesTotal = item.FramesTotal;
                    }
                    takenNow = null;
                }
                set.Index = index > 0 ? index : (item == null ? 0 : queue.IndexOf(item) + 1);
                set.FramesDone = framesDone;
                fill?.Invoke(set);
                if (item != null) {
                    item.Status = Taking;
                    item.Remaining = Math.Max(0, set.FramesTotal - framesDone);
                    item.ReasonCode = null;
                    item.Reason = null;
                }
                bool first = true;
                foreach (StatusCalibrationQueueItem q in queue) {
                    if (q == item || !CalibrationPlan.Open(q) || q.Status == Taking) {
                        continue;
                    }
                    q.Status = first ? Next : Waiting;
                    first = false;
                }
                if (cooling == null) {
                    SetKind(now, item?.Kind ?? runKind ?? KindDarks);
                }
            }
            Raise();
        }

        public void FrameStarted(int number, double seconds) {
            lock (sync) {
                DateTime now = Now();
                frame = new StatusCalibrationFrame { Number = number, Seconds = seconds, StartedAt = now, EndsAt = now + TimeSpan.FromSeconds(Math.Max(0, seconds)) };
            }
            Raise();
        }

        public void FrameTaken(int framesDone) {
            lock (sync) {
                DateTime now = Now();
                frame = null;
                if (set == null) {
                    return;
                }
                set.FramesDone = framesDone;
                StatusCalibrationQueueItem item = queue.FirstOrDefault(q => q.ComboKey == set.ComboKey);
                if (item != null) {
                    item.Remaining = Math.Max(0, set.FramesTotal - framesDone);
                }
                if (takenNow == null) {
                    takenNow = new StatusCalibrationTaken {
                        Kind = item?.Kind ?? runKind,
                        ComboKey = set.ComboKey,
                        Exposure = set.Exposure,
                        Gain = set.Gain,
                        Offset = set.Offset,
                        Binning = set.Binning,
                        SetTemp = set.SetTemp,
                        Filter = set.Filter,
                        TargetName = set.TargetName,
                        PanelBrightness = set.PanelBrightness,
                        StartedAt = set.StartedAt,
                    };
                    taken.Add(takenNow);
                }
                takenNow.Frames++;
                takenNow.EndedAt = now;
                runFrames++;
            }
            Raise();
        }

        public void SetFinished(string comboKey, string status, string code = null, string reason = null) {
            lock (sync) {
                DateTime now = Now();
                StatusCalibrationQueueItem item = queue.FirstOrDefault(q => q.ComboKey == comboKey);
                if (item != null) {
                    item.Status = status;
                    item.ReasonCode = code;
                    item.Reason = reason;
                    string what = CalibrationText.Describe(item);
                    if (status == Skipped) {
                        Log(now, $"Not taking {what}: {reason}.");
                    } else if (status == Done) {
                        int n = set != null && set.ComboKey == comboKey && takenNow != null ? takenNow.Frames : 0;
                        Log(now, n > 0 ? $"Finished {what}: {n} {(n == 1 ? "frame" : "frames")} this run." : $"Finished {what}.");
                    }
                }
                if (set != null && set.ComboKey == comboKey) {
                    set = null;
                    frame = null;
                    takenNow = null;
                }
                bool first = true;
                foreach (StatusCalibrationQueueItem q in queue) {
                    if (q.Status == Next || q.Status == Waiting) {
                        q.Status = first ? Next : Waiting;
                        first = false;
                    }
                }
            }
            Raise();
        }

        public void RunEnded(string code, string reason) {
            lock (sync) {
                DateTime now = Now();
                if (!running && kind == null && code == null) {
                    return;
                }
                running = false;
                set = null;
                frame = null;
                cooling = null;
                takenNow = null;
                purpose = null;
                foreach (StatusCalibrationQueueItem q in queue) {
                    if (CalibrationPlan.Open(q) && q.Remaining == 0 && q.FramesTotal > 0) {
                        q.Status = Done;
                        continue;
                    }
                    if (q.Status == Taking || q.Status == Next) {
                        q.Status = Waiting;
                    }
                    if (q.Status == Waiting && q.ReasonCode == null && code != null) {
                        q.ReasonCode = "stopped";
                        q.Reason = reason;
                    }
                }
                stop = new StatusCalibrationStop { At = now, Code = code ?? "finished", Reason = reason };
                SetKind(now, KindDone);
                string what = runKind == KindFlats ? "Flats" : "Darks";
                running = true;
                Log(now, string.IsNullOrEmpty(reason) ? $"{what} finished." : $"{what} stopped: {reason}.");
                running = false;
                if (runFrames == 0) {
                    string lines = string.Join("|", queue.Select(q => $"{q.ComboKey}:{q.Status}:{q.ReasonCode}")) + "|" + stop.Code;
                    if (lines == lastQuietRun) {
                        int drop = Math.Min(runLines.Count, log.Count);
                        log.RemoveRange(log.Count - drop, drop);
                    }
                    lastQuietRun = lines;
                } else {
                    lastQuietRun = null;
                }
                runLines.Clear();
            }
            Raise();
        }

        public void RunNotStarted(string runKindName, string place, string code, string reason, double? ambientNow, DateTime? sunLimit) {
            lock (sync) {
                DateTime now = Now();
                running = false;
                if (runKindName != KindFlats) {
                    readyAt = null;
                    planned = new List<StatusCalibrationQueueItem>();
                }
                runKind = runKindName;
                placement = place;
                ambient = ambientNow;
                sunLimitAt = sunLimit;
                bool changed = stop == null || stop.Code != code;
                stop = new StatusCalibrationStop { At = now, Code = code, Reason = reason };
                if (code == "nothing_left") {
                    queue = new List<StatusCalibrationQueueItem>();
                }
                SetKind(now, KindDone);
                if (changed) {
                    Log(now, $"{(runKindName == KindFlats ? "Flats" : "Darks")} not taken: {reason}.");
                }
            }
            Raise();
        }

        public StatusCalibration Snapshot(StatusCalibrationCamera camera) {
            return Snapshot(camera, out _);
        }

        public StatusCalibration Snapshot(StatusCalibrationCamera camera, out bool isRunning) {
            lock (sync) {
                isRunning = running || (sequenceCooling && purpose == "darks");
                DateTime now = Now();
                if (kind == null) {
                    return null;
                }
                var s = new StatusCalibration {
                    Kind = kind,
                    Placement = placement,
                    Purpose = kind == KindCooling ? purpose : null,
                    Since = since,
                    UpdatedAt = now,
                    Camera = camera,
                    Ambient = ambient,
                    SunLimitAt = sunLimitAt,
                    SunLimitDegrees = DarkPlanner.SunLimitDegrees,
                    Cooling = cooling == null ? null : new StatusCalibrationCooling {
                        Target = cooling.Target,
                        From = cooling.From,
                        StartedAt = cooling.StartedAt,
                        EndsAt = cooling.EndsAt,
                        RateCPerMin = cooling.RateCPerMin,
                        Source = cooling.Source
                    },
                    Set = set == null ? null : (StatusCalibrationSet)Clone(set),
                    Frame = frame == null ? null : new StatusCalibrationFrame { Number = frame.Number, Seconds = frame.Seconds, StartedAt = frame.StartedAt, EndsAt = frame.EndsAt },
                    Queue = queue.Select(q => q.Copy()).ToList(),
                    Taken = taken.Select(t => t.Copy()).ToList(),
                    Stop = stop == null ? null : new StatusCalibrationStop { At = stop.At, Code = stop.Code, Reason = stop.Reason },
                    Log = log.Select(l => new StatusCalibrationLog { At = l.At, Text = l.Text }).ToList(),
                };
                if (kind != KindDone && plan != null && (running || purpose == "darks")) {
                    DateTime start = now;
                    double? sp = camera?.Setpoint;
                    if (cooling != null) {
                        sp = cooling.Target;
                        if (cooling.EndsAt.HasValue && cooling.EndsAt.Value > now) {
                            start = cooling.EndsAt.Value;
                        }
                    }
                    CalibrationPlan.Fill(s.Queue, start, sp, plan, s.Frame);
                    if (s.Set != null) {
                        StatusCalibrationQueueItem item = s.Queue.FirstOrDefault(q => q.ComboKey == s.Set.ComboKey);
                        s.Set.EndsAt = item?.PlannedEnd;
                    }
                } else {
                    foreach (StatusCalibrationQueueItem q in s.Queue) {
                        q.PlannedStart = null;
                        q.PlannedEnd = null;
                        q.FitFrames = null;
                    }
                }
                return s;
            }
        }

        private static object Clone(StatusCalibrationSet s) {
            return new StatusCalibrationSet {
                ComboKey = s.ComboKey,
                Exposure = s.Exposure,
                Gain = s.Gain,
                Offset = s.Offset,
                Binning = s.Binning,
                ReadoutMode = s.ReadoutMode,
                SetTemp = s.SetTemp,
                Filter = s.Filter,
                TargetName = s.TargetName,
                TargetId = s.TargetId,
                SessionDate = s.SessionDate,
                Rotation = s.Rotation,
                PanelBrightness = s.PanelBrightness,
                FramesTotal = s.FramesTotal,
                FramesBefore = s.FramesBefore,
                FramesDone = s.FramesDone,
                Index = s.Index,
                Count = s.Count,
                StartedAt = s.StartedAt,
                EndsAt = s.EndsAt
            };
        }
    }
}
