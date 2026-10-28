using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.EventRuntime;
using VCR.Runtime.EventRuntime.Unity;

namespace VCR.Runtime.UI
{
    // One configuration owner per active event host. Both the previous
    // dashboard and this shell use the same versioned/atomic rule store.
    public sealed partial class WorkspaceToolkitShell
    {
        private const float WorkspaceEventReloadConfirmSeconds = 15f;
        private const int WorkspaceEventVisibleRuleLimit = 80;
        private EventRuntimeHost _workspaceEventHost;
        private EventRuntimeConfigurationStore _workspaceEventStore;
        private bool _workspaceEventRestoreChecked;
        private ScrollView _workspaceEventRuleList;
        private Label _workspaceEventSummary;
        private Label _workspaceEventFeedback;
        private TextField _workspaceEventSelectedRule;
        private TextField _workspaceEventMaxCommands;
        private string _workspaceEventSelectedDraft;
        private string _workspaceEventMaxDraft;
        private string _workspaceEventSignature;
        private float _nextWorkspaceEventRefresh;
        private float _workspaceEventReloadConfirmUntil;

        private void ResetWorkspaceEventRules()
        {
            _workspaceEventHost = null;
            _workspaceEventStore = null;
            _workspaceEventRestoreChecked = false;
            _workspaceEventRuleList = null;
            _workspaceEventSummary = null;
            _workspaceEventFeedback = null;
            _workspaceEventSelectedRule = null;
            _workspaceEventMaxCommands = null;
            _workspaceEventSelectedDraft = null;
            _workspaceEventMaxDraft = null;
            _workspaceEventSignature = null;
            _nextWorkspaceEventRefresh = 0f;
            _workspaceEventReloadConfirmUntil = 0f;
        }

        private void CaptureWorkspaceEventRulesDraft()
        {
            if (_workspaceEventSelectedRule != null)
                _workspaceEventSelectedDraft = _workspaceEventSelectedRule.value;
            if (_workspaceEventMaxCommands != null)
                _workspaceEventMaxDraft = _workspaceEventMaxCommands.value;
        }

        private void BuildWorkspaceEventRulesPanel(VisualElement panel)
        {
            _workspaceEventSignature = null;
            _nextWorkspaceEventRefresh = 0f;
            var title = new Label("Event rule controls");
            title.style.marginTop = 8;
            panel.Add(title);
            _workspaceEventSummary = new Label("Event runtime: checking...");
            _workspaceEventSummary.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceEventSummary);
            _workspaceEventSelectedRule = new TextField("Rule ID");
            _workspaceEventSelectedRule.SetValueWithoutNotify(
                _workspaceEventSelectedDraft ?? "");
            panel.Add(_workspaceEventSelectedRule);
            _workspaceEventRuleList = new ScrollView();
            _workspaceEventRuleList.style.minHeight = 65;
            _workspaceEventRuleList.style.maxHeight = 130;
            panel.Add(_workspaceEventRuleList);

            _workspaceEventMaxCommands = new TextField("Max commands per event (1–256)");
            _workspaceEventMaxCommands.SetValueWithoutNotify(
                _workspaceEventMaxDraft ?? "");
            panel.Add(_workspaceEventMaxCommands);
            var operations = new VisualElement();
            operations.style.flexDirection = FlexDirection.Row;
            operations.style.flexWrap = Wrap.Wrap;
            operations.Add(new Button(ToggleWorkspaceSelectedEventRule)
                { text = "Toggle selected rule" });
            operations.Add(new Button(ToggleWorkspaceEventTracing)
                { text = "Toggle trace" });
            operations.Add(new Button(ApplyWorkspaceEventCommandLimit)
                { text = "Apply limit" });
            operations.Add(new Button(SaveWorkspaceEventRules)
                { text = "Save rules" });
            operations.Add(new Button(ReloadWorkspaceEventRules)
                { text = "Reload saved rules" });
            panel.Add(operations);

            _workspaceEventFeedback = new Label(
                "Rule changes affect the live runtime. Save explicitly to persist.");
            _workspaceEventFeedback.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceEventFeedback);
            RefreshWorkspaceEventRulesPanel();
        }

        private void ReportWorkspaceEvent(string message)
        {
            if (_workspaceEventFeedback != null)
                _workspaceEventFeedback.text = message;
        }

        private EventRuntimeHost ResolveWorkspaceEventHost()
        {
            var current = _workspaceEventHost;
            if (current == null)
            {
                current = FindFirstObjectByType<EventRuntimeHost>();
                if (!ReferenceEquals(current, _workspaceEventHost))
                {
                    _workspaceEventHost = current;
                    _workspaceEventStore = null;
                    _workspaceEventRestoreChecked = false;
                    _workspaceEventSignature = null;
                    _workspaceEventMaxDraft = null;
                }
            }
            if (current == null || !_workspaceEventRestoreChecked)
            {
                if (current == null) return null;
                _workspaceEventRestoreChecked = true;
                try
                {
                    var path = Path.Combine(UnityEngine.Application.persistentDataPath,
                        "VCR", "event-rules.json");
                    _workspaceEventStore = new EventRuntimeConfigurationStore(path);
                    // Match the legacy dashboard: restore only once on first
                    // discovery, never silently create/overwrite a saved file.
                    // If the legacy dashboard is still active it retains
                    // responsibility for its own startup restoration. Do
                    // not overwrite its live, potentially unsaved edits.
                    if (FindFirstObjectByType<ApplicationUiController>() == null &&
                        File.Exists(path) &&
                        !TryReloadWorkspaceEventRules(current, out var error))
                        ReportWorkspaceEvent("Saved event rules not restored: " + error);
                }
                catch (Exception exception)
                {
                    _workspaceEventStore = null;
                    ReportWorkspaceEvent("Event rule store unavailable: " + exception.Message);
                    Debug.LogException(exception, this);
                }
            }
            return current;
        }

        private void RefreshWorkspaceEventRulesPanel()
        {
            if (_workspaceEventSummary == null ||
                Time.unscaledTime < _nextWorkspaceEventRefresh)
                return;
            _nextWorkspaceEventRefresh = Time.unscaledTime + 2f;
            var host = ResolveWorkspaceEventHost();
            if (host == null)
            {
                _workspaceEventSummary.text = "Event rule runtime unavailable.";
                _workspaceEventRuleList?.Clear();
                _workspaceEventSignature = null;
                return;
            }
            try
            {
                _workspaceEventSummary.text = "Rules: " + host.RuleCount +
                    " | Tracing: " + (host.RuleTracingEnabled ? "on" : "off") +
                    " | Max commands: " + host.MaxCommandsPerEvent +
                    " | Events: " + host.ProcessedEvents +
                    " | Dropped commands: " + host.DroppedCommands +
                    (string.IsNullOrWhiteSpace(host.LastError) ? "" :
                        "\nError: " + host.LastError);
                if (_workspaceEventMaxCommands != null &&
                    string.IsNullOrWhiteSpace(_workspaceEventMaxCommands.value))
                    _workspaceEventMaxCommands.SetValueWithoutNotify(
                        host.MaxCommandsPerEvent.ToString(CultureInfo.InvariantCulture));
                var signature = new StringBuilder();
                signature.Append(host.GetInstanceID()).Append('|').Append(host.RuleCount);
                var shown = Math.Min(host.RuleCount, WorkspaceEventVisibleRuleLimit);
                for (var i = 0; i < shown; i++)
                    if (host.TryGetRuleSummaryAt(i, out var summary))
                        signature.Append('|').Append(summary.Id).Append(':').Append(summary.Enabled);
                var value = signature.ToString();
                if (value == _workspaceEventSignature) return;
                _workspaceEventSignature = value;
                _workspaceEventRuleList.Clear();
                if (host.RuleCount == 0)
                    _workspaceEventRuleList.Add(new Label("No event rules registered."));
                for (var i = 0; i < shown; i++)
                {
                    if (!host.TryGetRuleSummaryAt(i, out var summary)) continue;
                    var ruleId = summary.Id;
                    _workspaceEventRuleList.Add(new Button(() =>
                    {
                        if (_workspaceEventSelectedRule != null)
                            _workspaceEventSelectedRule.value = ruleId;
                    }) { text = (summary.Enabled ? "[On] " : "[Off] ") + ruleId });
                }
                if (host.RuleCount > shown)
                    _workspaceEventRuleList.Add(new Label("Showing first " + shown +
                        " of " + host.RuleCount + " rules. Enter an ID to edit the rest."));
            }
            catch (Exception exception)
            {
                ReportWorkspaceEvent("Rule discovery failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }

        private bool TryGetWorkspaceEventForEdit(out EventRuntimeHost host)
        {
            host = ResolveWorkspaceEventHost();
            if (host != null) return true;
            ReportWorkspaceEvent("Event runtime unavailable.");
            return false;
        }

        private void FinishWorkspaceEventAction(string message)
        {
            ReportWorkspaceEvent(message);
            _workspaceEventSignature = null;
            _nextWorkspaceEventRefresh = 0f;
            RefreshWorkspaceEventRulesPanel();
        }

        private void ToggleWorkspaceSelectedEventRule()
        {
            if (!TryGetWorkspaceEventForEdit(out var host)) return;
            var ruleId = _workspaceEventSelectedRule?.value?.Trim();
            if (!host.TryGetRuleSummary(ruleId, out var selected))
            {
                ReportWorkspaceEvent("Choose a valid event rule ID first.");
                return;
            }
            try
            {
                FinishWorkspaceEventAction(host.TrySetRuleEnabled(
                    selected.Id, !selected.Enabled, out var error)
                    ? "Rule '" + selected.Id + "' " +
                      (selected.Enabled ? "disabled." : "enabled.")
                    : "Rule update failed: " + (error ?? "unknown error"));
            }
            catch (Exception ex)
            {
                ReportWorkspaceEvent("Rule update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ToggleWorkspaceEventTracing()
        {
            if (!TryGetWorkspaceEventForEdit(out var host)) return;
            try
            {
                host.SetRuleTracingEnabled(!host.RuleTracingEnabled);
                FinishWorkspaceEventAction("Event tracing " +
                    (host.RuleTracingEnabled ? "enabled." : "disabled."));
            }
            catch (Exception ex)
            {
                ReportWorkspaceEvent("Trace update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ApplyWorkspaceEventCommandLimit()
        {
            if (!TryGetWorkspaceEventForEdit(out var host)) return;
            if (!int.TryParse(_workspaceEventMaxCommands?.value?.Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
                value < 1 || value > 256)
            {
                ReportWorkspaceEvent("Max commands must be an integer from 1 to 256.");
                return;
            }
            try
            {
                FinishWorkspaceEventAction(host.TrySetMaxCommandsPerEvent(value, out var error)
                    ? "Max commands per event: " + value
                    : "Limit update failed: " + (error ?? "unknown error"));
            }
            catch (Exception ex)
            {
                ReportWorkspaceEvent("Limit update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void SaveWorkspaceEventRules()
        {
            if (!TryGetWorkspaceEventForEdit(out var host)) return;
            if (_workspaceEventStore == null)
            {
                ReportWorkspaceEvent("Event rule store unavailable; no file was written.");
                return;
            }
            try
            {
                FinishWorkspaceEventAction(_workspaceEventStore.TrySave(
                    host.CaptureRules(), host.MaxCommandsPerEvent, out var error)
                    ? "Rules saved: " + _workspaceEventStore.Path
                    : "Save failed: " + (error ?? "unknown error"));
            }
            catch (Exception ex)
            {
                ReportWorkspaceEvent("Save failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ReloadWorkspaceEventRules()
        {
            if (!TryGetWorkspaceEventForEdit(out var host)) return;
            if (_workspaceEventStore == null || !File.Exists(_workspaceEventStore.Path))
            {
                ReportWorkspaceEvent("No saved event rule file to reload.");
                return;
            }
            if (_workspaceEventReloadConfirmUntil <= 0f ||
                Time.unscaledTime > _workspaceEventReloadConfirmUntil)
            {
                _workspaceEventReloadConfirmUntil = Time.unscaledTime +
                    WorkspaceEventReloadConfirmSeconds;
                ReportWorkspaceEvent("Reload discards unsaved event rule edits. " +
                    "Press Reload again within 15 seconds to confirm.");
                return;
            }
            _workspaceEventReloadConfirmUntil = 0f;
            try
            {
                FinishWorkspaceEventAction(TryReloadWorkspaceEventRules(host, out var error)
                    ? "Saved event rules reloaded."
                    : "Event rule reload failed: " + error);
            }
            catch (Exception ex)
            {
                ReportWorkspaceEvent("Event rule reload failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private bool TryReloadWorkspaceEventRules(EventRuntimeHost host, out string error)
        {
            error = null;
            if (_workspaceEventStore == null || !_workspaceEventStore.TryLoad(
                    out var rules, out var maxCommands, out error))
                return false;
            var before = host.CaptureRules();
            var beforeMax = host.MaxCommandsPerEvent;
            try
            {
                host.SetRules(rules);
                // SetRules reports validation errors internally, not through a
                // return value. Verify the resulting live summaries.
                if (!MatchesWorkspaceEventRules(host, rules))
                {
                    error = "The runtime rejected the saved rule set.";
                    host.SetRules(before);
                    return false;
                }
                if (!host.TrySetMaxCommandsPerEvent(maxCommands, out error))
                {
                    host.SetRules(before);
                    host.TrySetMaxCommandsPerEvent(beforeMax, out _);
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                try
                {
                    host.SetRules(before);
                    host.TrySetMaxCommandsPerEvent(beforeMax, out _);
                }
                catch (Exception rollbackError)
                {
                    error += " | rollback: " + rollbackError.Message;
                }
                return false;
            }
        }

        private static bool MatchesWorkspaceEventRules(
            EventRuntimeHost host, EventRuntimeRule[] rules)
        {
            if (host.RuleCount != (rules?.Length ?? 0)) return false;
            for (var i = 0; i < rules.Length; i++)
            {
                if (rules[i] == null)
                {
                    if (host.GetRuleAt(i) != null) return false;
                    continue;
                }
                // SetRules returns void and can reject a rule set without
                // throwing. Comparing only IDs/enabled would falsely mark a
                // change in filter/actions as successfully reloaded.
                var current = host.GetRuleAt(i);
                if (current == null ||
                    !string.Equals(JsonUtility.ToJson(current),
                        JsonUtility.ToJson(rules[i]), StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }
}
