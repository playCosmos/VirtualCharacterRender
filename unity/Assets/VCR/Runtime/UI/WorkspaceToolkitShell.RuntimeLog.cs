using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

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

        private readonly struct WorkspaceLogEvent
        {
            public WorkspaceLogEvent(string message, LogType type)
            {
                Message = message ?? "";
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
                    if (IsWorkspaceLogVisible(log))
                        builder.Append('[').Append(log.Timestamp).Append("] [")
                            .Append(log.Type).Append("] ").AppendLine(log.Message);
                GUIUtility.systemCopyBuffer = builder.ToString();
            }) { text = "Copy visible" });
            panel.Add(controls);
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
    }
}
