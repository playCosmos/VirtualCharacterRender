using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            diagnostics.Add(buttons);
            panel.Add(diagnostics);
            RefreshWorkspaceDiagnosticsPanel();
        }

        private void RefreshWorkspaceDiagnosticsPanel()
        {
            if (_workspaceDiagnosticsStatus == null ||
                Time.unscaledTime < _nextWorkspaceDiagnosticsRefresh)
                return;
            _nextWorkspaceDiagnosticsRefresh = Time.unscaledTime + 2f;
            var service = ResolveWorkspaceDiagnostics();
            if (service == null)
            {
                _workspaceDiagnosticsStatus.text = "Runtime diagnostics service not found.";
                return;
            }
            try
            {
                var snapshot = service.LatestSnapshot;
                if (snapshot.Sequence <= 0)
                {
                    _workspaceDiagnosticsStatus.text = "Runtime diagnostics: waiting for first report.";
                    return;
                }
                _workspaceDiagnosticsStatus.text = "Diagnostics #" + snapshot.Sequence +
                    " | Frame avg " + snapshot.FrameAverageMs.ToString("0.##") +
                    " ms, P95 " + snapshot.FrameP95Ms.ToString("0.##") +
                    " ms | Face " + snapshot.FaceHz.ToString("0.#") +
                    " Hz | Body " + snapshot.BodyHandsHz.ToString("0.#") + " Hz";
            }
            catch (Exception exception)
            {
                _workspaceDiagnosticsStatus.text = "Diagnostics read failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }
    }
}
