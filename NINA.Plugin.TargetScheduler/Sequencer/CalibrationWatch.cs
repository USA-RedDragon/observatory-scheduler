using NINA.Core.Enum;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Darks;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using NINA.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NINA.Plugin.TargetScheduler.Sequencer {

    /// <summary>
    /// Finds a Cool Camera instruction the sequence runs outside the plugin, so the status can show the cool-down's
    /// target. The sequence root is remembered from the plugin's own instructions, since NINA gives plugins no handle on it.
    /// </summary>
    public static class CalibrationWatch {
        private static readonly object sync = new object();
        private static WeakReference<ISequenceEntity> root;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> containerProps = new ConcurrentDictionary<Type, PropertyInfo[]>();
        private static readonly LogThrottle warn = new LogThrottle(TimeSpan.FromMinutes(30));

        public static void Remember(ISequenceEntity item) {
            ISequenceEntity top = item;
            int guard = 0;
            while (top?.Parent != null && guard++ < 200) {
                top = top.Parent;
            }
            if (top == null) {
                return;
            }
            lock (sync) {
                root = new WeakReference<ISequenceEntity>(top);
            }
        }

        private static ISequenceEntity Root() {
            lock (sync) {
                return root != null && root.TryGetTarget(out ISequenceEntity r) ? r : null;
            }
        }

        public static void Poll(ICameraMediator camera) {
            try {
                CalibrationActivity activity = CalibrationActivity.Current;
                ISequenceEntity found = Root() == null ? null : FindRunningCoolCamera(Root());
                CameraInfo info = camera?.GetInfo();
                double? setpoint = info != null && info.Connected && !double.IsNaN(info.TemperatureSetPoint) ? info.TemperatureSetPoint : null;
                if (found == null) {
                    if (activity.SequenceCooling) {
                        activity.SequenceCoolingEnded(setpoint);
                    }
                    return;
                }
                (double? target, double? minutes) = Read(found);
                if (!target.HasValue) {
                    return;
                }
                double? sensor = info != null && info.Connected && !double.IsNaN(info.Temperature) ? info.Temperature : null;
                CoolerRates rates = DarkRuntime.State.Rates();
                var ctx = new CalibrationPlanContext {
                    Overhead = DarkRuntime.State.SaveTime() ?? TimeSpan.Zero,
                    Move = (a, b) => DarkPlanner.MoveTime(a, b, rates) ?? DarkPlanner.Ramp(a, b),
                    Limit = null
                };
                activity.SequenceCoolingSeen(target.Value, minutes, sensor, setpoint, ctx);
            } catch (Exception e) {
                if (warn.Allow("poll", DateTime.UtcNow)) {
                    TSLogger.Warning($"calibration watch: could not read the sequence's cool-down: {e.Message}");
                }
            }
        }

        public static (double? Target, double? Minutes) Read(ISequenceEntity item) {
            double? target = Number(item, "TempExpr") ?? Number(item, "Temperature");
            double? minutes = Number(item, "DurExpr") ?? Number(item, "Duration");
            return (target, minutes);
        }

        private static double? Number(object item, string name) {
            PropertyInfo p = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) {
                return null;
            }
            object v = p.GetValue(item);
            if (v == null) {
                return null;
            }
            if (v is double d) {
                return double.IsNaN(d) ? null : d;
            }
            PropertyInfo value = v.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (value != null && value.GetValue(v) is double e && !double.IsNaN(e)) {
                return e;
            }
            return null;
        }

        public static ISequenceEntity FindRunningCoolCamera(ISequenceEntity top) {
            var stack = new Stack<(ISequenceEntity Entity, int Depth)>();
            var seen = new HashSet<ISequenceEntity>(ReferenceEqualityComparer.Instance);
            stack.Push((top, 0));
            while (stack.Count > 0) {
                (ISequenceEntity e, int depth) = stack.Pop();
                if (e == null || depth > 60 || !seen.Add(e)) {
                    continue;
                }
                try {
                    if (e is ISequenceItem && e.GetType().Name == "CoolCamera" && e.Status == SequenceEntityStatus.RUNNING) {
                        return e;
                    }
                } catch (Exception) {
                    continue;
                }
                if (e is ISequenceContainer c) {
                    IEnumerable<ISequenceItem> items;
                    try {
                        items = c.GetItemsSnapshot();
                    } catch (Exception) {
                        items = Array.Empty<ISequenceItem>();
                    }
                    foreach (ISequenceItem i in items ?? Array.Empty<ISequenceItem>()) {
                        stack.Push((i, depth + 1));
                    }
                }
                foreach (PropertyInfo p in ContainerProperties(e.GetType())) {
                    object v;
                    try {
                        v = p.GetValue(e);
                    } catch (Exception) {
                        continue;
                    }
                    if (v is ISequenceEntity child) {
                        stack.Push((child, depth + 1));
                    }
                }
            }
            return null;
        }

        private static PropertyInfo[] ContainerProperties(Type t) {
            return containerProps.GetOrAdd(t, type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && typeof(ISequenceContainer).IsAssignableFrom(p.PropertyType) && p.Name != "Parent")
                .ToArray());
        }
    }
}
