using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.UI
{
    // UI Toolkit owns this page; tracking providers own their own validation and restart policy.
    public sealed partial class WorkspaceToolkitShell
    {
        // Preserve keys written by the previous dashboard during UI migration.
        private const string TrackingConfiguredKey = "VCR.TrackingInputs.Configured.v1";
        private const string TrackingEnabledKey = "VCR.TrackingInputs.Enabled.v1.";
        private const string TrackingSettingKey = "VCR.TrackingInputs.Setting.v1.";
        private const int MaxNativeTrackingSources = 200;
        private const int MaxNativeTrackingFields = 256;

        private readonly HashSet<string> _workspaceConfiguredTracking =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _workspaceRestoredTracking =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _workspaceNativeTrackingIds =
            new Dictionary<int, string>();
        private readonly Dictionary<int, string> _workspaceNativeTrackingFields =
            new Dictionary<int, string>();
        private string _workspaceNativeActiveTrackingId;
        private float _nextTrackingRestoreScan;
        private float _nextTrackingSummaryScan;
        private string _cachedTrackingSummary = "Checking tracking sources...";

        private void InitializeWorkspaceTrackingRestore()
        {
            _workspaceConfiguredTracking.Clear();
            _workspaceRestoredTracking.Clear();
            _workspaceNativeTrackingIds.Clear();
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeActiveTrackingId = null;
            foreach (var id in PlayerPrefs.GetString(TrackingConfiguredKey, "")
                         .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(id))
                    _workspaceConfiguredTracking.Add(id.Trim());
            }
            _nextTrackingRestoreScan = 0f;
            _nextTrackingSummaryScan = 0f;
        }

        private static List<ITrackingRuntimeControl> DiscoverWorkspaceTrackingSources()
        {
            var found = new List<ITrackingRuntimeControl>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || !(behaviour is ITrackingRuntimeControl control) ||
                    string.IsNullOrWhiteSpace(control.ControlId) ||
                    !ids.Add(control.ControlId))
                    continue;
                found.Add(control);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.ControlId, b.ControlId));
            return found;
        }

        private static ITrackingRuntimeControl FindWorkspaceTrackingSource(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var source in DiscoverWorkspaceTrackingSources())
                if (string.Equals(source.ControlId, id, StringComparison.OrdinalIgnoreCase))
                    return source;
            return null;
        }

        private void RestoreWorkspaceTrackingPreferences()
        {
            if (Time.unscaledTime < _nextTrackingRestoreScan)
                return;
            _nextTrackingRestoreScan = Time.unscaledTime + 2f;

            foreach (var source in DiscoverWorkspaceTrackingSources())
            {
                var id = source.ControlId;
                if (!_workspaceConfiguredTracking.Contains(id) ||
                    !_workspaceRestoredTracking.Add(id))
                    continue;
                try
                {
                    if (source is ITrackingRuntimeConfigurable configurable)
                    {
                        var settings = configurable.GetConfigurationSettings();
                        if (settings != null)
                        {
                            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var hasPersisted = false;
                            foreach (var setting in settings)
                            {
                                if (string.IsNullOrWhiteSpace(setting.Key)) continue;
                                var key = TrackingSettingKey + id + "." + setting.Key;
                                values[setting.Key] = PlayerPrefs.GetString(key, setting.Value);
                                hasPersisted |= PlayerPrefs.HasKey(key);
                            }
                            if (hasPersisted &&
                                !configurable.TryApplyConfiguration(values, out var applyError))
                            {
                                Debug.LogWarning("U1 tracking settings restore failed for " +
                                                 id + ": " + applyError, this);
                                _workspaceRestoredTracking.Remove(id);
                                continue;
                            }
                        }
                    }

                    var enabled = PlayerPrefs.GetInt(TrackingEnabledKey + id, 0) != 0;
                    if (source.ControlEnabled != enabled &&
                        !source.TrySetControlEnabled(enabled, out var enableError))
                    {
                        Debug.LogWarning("U1 tracking enable restore failed for " +
                                         id + ": " + enableError, this);
                        _workspaceRestoredTracking.Remove(id);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("U1 tracking restore failed for " +
                                     id + ": " + exception, this);
                    // Retry if a provider was not yet initialized.
                    _workspaceRestoredTracking.Remove(id);
                }
            }
        }

        private string GetWorkspaceTrackingSummary()
        {
            if (Time.unscaledTime < _nextTrackingSummaryScan)
                return _cachedTrackingSummary;
            _nextTrackingSummaryScan = Time.unscaledTime + 2f;
            var sources = DiscoverWorkspaceTrackingSources();
            if (sources.Count == 0)
                return _cachedTrackingSummary = "Tracking: no configured source found";

            var enabled = 0;
            var unhealthy = 0;
            foreach (var control in sources)
            {
                if (!control.ControlEnabled) continue;
                enabled++;
                if (!string.IsNullOrWhiteSpace(control.ControlError))
                    unhealthy++;
            }
            return _cachedTrackingSummary = "Tracking sources: " + sources.Count +
                " | Enabled: " + enabled + " | Errors: " + unhealthy;
        }

        private void PersistConfiguredWorkspaceTracking()
        {
            var ids = new List<string>(_workspaceConfiguredTracking);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            PlayerPrefs.SetString(TrackingConfiguredKey, string.Join("\n", ids));
            PlayerPrefs.Save();
        }

        private void RebuildWorkspaceNativeTrackingSources()
        {
            if (_workspaceNativeSettings == null) return;
            _workspaceNativeSettings.ClearCategory(2);
            _workspaceNativeTrackingIds.Clear();
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeActiveTrackingId = null;
            _workspaceNativeSettings.Add(2, 4, DesktopNativeSettingsWindow.ControlKind.Label,
                "Tracking input sources");
            _workspaceNativeSettings.Add(2, 450, DesktopNativeSettingsWindow.ControlKind.Button,
                "Refresh device list");
            var sources = DiscoverWorkspaceTrackingSources();
            var count = Math.Min(sources.Count, MaxNativeTrackingSources);
            for (var i = 0; i < count; i++)
            {
                var source = sources[i];
                _workspaceNativeTrackingIds[i] = source.ControlId;
                var title = source.DisplayName + " (" +
                    (source.ControlEnabled ? source.ControlHealthState.ToString() : "Off") + ")";
                if (source is ITrackingRuntimeConfigurable)
                    _workspaceNativeSettings.Add(2, 5000 + i,
                        DesktopNativeSettingsWindow.ControlKind.Button, title + " settings");
                _workspaceNativeSettings.Add(2, 5200 + i,
                    DesktopNativeSettingsWindow.ControlKind.Button,
                    (source.ControlEnabled ? "Disable " : "Enable ") + source.DisplayName);
                _workspaceNativeSettings.Add(2, 5400 + i,
                    DesktopNativeSettingsWindow.ControlKind.Button,
                    "Recover " + source.DisplayName);
                _workspaceNativeSettings.Add(2, 5600 + i,
                    DesktopNativeSettingsWindow.ControlKind.Button,
                    (_workspaceConfiguredTracking.Contains(source.ControlId) ?
                        "Remove " : "Add ") + source.DisplayName);
            }
            if (count == 0)
                _workspaceNativeSettings.Add(2, 5,
                    DesktopNativeSettingsWindow.ControlKind.Label, "No tracking sources detected");
            if (sources.Count > count)
                Debug.LogWarning("U1 tracking settings shows first " + count +
                                 " devices; source count exceeds UI limit.", this);
            _workspaceNativeSettings.Add(2, 92, DesktopNativeSettingsWindow.ControlKind.Label,
                "Select a source to configure it.");
        }

        private void ShowWorkspaceNativeTrackingDetails(string controlId)
        {
            var control = FindWorkspaceTrackingSource(controlId);
            if (!(control is ITrackingRuntimeConfigurable configurable))
            {
                _workspaceNativeSettings.SetText(92, "Tracking source not configurable.");
                return;
            }
            IReadOnlyList<TrackingRuntimeSetting> settings;
            try { settings = configurable.GetConfigurationSettings(); }
            catch (Exception ex)
            {
                _workspaceNativeSettings.SetText(92, "Unable to read settings: " + ex.Message);
                return;
            }
            if (settings != null && settings.Count > MaxNativeTrackingFields)
            {
                _workspaceNativeSettings.SetText(92, "Too many device settings.");
                return;
            }

            _workspaceNativeActiveTrackingId = controlId;
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeSettings.ClearCategory(2);
            _workspaceNativeSettings.Add(2, 6, DesktopNativeSettingsWindow.ControlKind.Label,
                configurable.ConfigurationTitle);
            if (settings != null)
            {
                for (var i = 0; i < settings.Count; i++)
                {
                    var setting = settings[i];
                    if (string.IsNullOrWhiteSpace(setting.Key) ||
                        _workspaceNativeTrackingFields.ContainsValue(setting.Key))
                        continue;
                    var key = TrackingSettingKey + controlId + "." + setting.Key;
                    var persisted = PlayerPrefs.GetString(key, setting.Value);
                    _workspaceNativeSettings.Add(2, 6000 + i,
                        DesktopNativeSettingsWindow.ControlKind.Edit,
                        setting.Label, persisted);
                    _workspaceNativeTrackingFields[6000 + i] = setting.Key;
                }
            }
            _workspaceNativeSettings.Add(2, 6900, DesktopNativeSettingsWindow.ControlKind.Button,
                "Save and apply");
            _workspaceNativeSettings.Add(2, 6901, DesktopNativeSettingsWindow.ControlKind.Button,
                "Back to devices");
            _workspaceNativeSettings.Add(2, 92, DesktopNativeSettingsWindow.ControlKind.Label,
                "Values are validated by the selected tracking provider.");
            _workspaceNativeSettings.SelectCategory(2);
        }

        private void SaveWorkspaceNativeTrackingDetails()
        {
            var control = FindWorkspaceTrackingSource(_workspaceNativeActiveTrackingId);
            if (!(control is ITrackingRuntimeConfigurable configurable))
            {
                _workspaceNativeSettings.SetText(92, "Tracking source no longer available.");
                return;
            }
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _workspaceNativeTrackingFields)
                values[entry.Value] = _workspaceNativeSettings.GetText(entry.Key);
            try
            {
                if (!configurable.TryApplyConfiguration(values, out var error))
                {
                    _workspaceNativeSettings.SetText(92,
                        "Apply failed: " + (error ?? "unknown error"));
                    return;
                }
                foreach (var pair in values)
                    PlayerPrefs.SetString(TrackingSettingKey + control.ControlId + "." + pair.Key,
                                          pair.Value ?? "");
                _workspaceConfiguredTracking.Add(control.ControlId);
                _workspaceRestoredTracking.Add(control.ControlId);
                PersistConfiguredWorkspaceTracking();
                RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92, "Tracking settings applied.");
            }
            catch (Exception ex)
            {
                _workspaceNativeSettings.SetText(92, "Apply failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private bool HandleWorkspaceNativeTrackingAction(int action)
        {
            if (action == 450)
            {
                RebuildWorkspaceNativeTrackingSources();
                return true;
            }
            if (action == 6901)
            {
                RebuildWorkspaceNativeTrackingSources();
                return true;
            }
            if (action == 6900)
            {
                SaveWorkspaceNativeTrackingDetails();
                return true;
            }
            if (action < 5000 || action >= 5800)
                return false;

            var slot = (action - 5000) % 200;
            if (!_workspaceNativeTrackingIds.TryGetValue(slot, out var id))
                return true;
            var control = FindWorkspaceTrackingSource(id);
            if (control == null)
            {
                _workspaceNativeSettings.SetText(92, "Tracking source is unavailable.");
                return true;
            }
            if (action < 5200)
                ShowWorkspaceNativeTrackingDetails(id);
            else if (action < 5400)
            {
                var enabled = !control.ControlEnabled;
                if (control.TrySetControlEnabled(enabled, out var error))
                {
                    _workspaceConfiguredTracking.Add(id);
                    _workspaceRestoredTracking.Add(id);
                    PlayerPrefs.SetInt(TrackingEnabledKey + id, enabled ? 1 : 0);
                    PersistConfiguredWorkspaceTracking();
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SetText(92, "Tracking source " +
                        (enabled ? "enabled." : "disabled."));
                }
                else
                    _workspaceNativeSettings.SetText(92, "Toggle failed: " + error);
            }
            else if (action < 5600)
            {
                if (control.TryRecover(out var error))
                    _workspaceNativeSettings.SetText(92, "Recovery requested.");
                else
                    _workspaceNativeSettings.SetText(92, "Recovery failed: " + error);
            }
            else
            {
                var remove = _workspaceConfiguredTracking.Contains(id);
                if (remove && control.ControlEnabled &&
                    !control.TrySetControlEnabled(false, out var error))
                {
                    _workspaceNativeSettings.SetText(92, "Remove failed: " + error);
                    return true;
                }
                if (remove)
                {
                    _workspaceConfiguredTracking.Remove(id);
                    _workspaceRestoredTracking.Remove(id);
                    PlayerPrefs.SetInt(TrackingEnabledKey + id, 0);
                }
                else
                {
                    _workspaceConfiguredTracking.Add(id);
                    _workspaceRestoredTracking.Add(id);
                    // Newly added inputs start disabled, matching the old dashboard.
                    if (control.ControlEnabled && !control.TrySetControlEnabled(false, out var addError))
                    {
                        _workspaceConfiguredTracking.Remove(id);
                        _workspaceNativeSettings.SetText(92, "Add failed: " + addError);
                        return true;
                    }
                    PlayerPrefs.SetInt(TrackingEnabledKey + id, 0);
                }
                PersistConfiguredWorkspaceTracking();
                RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92,
                    remove ? "Tracking source removed." : "Tracking source added.");
                if (!remove && control is ITrackingRuntimeConfigurable config &&
                    config.OpenConfigurationOnAdd)
                    ShowWorkspaceNativeTrackingDetails(id);
            }
            return true;
        }

        private ScrollView _workspaceTrackingList;
        private VisualElement _workspaceTrackingEditor;
        private Label _workspaceTrackingFeedback;
        private float _nextWorkspaceTrackingPanelRefresh;
        private string _workspaceTrackingPanelSignature;

        private void ResetWorkspaceTrackingPanel()
        {
            _workspaceTrackingList = null;
            _workspaceTrackingEditor = null;
            _workspaceTrackingFeedback = null;
            _workspaceTrackingPanelSignature = null;
            _nextWorkspaceTrackingPanelRefresh = 0f;
        }

        private void BuildWorkspaceTrackingPanel(VisualElement panel)
        {
            // Build() replaces the visual tree while the runtime remains the
            // same. Invalidate the old tree's cache so the new list is populated.
            _workspaceTrackingPanelSignature = null;
            _nextWorkspaceTrackingPanelRefresh = 0f;
            _workspaceTrackingFeedback = new Label(
                "Select an input source to manage it. Configuration is also available in Settings.");
            panel.Add(_workspaceTrackingFeedback);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.Add(new Button(() =>
            {
                _workspaceTrackingPanelSignature = null;
                _nextWorkspaceTrackingPanelRefresh = 0f;
                RefreshWorkspaceTrackingPanel();
            }) { text = "Refresh inputs" });
            if (DesktopNativeSettingsWindow.Supported)
            {
                actions.Add(new Button(() =>
                {
                    OpenWorkspaceNativeSettings();
                    if (_workspaceNativeSettings != null)
                    {
                        RebuildWorkspaceNativeTrackingSources();
                        _workspaceNativeSettings.SelectCategory(2);
                    }
                }) { text = "Device Settings" });
            }
            panel.Add(actions);

            _workspaceTrackingList = new ScrollView();
            _workspaceTrackingList.name = "vcr-tracking-input-list";
            _workspaceTrackingList.style.flexGrow = 1;
            _workspaceTrackingList.style.minHeight = 60;
            panel.Add(_workspaceTrackingList);

            _workspaceTrackingEditor = new VisualElement();
            _workspaceTrackingEditor.name = "vcr-tracking-input-editor";
            panel.Add(_workspaceTrackingEditor);
            RefreshWorkspaceTrackingPanel();
        }

        private void RefreshWorkspaceTrackingPanel()
        {
            if (_workspaceTrackingList == null ||
                Time.unscaledTime < _nextWorkspaceTrackingPanelRefresh)
                return;
            _nextWorkspaceTrackingPanelRefresh = Time.unscaledTime + 2f;

            try
            {
                var sources = DiscoverWorkspaceTrackingSources();
                var signature = "";
                foreach (var source in sources)
                {
                    signature += source.ControlId + "\u001f" + source.DisplayName + "\u001f" +
                        source.ControlEnabled + "\u001f" + source.ControlHealthState +
                        "\u001f" + source.ControlError + "\u001f" +
                        _workspaceConfiguredTracking.Contains(source.ControlId) + "\u001e";
                }
                if (signature == _workspaceTrackingPanelSignature)
                    return;
                _workspaceTrackingPanelSignature = signature;
                _workspaceTrackingList.Clear();

                if (sources.Count == 0)
                {
                    _workspaceTrackingList.Add(
                        new Label("No tracking devices detected. Connect a source and refresh."));
                    return;
                }

                foreach (var source in sources)
                {
                    var sourceId = source.ControlId;
                    var configured = _workspaceConfiguredTracking.Contains(sourceId);
                    var container = new VisualElement();
                    container.style.marginBottom = 5;
                    container.style.paddingBottom = 5;
                    container.style.borderBottomWidth = 1;
                    container.style.borderBottomColor = new Color(0.24f, 0.28f, 0.32f);

                    var name = new Label((source.DisplayName ?? sourceId) + " — " +
                        (source.ControlEnabled ? source.ControlHealthState.ToString() : "Off") +
                        (configured ? "" : " (not added)"));
                    container.Add(name);

                    if (!string.IsNullOrWhiteSpace(source.ControlError))
                    {
                        var error = new Label(source.ControlError);
                        error.style.color = new Color(1f, 0.55f, 0.45f);
                        container.Add(error);
                    }

                    var buttons = new VisualElement();
                    buttons.style.flexDirection = FlexDirection.Row;
                    buttons.style.flexWrap = Wrap.Wrap;
                    buttons.Add(new Button(() => UpdateWorkspaceTrackingMembership(sourceId))
                        { text = configured ? "Remove" : "Add" });
                    if (configured)
                    {
                        buttons.Add(new Button(() => ToggleWorkspaceTrackingEnabled(sourceId))
                            { text = source.ControlEnabled ? "Disable" : "Enable" });
                        buttons.Add(new Button(() => RecoverWorkspaceTrackingSource(sourceId))
                            { text = "Recover" });
                        if (source is ITrackingRuntimeConfigurable)
                            buttons.Add(new Button(() => EditWorkspaceTrackingSettings(sourceId))
                                { text = "Configure" });
                    }
                    container.Add(buttons);
                    _workspaceTrackingList.Add(container);
                }
            }
            catch (Exception ex)
            {
                SetWorkspaceTrackingFeedback("Unable to list tracking devices: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void SetWorkspaceTrackingFeedback(string message)
        {
            if (_workspaceTrackingFeedback != null)
                _workspaceTrackingFeedback.text = message;
        }

        private void TrackingPanelActionFinished(string message)
        {
            SetWorkspaceTrackingFeedback(message);
            _workspaceTrackingPanelSignature = null;
            _nextWorkspaceTrackingPanelRefresh = 0f;
            RefreshWorkspaceTrackingPanel();
        }

        private void UpdateWorkspaceTrackingMembership(string sourceId)
        {
            try
            {
                var source = FindWorkspaceTrackingSource(sourceId);
                if (source == null)
                {
                    SetWorkspaceTrackingFeedback("Tracking source disconnected.");
                    return;
                }
                var configured = _workspaceConfiguredTracking.Contains(sourceId);
                // Keep the former dashboard behavior: newly added devices start disabled.
                if (source.ControlEnabled &&
                    !source.TrySetControlEnabled(false, out var error))
                {
                    SetWorkspaceTrackingFeedback("Cannot " + (configured ? "remove" : "add") +
                        " source: " + error);
                    return;
                }

                if (configured)
                {
                    _workspaceConfiguredTracking.Remove(sourceId);
                    _workspaceRestoredTracking.Remove(sourceId);
                    _workspaceTrackingEditor?.Clear();
                }
                else
                {
                    _workspaceConfiguredTracking.Add(sourceId);
                    _workspaceRestoredTracking.Add(sourceId);
                }
                PlayerPrefs.SetInt(TrackingEnabledKey + sourceId, 0);
                PersistConfiguredWorkspaceTracking();
                TrackingPanelActionFinished((source.DisplayName ?? sourceId) +
                    (configured ? " removed." : " added (disabled)."));

                if (!configured && source is ITrackingRuntimeConfigurable settings &&
                    settings.OpenConfigurationOnAdd)
                    EditWorkspaceTrackingSettings(sourceId);
            }
            catch (Exception ex)
            {
                SetWorkspaceTrackingFeedback("Tracking source update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ToggleWorkspaceTrackingEnabled(string sourceId)
        {
            try
            {
                var source = FindWorkspaceTrackingSource(sourceId);
                if (source == null || !_workspaceConfiguredTracking.Contains(sourceId))
                {
                    SetWorkspaceTrackingFeedback("Add a connected source before enabling it.");
                    return;
                }
                var enabled = !source.ControlEnabled;
                if (!source.TrySetControlEnabled(enabled, out var error))
                {
                    SetWorkspaceTrackingFeedback("Tracking toggle failed: " + error);
                    return;
                }
                _workspaceRestoredTracking.Add(sourceId);
                PlayerPrefs.SetInt(TrackingEnabledKey + sourceId, enabled ? 1 : 0);
                PlayerPrefs.Save();
                TrackingPanelActionFinished((source.DisplayName ?? sourceId) +
                    (enabled ? " enabled." : " disabled."));
            }
            catch (Exception ex)
            {
                SetWorkspaceTrackingFeedback("Tracking toggle failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void RecoverWorkspaceTrackingSource(string sourceId)
        {
            try
            {
                var source = FindWorkspaceTrackingSource(sourceId);
                if (source == null)
                {
                    SetWorkspaceTrackingFeedback("Tracking source disconnected.");
                    return;
                }
                if (source.TryRecover(out var error))
                    TrackingPanelActionFinished("Recovery requested for " +
                        (source.DisplayName ?? sourceId) + ".");
                else
                    SetWorkspaceTrackingFeedback("Recovery failed: " + error);
            }
            catch (Exception ex)
            {
                SetWorkspaceTrackingFeedback("Recovery failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void EditWorkspaceTrackingSettings(string sourceId)
        {
            if (_workspaceTrackingEditor == null) return;
            _workspaceTrackingEditor.Clear();
            try
            {
                var source = FindWorkspaceTrackingSource(sourceId);
                if (!(source is ITrackingRuntimeConfigurable configurable))
                {
                    SetWorkspaceTrackingFeedback("This source has no editable settings.");
                    return;
                }
                var settings = configurable.GetConfigurationSettings();
                if (settings == null || settings.Count > MaxNativeTrackingFields)
                {
                    SetWorkspaceTrackingFeedback("Tracking configuration is unavailable.");
                    return;
                }
                _workspaceTrackingEditor.Add(new Label(configurable.ConfigurationTitle));
                var fields = new Dictionary<string, TextField>(StringComparer.OrdinalIgnoreCase);
                foreach (var setting in settings)
                {
                    if (string.IsNullOrWhiteSpace(setting.Key) ||
                        fields.ContainsKey(setting.Key))
                        continue;
                    var key = TrackingSettingKey + sourceId + "." + setting.Key;
                    var field = new TextField(setting.Label)
                    {
                        value = PlayerPrefs.GetString(key, setting.Value),
                        tooltip = setting.HelpText
                    };
                    _workspaceTrackingEditor.Add(field);
                    fields[setting.Key] = field;
                }
                _workspaceTrackingEditor.Add(new Button(() =>
                {
                    try
                    {
                        var live = FindWorkspaceTrackingSource(sourceId);
                        if (!(live is ITrackingRuntimeConfigurable target))
                        {
                            SetWorkspaceTrackingFeedback("Tracking source disconnected.");
                            return;
                        }
                        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var field in fields)
                            values[field.Key] = field.Value.value ?? "";
                        if (!target.TryApplyConfiguration(values, out var error))
                        {
                            SetWorkspaceTrackingFeedback("Apply failed: " + error);
                            return;
                        }
                        foreach (var pair in values)
                            PlayerPrefs.SetString(TrackingSettingKey + sourceId + "." + pair.Key,
                                pair.Value);
                        _workspaceConfiguredTracking.Add(sourceId);
                        _workspaceRestoredTracking.Add(sourceId);
                        PersistConfiguredWorkspaceTracking();
                        _workspaceTrackingEditor.Clear();
                        TrackingPanelActionFinished("Tracking settings saved and applied.");
                    }
                    catch (Exception ex)
                    {
                        SetWorkspaceTrackingFeedback("Apply failed: " + ex.Message);
                        Debug.LogException(ex, this);
                    }
                }) { text = "Save and apply" });
                _workspaceTrackingEditor.Add(new Button(() =>
                    _workspaceTrackingEditor.Clear()) { text = "Cancel" });
                SetWorkspaceTrackingFeedback("Editing " + (source.DisplayName ?? sourceId));
            }
            catch (Exception ex)
            {
                _workspaceTrackingEditor.Clear();
                SetWorkspaceTrackingFeedback("Unable to open tracking settings: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }
    }
}
