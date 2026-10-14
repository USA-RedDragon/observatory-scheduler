using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.MeridianFlip;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Watches the safety monitor while one plan runs. NINA runs the scheduler container's triggers only
    /// between plan instructions, and the Powerups interrupt watchdog never starts inside this container,
    /// so an unsafe sky would wait for the running exposure, slew, center or autofocus to end. When the
    /// monitor stays unsafe and a When Becomes Unsafe trigger is there to act on it, this cancels the
    /// plan's token only: the sequence token is untouched and the container then runs the trigger.
    /// </summary>
    public sealed class UnsafeStopGuard : IDisposable {
        public const string UnsafeTriggerType = "WhenUnsafe";

        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(10);

        private readonly ISafetyMonitorMediator safety;
        private readonly ISequenceContainer owner;
        private readonly ISequenceContainer plan;
        private readonly CancellationTokenSource planCts;
        private readonly UnsafeStopRule rule;
        private readonly List<ISequenceTrigger> unsafeTriggers;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly Task loop;
        private DateTime lastWarning = DateTime.MinValue;

        public UnsafeStopGuard(ISafetyMonitorMediator safety, ISequenceContainer owner, ISequenceContainer plan, CancellationTokenSource planCts, TimeSpan hold) {
            this.safety = safety;
            this.owner = owner;
            this.plan = plan;
            this.planCts = planCts;
            rule = new UnsafeStopRule(hold);
            unsafeTriggers = UnsafeTriggers(owner);
            if (safety == null || unsafeTriggers.Count == 0) {
                loop = Task.CompletedTask;
                return;
            }
            Tick();
            loop = Task.Run(Loop);
        }

        public bool Cancelled => Action == UnsafeStopAction.Cancel;

        public bool Interrupted => Action != UnsafeStopAction.None;

        public IReadOnlyList<ISequenceTrigger> InterruptedFlips { get; private set; } = Array.Empty<ISequenceTrigger>();

        public UnsafeStopAction Action {
            get {
                lock (sync) {
                    return rule.Action;
                }
            }
        }

        private async Task Loop() {
            try {
                while (!stop.IsCancellationRequested) {
                    await Task.Delay(Poll, stop.Token);
                    Tick();
                }
            } catch (OperationCanceledException) {
            }
        }

        private void Tick() {
            try {
                if (stop.IsCancellationRequested || Action != UnsafeStopAction.None) {
                    return;
                }
                (bool connected, bool safe) = ReadSafety();
                bool triggerRunning = UnsafeTriggerRunning(owner);
                UnsafeStopAction action;
                DateTime? since;
                lock (sync) {
                    if (stop.IsCancellationRequested) {
                        return;
                    }
                    action = rule.Observe(DateTime.UtcNow, connected, safe, triggerRunning);
                    since = rule.UnsafeSince;
                }
                if (action == UnsafeStopAction.Cancel && UnsafeTriggerRunning(owner)) {
                    lock (sync) {
                        rule.Downgrade();
                    }
                    action = UnsafeStopAction.SkipRest;
                }
                if (action == UnsafeStopAction.SkipRest) {
                    int skipped = SkipRemaining(plan);
                    Log($"the unsafe trigger is running inside this plan: skipped the {skipped} instruction(s) left in it, so the scheduler plans again with a fresh slew and center when the trigger is done");
                } else if (action == UnsafeStopAction.Cancel) {
                    string running = Running(owner, plan);
                    InterruptedFlips = RunningTriggers(owner).Where(t => t is MeridianFlipTrigger).ToList();
                    Task.Run(() => {
                        try {
                            planCts.Cancel();
                        } catch (ObjectDisposedException) {
                        }
                    });
                    Log($"safety monitor unsafe since {since?.ToLocalTime():HH:mm:ss}: cancelled the running plan instruction ({running}) so the unsafe trigger runs now; the sequence keeps running and a cancelled exposure is not counted");
                }
            } catch (Exception ex) {
                TSLogger.Error($"safety watch failed: {ex}");
            }
        }

        private (bool connected, bool safe) ReadSafety() {
            SafetyMonitorInfo info;
            try {
                info = safety.GetInfo();
            } catch (Exception ex) {
                if (DateTime.UtcNow - lastWarning > WarnEvery) {
                    lastWarning = DateTime.UtcNow;
                    TSLogger.Warning($"could not read the safety monitor: {ex.Message}");
                }
                return (false, false);
            }
            if (info == null || !info.Connected) {
                return (false, false);
            }
            bool safe = info.IsSafe;
            foreach (ISequenceTrigger trigger in unsafeTriggers) {
                if (TriggerCheck(trigger) is bool check) {
                    safe = check;
                    break;
                }
            }
            return (true, safe);
        }

        private static bool? TriggerCheck(ISequenceTrigger trigger) {
            try {
                MethodInfo check = trigger.GetType().GetMethod("Check", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return check?.ReturnType == typeof(bool) ? (bool)check.Invoke(trigger, null) : null;
            } catch (Exception) {
                return null;
            }
        }

        private static void Log(string message) {
            TSLogger.Warning(message);
            Logger.Warning($"Target Scheduler: {message}");
        }

        private static IEnumerable<ISequenceTrigger> ChainTriggers(ISequenceContainer container) {
            for (ISequenceContainer c = container; c != null; c = c.Parent) {
                if (c is ITriggerable triggerable) {
                    foreach (ISequenceTrigger trigger in triggerable.GetTriggersSnapshot() ?? Enumerable.Empty<ISequenceTrigger>()) {
                        if (trigger != null) {
                            yield return trigger;
                        }
                    }
                }
            }
        }

        private static IEnumerable<ISequenceTrigger> RunningTriggers(ISequenceContainer container) {
            return ChainTriggers(container).Where(t => t.Status == SequenceEntityStatus.RUNNING);
        }

        /// <summary>
        /// The enabled When Becomes Unsafe triggers on this container and its ancestors, nearest first.
        /// </summary>
        public static List<ISequenceTrigger> UnsafeTriggers(ISequenceContainer container) {
            return ChainTriggers(container).Where(t => t.GetType().Name == UnsafeTriggerType && t.Status != SequenceEntityStatus.DISABLED).ToList();
        }

        public static bool UnsafeTriggerRunning(ISequenceContainer container) {
            return RunningTriggers(container).Any(t => t.GetType().Name == UnsafeTriggerType);
        }

        public static int SkipRemaining(ISequenceContainer container) {
            int skipped = 0;
            foreach (ISequenceItem item in container.GetItemsSnapshot() ?? Enumerable.Empty<ISequenceItem>()) {
                if (item == null) {
                    continue;
                }
                if (item.Status == SequenceEntityStatus.CREATED) {
                    item.Skip();
                    skipped++;
                } else if (item.Status == SequenceEntityStatus.RUNNING && item is ISequenceContainer child) {
                    skipped += SkipRemaining(child);
                }
            }
            return skipped;
        }

        public static string Running(ISequenceContainer owner, ISequenceContainer plan) {
            List<string> names = RunningTriggers(owner).Select(t => $"trigger {t.Name}").ToList();
            Collect(plan, names);
            return names.Count == 0 ? "between instructions" : string.Join(" > ", names);
        }

        private static void Collect(ISequenceContainer container, List<string> names) {
            ISequenceItem running = (container.GetItemsSnapshot() ?? Enumerable.Empty<ISequenceItem>()).FirstOrDefault(i => i != null && i.Status == SequenceEntityStatus.RUNNING);
            if (container is ITriggerable triggerable) {
                ISequenceTrigger trigger = (triggerable.GetTriggersSnapshot() ?? Enumerable.Empty<ISequenceTrigger>()).FirstOrDefault(t => t != null && t.Status == SequenceEntityStatus.RUNNING);
                if (trigger != null) {
                    names.Add($"trigger {trigger.Name}");
                }
            }
            if (running == null) {
                return;
            }
            names.Add(running.Name);
            if (running is ISequenceContainer child) {
                Collect(child, names);
            }
        }

        public void Dispose() {
            stop.Cancel();
            try {
                loop.Wait(TimeSpan.FromSeconds(5));
            } catch (AggregateException) {
            }
            stop.Dispose();
        }
    }
}
