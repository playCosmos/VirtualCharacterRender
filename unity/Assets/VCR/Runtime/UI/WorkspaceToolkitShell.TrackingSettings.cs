using System;
using System.Collections.Generic;
using UnityEngine;
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
                                Debug.LogWarning("U1 tracking settings restore failed for " +
                                                 id + ": " + applyError, this);
                        }
                    }

                    var enabled = PlayerPrefs.GetInt(TrackingEnabledKey + id, 0) != 0;
                    if (source.ControlEnabled != enabled &&
                        !source.TrySetControlEnabled(enabled, out var enableError))
                        Debug.LogWarning("U1 tracking enable restore failed for " +
                                         id + ": " + enableError, this);
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
                    if (control.ControlEnabled && !control.TrySetControlEnabled(false, out var error))
                    {
                        _workspaceConfiguredTracking.Remove(id);
                        _workspaceNativeSettings.SetText(92, "Add failed: " + error);
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
    }
}
