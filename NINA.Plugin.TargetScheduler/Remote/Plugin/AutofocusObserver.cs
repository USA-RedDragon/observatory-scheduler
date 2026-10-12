using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Data;
using NINA.Plugin.TargetScheduler.Shared.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.TargetScheduler.Remote.Plugin {

    public sealed class AutofocusObserver : IFocuserConsumer {
        public const string FailedWithoutBroadcast = "the autofocus run wrote a report but did not announce a new focus position";
        private const long MaxReportBytes = 8 << 20;
        private static readonly TimeSpan ClaimWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan FailureGrace = TimeSpan.FromSeconds(20);
        private const int SaveAttempts = 4;

        private readonly IFocuserMediator focuser;
        private readonly AutofocusStore store;
        private readonly AutofocusLatest latest;
        private readonly Func<string> profileId;
        private readonly Func<(string Name, string Guid)> target;
        private readonly AutofocusReports reports = new AutofocusReports();
        private readonly string directory = Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus");
        private FileSystemWatcher watcher;
        private readonly object recordLock = new object();
        private volatile bool stopped;

        public AutofocusObserver(IFocuserMediator focuser, AutofocusStore store, AutofocusLatest latest, Func<string> profileId, Func<(string Name, string Guid)> target) {
            this.focuser = focuser;
            this.store = store;
            this.latest = latest;
            this.profileId = profileId;
            this.target = target;
        }

        public void Start() {
            try {
                Directory.CreateDirectory(directory);
                watcher = new FileSystemWatcher(directory, "*.json") {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                watcher.Created += OnReportWritten;
                watcher.EnableRaisingEvents = true;
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: cannot watch {directory}, failed runs will not be recorded: {e.Message}");
            }
            focuser?.RegisterConsumer(this);
            TSLogger.Info($"autofocus observer started: focuser={(focuser != null ? "registered" : "missing")}, reports={(watcher != null ? directory : "not watched")}");
        }

        public void Stop() {
            stopped = true;
            try {
                focuser?.RemoveConsumer(this);
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: remove consumer failed: {e.Message}");
            }
            try {
                watcher?.Dispose();
            } catch (Exception) {
            }
            watcher = null;
        }

        public void Dispose() {
            Stop();
        }

        public void UpdateDeviceInfo(FocuserInfo deviceInfo) {
        }

        public void UpdateUserFocused(FocuserInfo info) {
        }

        public void UpdateEndAutoFocusRun(AutoFocusInfo info) {
            try {
                if (stopped) {
                    return;
                }
                var at = DateTime.UtcNow;
                var filter = info?.Filter;
                var temperature = info?.Temperature;
                var position = info?.Position;
                Task.Run(() => RecordSuccess(at, filter, temperature, position));
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: success notification not recorded: {e.Message}");
            }
        }

        private AutofocusCompletion Completion(bool success, string error, DateTime finishedUtc, (string Name, string Guid) tgt) {
            return new AutofocusCompletion {
                Success = success,
                Error = error,
                FinishedAt = finishedUtc,
                ProfileId = profileId(),
                TargetName = tgt.Name,
                TargetGuid = tgt.Guid,
            };
        }

        private void RecordSuccess(DateTime at, string filter, double? temperature, double? position) {
            try {
                var tgt = target();
                lock (recordLock) {
                    if (stopped) {
                        return;
                    }
                    var done = Completion(true, null, at, tgt);
                    done.Filter = filter;
                    done.Temperature = temperature;
                    done.Position = position;
                    var files = Directory.Exists(directory)
                        ? new DirectoryInfo(directory).GetFiles("*.json").Select(f => (f.FullName, f.LastWriteTimeUtc)).ToList()
                        : new List<(string, DateTime)>();
                    var (path, failedRunId) = reports.ClaimForSuccess(files, at, ClaimWindow, done.ProfileId);
                    var report = path == null ? null : AutofocusReport.Parse(Read(path));
                    if (path != null && report == null) {
                        TSLogger.Warning($"autofocus observer: could not parse {path}, recording the run without its points");
                    }
                    if (AutofocusRuns.IsReanalysis(report)) {
                        TSLogger.Info($"autofocus observer: {path} has no duration (a re-analysis of saved frames), not recorded");
                        return;
                    }
                    if (failedRunId != null) {
                        TSLogger.Info($"autofocus observer: success arrived after {path} was recorded as failed, updating run {failedRunId}");
                        done.RunId = failedRunId;
                        done.FinishedAt = File.GetLastWriteTimeUtc(path);
                    }
                    Save(AutofocusRuns.Build(done, report, Previous(done.FinishedAt)), path);
                }
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: recording a successful run failed: {e}");
            }
        }

        private void OnReportWritten(object sender, FileSystemEventArgs e) {
            try {
                var path = e.FullPath;
                if (stopped || !AutofocusReports.Mine(path, profileId())) {
                    return;
                }
                var tgt = target();
                Task.Delay(FailureGrace).ContinueWith(_ => RecordFailure(path, tgt), TaskScheduler.Default);
            } catch (Exception ex) {
                TSLogger.Warning($"autofocus observer: report notification failed: {ex.Message}");
            }
        }

        private void RecordFailure(string path, (string Name, string Guid) tgt) {
            try {
                lock (recordLock) {
                    if (stopped) {
                        return;
                    }
                    var id = reports.ClaimForFailure(path);
                    if (id == null) {
                        return;
                    }
                    var report = AutofocusReport.Parse(Read(path));
                    if (report == null) {
                        TSLogger.Warning($"autofocus observer: could not parse {path}, no run recorded for it");
                        return;
                    }
                    if (AutofocusRuns.IsReanalysis(report)) {
                        return;
                    }
                    var done = Completion(false, FailedWithoutBroadcast, File.GetLastWriteTimeUtc(path), tgt);
                    done.RunId = id;
                    Save(AutofocusRuns.Build(done, report, Previous(done.FinishedAt)), path);
                }
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: recording a failed run failed: {e}");
            }
        }

        private AutofocusRun Previous(DateTime beforeUtc) {
            try {
                return store.PreviousSuccess(beforeUtc);
            } catch (Exception e) {
                TSLogger.Warning($"autofocus observer: could not read the previous run: {e.Message}");
                return null;
            }
        }

        private void Save(AutofocusRun run, string path) {
            latest.Record(run);
            for (int attempt = 1; ; attempt++) {
                try {
                    store.Save(run, DateTime.UtcNow);
                    break;
                } catch (Exception e) when (attempt < SaveAttempts && !stopped) {
                    TSLogger.Warning($"autofocus observer: saving run {run.Id} failed (attempt {attempt} of {SaveAttempts}), retrying: {e.Message}");
                    Thread.Sleep(TimeSpan.FromSeconds(5 * attempt));
                }
            }
            TSLogger.Info($"autofocus observer: recorded run {run.Id} success={run.Success} filter={run.Filter} position={run.ChosenPosition} points={run.Points.Count} report={path ?? "none"}");
        }

        private static string Read(string path) {
            for (int attempt = 0; ; attempt++) {
                try {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length > MaxReportBytes) {
                        return null;
                    }
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    return reader.ReadToEnd();
                } catch (IOException) when (attempt < 4) {
                    Thread.Sleep(250);
                }
            }
        }
    }
}
