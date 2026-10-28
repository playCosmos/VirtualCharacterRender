using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Diagnostics;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        private const int MaxWorkspaceLogHistory = 200;
        private const int MaxWorkspaceLogPending = 512;
        private readonly ConcurrentQueue<WorkspaceLogEvent> _workspacePendingLogs =
            new ConcurrentQueue<WorkspaceLogEvent>();
        private readonly Queue<WorkspaceLogEvent> _workspaceLogHistory =
            new Queue<WorkspaceLogEvent>();
        private ScrollView _workspaceLogList;
        private Label _workspaceLogSummary;
        private bool _workspaceErrorsOnly;
        private bool _workspaceLogSubscribed;
        private int _workspacePendingLogCount;
        private int _workspaceDroppedLogCount;
        private RuntimeDiagnostics _workspaceDiagnostics;
        private Label _workspaceDiagnosticsStatus;
        private Label _workspaceDiagnosticsFeedback;
        private float _nextWorkspaceDiagnosticsRefresh;
        private int _workspaceDiagnosticsMetricPage;
        private Label _workspaceDiagnosticsDetails;
        private Label _workspaceDiagnosticsPageLabel;
        private VisualElement _workspaceDiagnosticsMetricList;
        private Button _workspaceDiagnosticsPreviousPage;
        private Button _workspaceDiagnosticsNextPage;
        private Button _workspaceDiagnosticsCsvButton;
        private Button _workspaceDiagnosticsConsoleButton;
        private const int WorkspaceDiagnosticsPageSize = 12;

        private readonly struct WorkspaceLogEvent
        {
            public WorkspaceLogEvent(string message, LogType type)
            {
                // Cap per-message memory too; a bounded queue alone does not
                // protect against unbounded exception or plugin log strings.
                Message = message == null ? "" :
                    message.Length > 4096 ? message.Substring(0, 4096) + "…" : message;
                Type = type;
                Timestamp = DateTime.UtcNow.ToString("HH:mm:ss");
            }

            public string Message { get; }
            public LogType Type { get; }
            public string Timestamp { get; }
        }

        private void SubscribeWorkspaceLog()
        {
            if (_workspaceLogSubscribed) return;
            UnityEngine.Application.logMessageReceivedThreaded += OnWorkspaceLogReceived;
            _workspaceLogSubscribed = true;
        }

        private void UnsubscribeWorkspaceLog()
        {
            if (!_workspaceLogSubscribed) return;
            UnityEngine.Application.logMessageReceivedThreaded -= OnWorkspaceLogReceived;
            _workspaceLogSubscribed = false;
        }

        private void OnWorkspaceLogReceived(string condition, string stackTrace, LogType type)
        {
            // Unity invokes this from arbitrary threads. No UI or Unity object access here.
            if (Interlocked.Increment(ref _workspacePendingLogCount) > MaxWorkspaceLogPending)
            {
                Interlocked.Decrement(ref _workspacePendingLogCount);
                Interlocked.Increment(ref _workspaceDroppedLogCount);
                return;
            }
            _workspacePendingLogs.Enqueue(new WorkspaceLogEvent(condition, type));
        }

        private void ResetWorkspaceLogPanel()
        {
            _workspaceLogList = null;
            _workspaceLogSummary = null;
            _workspaceDiagnostics = null;
            _workspaceDiagnosticsStatus = null;
            _workspaceDiagnosticsFeedback = null;
            _nextWorkspaceDiagnosticsRefresh = 0f;
            _workspaceDiagnosticsMetricPage = 0;
            _workspaceDiagnosticsDetails = null;
            _workspaceDiagnosticsPageLabel = null;
            _workspaceDiagnosticsMetricList = null;
            _workspaceDiagnosticsPreviousPage = null;
            _workspaceDiagnosticsNextPage = null;
            _workspaceDiagnosticsCsvButton = null;
            _workspaceDiagnosticsConsoleButton = null;
            _workspaceErrorsOnly = false;
            _workspaceLogHistory.Clear();
            while (_workspacePendingLogs.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _workspacePendingLogCount, 0);
            Interlocked.Exchange(ref _workspaceDroppedLogCount, 0);
        }

        private void BuildWorkspaceLogPanel(VisualElement panel)
        {
            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.flexWrap = Wrap.Wrap;
            var errors = new Toggle("Warnings/errors only");
            errors.value = _workspaceErrorsOnly;
            errors.RegisterValueChangedCallback(evt =>
            {
                _workspaceErrorsOnly = evt.newValue;
                RefreshWorkspaceLogView();
            });
            controls.Add(errors);
            controls.Add(new Button(() =>
            {
                _workspaceLogHistory.Clear();
                _workspaceLogList?.Clear();
                if (_workspaceLogSummary != null) _workspaceLogSummary.text = "Log cleared.";
            }) { text = "Clear" });
            controls.Add(new Button(() =>
            {
                var builder = new StringBuilder();
                foreach (var log in _workspaceLogHistory)
                    if (!_workspaceErrorsOnly || IsWorkspaceLogVisible(log))
                        builder.Append('[').Append(log.Timestamp).Append("] [")
                            .Append(log.Type).Append("] ").AppendLine(log.Message);
                GUIUtility.systemCopyBuffer = builder.ToString();
            }) { text = "Copy visible" });
            panel.Add(controls);
            BuildWorkspaceDiagnosticsControls(panel);
            _workspaceLogSummary = new Label("Waiting for application log events...");
            panel.Add(_workspaceLogSummary);
            _workspaceLogList = new ScrollView();
            _workspaceLogList.style.flexGrow = 1;
            _workspaceLogList.style.minHeight = 60;
            panel.Add(_workspaceLogList);
            RefreshWorkspaceLogView();
        }

        private static bool IsWorkspaceLogVisible(WorkspaceLogEvent log)
        {
            return log.Type == LogType.Warning || log.Type == LogType.Error ||
                   log.Type == LogType.Exception || log.Type == LogType.Assert;
        }

        private void DrainWorkspaceLog()
        {
            if (_workspaceLogList == null) return;
            var changed = false;
            for (var i = 0; i < 128 && _workspacePendingLogs.TryDequeue(out var evt); i++)
            {
                Interlocked.Decrement(ref _workspacePendingLogCount);
                _workspaceLogHistory.Enqueue(evt);
                if (_workspaceLogHistory.Count > MaxWorkspaceLogHistory)
                    _workspaceLogHistory.Dequeue();
                changed = true;
            }
            if (changed)
                RefreshWorkspaceLogView();
            else if (_workspaceLogSummary != null &&
                     Volatile.Read(ref _workspaceDroppedLogCount) > 0)
                _workspaceLogSummary.text = "Recent logs: " + _workspaceLogHistory.Count +
                    " | Dropped during overload: " + Volatile.Read(ref _workspaceDroppedLogCount);
        }

        private void RefreshWorkspaceLogView()
        {
            if (_workspaceLogList == null) return;
            _workspaceLogList.Clear();
            Label last = null;
            foreach (var evt in _workspaceLogHistory)
            {
                if (_workspaceErrorsOnly && !IsWorkspaceLogVisible(evt))
                    continue;
                var message = evt.Message.Length > 300
                    ? evt.Message.Substring(0, 300) + "…" : evt.Message;
                last = new Label("[" + evt.Timestamp + "] [" + evt.Type + "] " + message);
                last.style.whiteSpace = WhiteSpace.Normal;
                if (evt.Type == LogType.Error || evt.Type == LogType.Exception ||
                    evt.Type == LogType.Assert)
                    last.style.color = new Color(1f, 0.55f, 0.48f);
                else if (evt.Type == LogType.Warning)
                    last.style.color = new Color(1f, 0.8f, 0.48f);
                _workspaceLogList.Add(last);
            }
            _workspaceLogSummary.text = "Recent logs: " + _workspaceLogHistory.Count +
                " | Dropped during overload: " + Volatile.Read(ref _workspaceDroppedLogCount);
            if (last != null)
                _workspaceLogList.ScrollTo(last);
        }

        private RuntimeDiagnostics ResolveWorkspaceDiagnostics()
        {
            if (_workspaceDiagnostics == null)
                _workspaceDiagnostics = FindFirstObjectByType<RuntimeDiagnostics>();
            return _workspaceDiagnostics;
        }

        private void BuildWorkspaceDiagnosticsControls(VisualElement panel)
        {
            var diagnostics = new VisualElement();
            diagnostics.style.marginTop = 5;
            _workspaceDiagnosticsStatus = new Label("Runtime diagnostics: checking...");
            diagnostics.Add(_workspaceDiagnosticsStatus);
            _workspaceDiagnosticsFeedback = new Label("");
            _workspaceDiagnosticsFeedback.style.whiteSpace = WhiteSpace.Normal;
            diagnostics.Add(_workspaceDiagnosticsFeedback);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexWrap = Wrap.Wrap;
            buttons.Add(new Button(() =>
            {
                var service = ResolveWorkspaceDiagnostics();
                if (service == null)
                {
                    _workspaceDiagnosticsFeedback.text = "Runtime diagnostics unavailable.";
                    return;
                }
                try
                {
                    if (!service.TryCaptureNow(out var snapshot, out var error))
                    {
                        _workspaceDiagnosticsFeedback.text =
                            "Capture unavailable: " + (error ?? "unknown error");
                        return;
                    }
                    _workspaceDiagnosticsMetricPage = 0;
                    _nextWorkspaceDiagnosticsRefresh = 0f;
                    RefreshWorkspaceDiagnosticsPanel();
                }
                catch (Exception exception)
                {
                    _workspaceDiagnosticsFeedback.text = "Capture failed: " + exception.Message;
                    Debug.LogException(exception, this);
                }
            }) { text = "Capture metrics" });
            buttons.Add(new Button(() =>
            {
                var service = ResolveWorkspaceDiagnostics();
                if (service == null)
                {
                    _workspaceDiagnosticsFeedback.text = "Runtime diagnostics unavailable.";
                    return;
                }
                try
                {
                    if (service.LatestSnapshot.Sequence <= 0 &&
                        !service.TryCaptureNow(out _, out var captureError))
                    {
                        _workspaceDiagnosticsFeedback.text =
                            "Export requires a captured report: " +
                            (captureError ?? "unknown error");
                        return;
                    }
                    if (!service.TryWriteLatestSnapshotJson(out var path, out var error))
                    {
                        _workspaceDiagnosticsFeedback.text =
                            "Export failed: " + (error ?? "unknown error");
                        return;
                    }
                    _workspaceDiagnosticsFeedback.text = "Diagnostics JSON saved: " + path;
                }
                catch (Exception exception)
                {
                    _workspaceDiagnosticsFeedback.text = "Export failed: " + exception.Message;
                    Debug.LogException(exception, this);
                }
            }) { text = "Export latest JSON" });
            _workspaceDiagnosticsCsvButton = new Button(() =>
            {
                var service = ResolveWorkspaceDiagnostics();
                if (service == null)
                {
                    _workspaceDiagnosticsFeedback.text = "Runtime diagnostics unavailable.";
                    return;
                }
                try
                {
                    service.SetCsvEvidence(!service.CsvEvidenceEnabled);
                    _workspaceDiagnosticsFeedback.text = "CSV evidence " +
                        (service.CsvEvidenceEnabled ? "enabled." : "disabled.");
                }
                catch (Exception exception)
                {
                    _workspaceDiagnosticsFeedback.text = "CSV evidence update failed: " +
                        exception.Message;
                    Debug.LogException(exception, this);
                }
                _nextWorkspaceDiagnosticsRefresh = 0f;
                RefreshWorkspaceDiagnosticsPanel();
            }) { text = "CSV evidence: n/a" };
            buttons.Add(_workspaceDiagnosticsCsvButton);
            _workspaceDiagnosticsConsoleButton = new Button(() =>
            {
                var service = ResolveWorkspaceDiagnostics();
                if (service == null)
                {
                    _workspaceDiagnosticsFeedback.text = "Runtime diagnostics unavailable.";
                    return;
                }
                try
                {
                    service.SetConsoleLogging(!service.ConsoleLoggingEnabled);
                    _workspaceDiagnosticsFeedback.text = "Console logging " +
                        (service.ConsoleLoggingEnabled ? "enabled." : "disabled.");
                }
                catch (Exception exception)
                {
                    _workspaceDiagnosticsFeedback.text = "Console logging update failed: " +
                        exception.Message;
                    Debug.LogException(exception, this);
                }
                _nextWorkspaceDiagnosticsRefresh = 0f;
                RefreshWorkspaceDiagnosticsPanel();
            }) { text = "Console logging: n/a" };
            buttons.Add(_workspaceDiagnosticsConsoleButton);
            diagnostics.Add(buttons);
            _workspaceDiagnosticsDetails = new Label("No snapshot captured yet.");
            _workspaceDiagnosticsDetails.style.whiteSpace = WhiteSpace.Normal;
            diagnostics.Add(_workspaceDiagnosticsDetails);
            var pages = new VisualElement();
            pages.style.flexDirection = FlexDirection.Row;
            pages.style.flexWrap = Wrap.Wrap;
            _workspaceDiagnosticsPreviousPage = new Button(() =>
                ChangeWorkspaceDiagnosticsMetricPage(-1)) { text = "Previous metrics" };
            _workspaceDiagnosticsNextPage = new Button(() =>
                ChangeWorkspaceDiagnosticsMetricPage(1)) { text = "Next metrics" };
            _workspaceDiagnosticsPageLabel = new Label("Metrics: 0");
            pages.Add(_workspaceDiagnosticsPreviousPage);
            pages.Add(_workspaceDiagnosticsPageLabel);
            pages.Add(_workspaceDiagnosticsNextPage);
            diagnostics.Add(pages);
            _workspaceDiagnosticsMetricList = new VisualElement();
            diagnostics.Add(_workspaceDiagnosticsMetricList);
            panel.Add(diagnostics);
            RefreshWorkspaceDiagnosticsPanel();
        }

        private void ChangeWorkspaceDiagnosticsMetricPage(int direction)
        {
            var service = ResolveWorkspaceDiagnostics();
            if (service == null || service.LatestSnapshot.Sequence <= 0) return;
            var count = service.LatestSnapshot.Metrics?.Count ?? 0;
            var pages = Math.Max(1, (count + WorkspaceDiagnosticsPageSize - 1) /
                WorkspaceDiagnosticsPageSize);
            _workspaceDiagnosticsMetricPage =
                (_workspaceDiagnosticsMetricPage + direction + pages) % pages;
            _nextWorkspaceDiagnosticsRefresh = 0f;
            RefreshWorkspaceDiagnosticsPanel();
        }

        private static string WorkspaceMetricValue(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? "n/a" : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void RefreshWorkspaceDiagnosticsPanel()
        {
            if (_workspaceDiagnosticsStatus == null ||
                Time.unscaledTime < _nextWorkspaceDiagnosticsRefresh)
                return;
            _nextWorkspaceDiagnosticsRefresh = Time.unscaledTime + 2f;
            var service = ResolveWorkspaceDiagnostics();
            _workspaceDiagnosticsCsvButton?.SetEnabled(service != null);
            _workspaceDiagnosticsConsoleButton?.SetEnabled(service != null);
            _workspaceDiagnosticsCsvButton.text = service == null
                ? "CSV evidence: n/a" :
                "CSV evidence: " + (service.CsvEvidenceEnabled ? "On" : "Off");
            _workspaceDiagnosticsConsoleButton.text = service == null
                ? "Console logging: n/a" :
                "Console logging: " + (service.ConsoleLoggingEnabled ? "On" : "Off");
            if (service == null)
            {
                _workspaceDiagnosticsStatus.text = "Runtime diagnostics service not found.";
                _workspaceDiagnosticsDetails.text = "No diagnostics service.";
                _workspaceDiagnosticsMetricList.Clear();
                _workspaceDiagnosticsPageLabel.text = "Metrics: 0";
                _workspaceDiagnosticsPreviousPage.SetEnabled(false);
                _workspaceDiagnosticsNextPage.SetEnabled(false);
                return;
            }
            try
            {
                var snapshot = service.LatestSnapshot;
                var metrics = snapshot.Metrics;
                var count = metrics?.Count ?? 0;
                var pageCount = Math.Max(1, (count + WorkspaceDiagnosticsPageSize - 1) /
                    WorkspaceDiagnosticsPageSize);
                _workspaceDiagnosticsMetricPage = Math.Max(0,
                    Math.Min(_workspaceDiagnosticsMetricPage, pageCount - 1));
                var hasMetrics = snapshot.Sequence > 0 && pageCount > 1;
                _workspaceDiagnosticsPreviousPage.SetEnabled(hasMetrics);
                _workspaceDiagnosticsNextPage.SetEnabled(hasMetrics);
                _workspaceDiagnosticsPageLabel.text = "Subsystem metrics: " + count +
                    " | Page " + (_workspaceDiagnosticsMetricPage + 1) + "/" + pageCount;
                _workspaceDiagnosticsMetricList.Clear();
                if (snapshot.Sequence <= 0)
                {
                    _workspaceDiagnosticsStatus.text =
                        "Runtime diagnostics: waiting for first report.";
                    _workspaceDiagnosticsDetails.text =
                        "Reporting every " +
                        service.ReportIntervalSeconds.ToString("0.###",
                            CultureInfo.InvariantCulture) +
                        "s | Evidence directory: " + service.EvidenceDirectory;
                    return;
                }
                _workspaceDiagnosticsStatus.text = "Diagnostics #" + snapshot.Sequence +
                    " | Frame avg " + WorkspaceMetricValue(snapshot.FrameAverageMs) +
                    " ms | P95 " + WorkspaceMetricValue(snapshot.FrameP95Ms) +
                    " ms | P99 " + WorkspaceMetricValue(snapshot.FrameP99Ms) + " ms";
                _workspaceDiagnosticsDetails.text =
                    "Tracking Hz: face " + WorkspaceMetricValue(snapshot.FaceHz) +
                    " | body/hands " + WorkspaceMetricValue(snapshot.BodyHandsHz) +
                    " | full body " + WorkspaceMetricValue(snapshot.FullBodyHz) +
                    " | expressions " + WorkspaceMetricValue(snapshot.ExpressionHz) +
                    "\nTracking age (ms): face " + WorkspaceMetricValue(snapshot.FaceAgeMs) +
                    " | body/hands " + WorkspaceMetricValue(snapshot.BodyHandsAgeMs) +
                    " | full body " + WorkspaceMetricValue(snapshot.FullBodyAgeMs) +
                    " | expressions " + WorkspaceMetricValue(snapshot.ExpressionAgeMs) +
                    "\nPresence: " + (snapshot.Presence.HasValue
                        ? snapshot.Presence.Value.SubjectState.ToString() : "n/a") +
                    " | Report interval " +
                    service.ReportIntervalSeconds.ToString("0.###",
                        CultureInfo.InvariantCulture) +
                    "s\nEvidence directory: " + service.EvidenceDirectory;
                if (count == 0)
                {
                    _workspaceDiagnosticsMetricList.Add(
                        new Label("No subsystem metrics in this snapshot."));
                    return;
                }
                var startIndex = _workspaceDiagnosticsMetricPage * WorkspaceDiagnosticsPageSize;
                var endIndex = Math.Min(count, startIndex + WorkspaceDiagnosticsPageSize);
                for (var i = startIndex; i < endIndex; i++)
                {
                    var metric = metrics[i];
                    var label = new Label(metric.Name + " = " +
                        WorkspaceMetricValue(metric.Value) +
                        (string.IsNullOrWhiteSpace(metric.Unit) ? "" :
                            " " + metric.Unit));
                    label.style.whiteSpace = WhiteSpace.Normal;
                    _workspaceDiagnosticsMetricList.Add(label);
                }
            }
            catch (Exception exception)
            {
                _workspaceDiagnosticsStatus.text = "Diagnostics read failed: " +
                    exception.Message;
                _workspaceDiagnosticsMetricList.Clear();
                Debug.LogException(exception, this);
            }
        }
    }
}
