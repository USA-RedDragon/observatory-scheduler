using NINA.Plugin.TargetScheduler.Remote.Commands;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Api {

    public static class Activities {
        public const string Preparing = "preparing";
        public const string Slewing = "slewing";
        public const string Centering = "centering";
        public const string Focusing = "focusing";
        public const string Guiding = "guiding";
        public const string Dithering = "dithering";
        public const string MeridianFlip = "meridian_flip";
        public const string SwitchingFilter = "switching_filter";
        public const string Waiting = "waiting";
        public const string StartingExposure = "starting_exposure";
        public const string Exposing = "exposing";
        public const string Downloading = "downloading";
        public const string Saving = "saving";
        public const string Busy = "busy";
        public const string Exposure = "exposure";
    }

    public class RunningStep {
        public string Type { get; set; }
        public string Name { get; set; }

        public static RunningStep Of(string type, string name) => new RunningStep { Type = type, Name = name };
    }

    public static class ActivityClassifier {
        private static readonly (string match, string activity)[] rules = {
            ("TakeExposure", Activities.Exposure),
            ("TakeSubframeExposure", Activities.Exposure),
            ("MeridianFlip", Activities.MeridianFlip),
            ("Autofocus", Activities.Focusing),
            ("AutoFocus", Activities.Focusing),
            ("Dither", Activities.Dithering),
            ("Center", Activities.Centering),
            ("Solve", Activities.Centering),
            ("Slew", Activities.Slewing),
            ("SwitchFilter", Activities.SwitchingFilter),
            ("Guid", Activities.Guiding),
            ("Wait", Activities.Waiting),
        };

        private static readonly string[] structural = { "Container", "TriggerRunner", "PlanSchedulerProgress" };

        public static string Classify(string type) {
            if (string.IsNullOrEmpty(type)) {
                return null;
            }
            var simple = type.Substring(type.LastIndexOf('.') + 1);
            foreach (var s in structural) {
                if (simple.Contains(s)) {
                    return null;
                }
            }
            foreach (var (match, activity) in rules) {
                if (simple.Contains(match)) {
                    return activity;
                }
            }
            return Activities.Busy;
        }

        public static (string activity, string detail) Classify(IReadOnlyList<RunningStep> chain) {
            if (chain == null || chain.Count == 0) {
                return (null, null);
            }
            string busy = null;
            foreach (var step in chain) {
                var a = Classify(step.Type);
                if (a == null) {
                    continue;
                }
                if (a != Activities.Busy) {
                    return (a, step.Name);
                }
                busy ??= step.Name;
            }
            return busy != null ? (Activities.Busy, busy) : (null, null);
        }
    }

    public class ExposureView {
        public StatusExposure Exposure { get; set; }
        public string Activity { get; set; }
        public string ActivityDetail { get; set; }
        public DateTime? ActivitySince { get; set; }
    }

    public class ExposureTracker {
        private readonly object sync = new object();
        private readonly Func<DateTime, DateTime> nightOf;
        private readonly Func<int, DateTime, int> taken;
        private readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        private DateTime? night;
        private bool inPlan;
        private int? targetId;
        private StatusExposure open;
        private bool afterShutter;
        private string lastActivity;
        private DateTime? since;

        public ExposureTracker(Func<DateTime, DateTime> nightOf = null, Func<int, DateTime, int> taken = null) {
            this.nightOf = nightOf ?? LocalNight;
            this.taken = taken;
        }

        public static DateTime LocalNight(DateTime utc) {
            var local = utc.ToLocalTime();
            var noon = DateTime.SpecifyKind(local.Date.AddHours(12), DateTimeKind.Local);
            return (local.Hour < 12 ? noon.AddDays(-1) : noon).ToUniversalTime();
        }

        public bool InPlan {
            get {
                lock (sync) {
                    return inPlan;
                }
            }
        }

        public void PlanStarted(int target) {
            lock (sync) {
                targetId = target;
                inPlan = true;
                open = null;
                afterShutter = false;
            }
        }

        public void PlanEnded() {
            lock (sync) {
                inPlan = false;
                open = null;
                afterShutter = false;
            }
        }

        public void Reset() {
            lock (sync) {
                inPlan = false;
                open = null;
                afterShutter = false;
                targetId = null;
                counts.Clear();
                night = null;
                lastActivity = null;
                since = null;
            }
        }

        public StatusExposure ShutterOpened(int planId, string filter, double seconds, DateTime at) {
            lock (sync) {
                var n = nightOf(at);
                if (night != n) {
                    counts.Clear();
                    night = n;
                }
                var key = targetId ?? -1;
                if (!counts.TryGetValue(key, out var number) && targetId != null && taken != null) {
                    try {
                        number = taken(key, n);
                    } catch (Exception) {
                        number = 0;
                    }
                }
                number++;
                counts[key] = number;
                afterShutter = false;
                open = new StatusExposure {
                    PlanId = planId,
                    Filter = filter,
                    Seconds = seconds,
                    StartedAt = at,
                    EndsAt = at.AddSeconds(seconds),
                    Number = number,
                };
                return Copy(open);
            }
        }

        public void CaptureAborted() {
            lock (sync) {
                if (open == null) {
                    return;
                }
                var key = targetId ?? -1;
                if (counts.TryGetValue(key, out var number) && number > 0) {
                    counts[key] = number - 1;
                }
                open = null;
                afterShutter = false;
            }
        }

        public void CaptureEnded() {
            lock (sync) {
                if (open != null) {
                    open = null;
                    afterShutter = true;
                }
            }
        }

        public ExposureView View(DateTime now, IReadOnlyList<RunningStep> running) {
            lock (sync) {
                var view = new ExposureView();
                if (inPlan) {
                    var (stepActivity, detail) = ActivityClassifier.Classify(running);
                    if (open != null && now < open.EndsAt) {
                        view.Exposure = Copy(open);
                        view.Activity = Activities.Exposing;
                    } else if (open != null) {
                        view.Exposure = Copy(open);
                        view.Activity = Activities.Downloading;
                    } else if (stepActivity != null && stepActivity != Activities.Exposure) {
                        view.Activity = stepActivity;
                        view.ActivityDetail = detail;
                    } else if (afterShutter) {
                        view.Activity = Activities.Saving;
                    } else if (stepActivity == Activities.Exposure) {
                        view.Activity = Activities.StartingExposure;
                    } else {
                        view.Activity = Activities.Preparing;
                    }
                }
                if (view.Activity != lastActivity) {
                    lastActivity = view.Activity;
                    since = view.Activity == null ? null : now;
                }
                view.ActivitySince = since;
                return view;
            }
        }

        public PendingTiming Pending(DateTime now) {
            lock (sync) {
                if (!inPlan) {
                    return PendingTiming.At(null, PendingTiming.BeforeNextExposure);
                }
                if (open != null && now < open.EndsAt) {
                    return PendingTiming.At(open.EndsAt, PendingTiming.WhenExposureEnds);
                }
                if (open != null || afterShutter) {
                    return PendingTiming.At(null, PendingTiming.AfterSaving);
                }
                return PendingTiming.At(null, PendingTiming.AfterNextExposure);
            }
        }

        private static StatusExposure Copy(StatusExposure e) {
            return new StatusExposure {
                PlanId = e.PlanId,
                Filter = e.Filter,
                Seconds = e.Seconds,
                StartedAt = e.StartedAt,
                EndsAt = e.EndsAt,
                Number = e.Number,
            };
        }
    }
}
