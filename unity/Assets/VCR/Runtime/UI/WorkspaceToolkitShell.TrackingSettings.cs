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
        // A reconnected provider may reuse the same ControlId with a new runtime
        // component. Remember the exact provider instance, not only its ID.
        private readonly Dictionary<string, ITrackingRuntimeControl> _workspaceRestoredTracking =
            new Dictionary<string, ITrackingRuntimeControl>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, string> _workspaceNativeTrackingIds =
            new Dictionary<int, string>();
        // A native button must only act on the provider instance it displayed.
        private readonly Dictionary<int, ITrackingRuntimeControl> _workspaceNativeTrackingProviders =
            new Dictionary<int, ITrackingRuntimeControl>();
        // The Add/Remove button must retain the membership state it displayed.
        private readonly Dictionary<int, bool> _workspaceNativeTrackingConfigured =
            new Dictionary<int, bool>();
        // Keep the state and revision that produced each native Enable/Disable label.
        private readonly Dictionary<int, bool> _workspaceNativeTrackingEnabled =
            new Dictionary<int, bool>();
        private readonly Dictionary<int, int> _workspaceNativeTrackingVersions =
            new Dictionary<int, int>();
        private readonly Dictionary<int, string> _workspaceNativeTrackingFields =
            new Dictionary<int, string>();
        // Saves and Add/Remove across both UI surfaces invalidate old drafts.
        private readonly Dictionary<string, int> _workspaceTrackingEditVersions =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _workspaceNativeTrackingEditVersion;
        private Dictionary<string, string> _workspaceNativeTrackingOpenValues;
        private string _workspaceNativeActiveTrackingId;
        // The Settings editor belongs to the specific provider that created
        // its fields, not merely to a reusable logical ControlId.
        private ITrackingRuntimeControl _workspaceNativeActiveTrackingProvider;
        private float _nextTrackingRestoreScan;
        private float _nextTrackingSummaryScan;
        private string _cachedTrackingSummary = "Checking tracking sources...";

        private void InitializeWorkspaceTrackingRestore()
        {
            _workspaceConfiguredTracking.Clear();
            _workspaceRestoredTracking.Clear();
            _workspaceTrackingEditVersions.Clear();
            _workspaceNativeTrackingEditVersion = 0;
            _workspaceNativeTrackingOpenValues = null;
            _workspaceNativeTrackingIds.Clear();
            _workspaceNativeTrackingProviders.Clear();
            _workspaceNativeTrackingConfigured.Clear();
            _workspaceNativeTrackingEnabled.Clear();
            _workspaceNativeTrackingVersions.Clear();
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeActiveTrackingId = null;
            _workspaceNativeActiveTrackingProvider = null;
            foreach (var id in PlayerPrefs.GetString(TrackingConfiguredKey, "")
                         .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(id))
                    _workspaceConfiguredTracking.Add(id.Trim());
            }
            _nextTrackingRestoreScan = 0f;
            _nextTrackingSummaryScan = 0f;
        }

        private string GetWorkspaceTrackingPersistenceId(string runtimeId)
        {
            // Registered IDs are compared without case, but PlayerPrefs
            // keys must preserve the original registered spelling.
            foreach (var configuredId in _workspaceConfiguredTracking)
                if (string.Equals(configuredId, runtimeId,
                        StringComparison.OrdinalIgnoreCase))
                    return configuredId;
            return runtimeId;
        }

        private int GetWorkspaceTrackingEditVersion(string sourceId)
        {
            return !string.IsNullOrWhiteSpace(sourceId) &&
                   _workspaceTrackingEditVersions.TryGetValue(sourceId, out var version)
                ? version : 0;
        }

        private void InvalidateWorkspaceTrackingEditors(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return;
            var version = GetWorkspaceTrackingEditVersion(sourceId);
            _workspaceTrackingEditVersions[sourceId] =
                version == int.MaxValue ? 1 : version + 1;
        }

        private bool IsWorkspaceTrackingEditVersionCurrent(
            string sourceId, int openedVersion)
        {
            return _workspaceConfiguredTracking.Contains(sourceId) &&
                   GetWorkspaceTrackingEditVersion(sourceId) == openedVersion;
        }

        private static List<ITrackingRuntimeControl> DiscoverWorkspaceTrackingSources()
        {
            return DiscoverWorkspaceTrackingSources(out _);
        }

        private static List<ITrackingRuntimeControl> DiscoverWorkspaceTrackingSources(
            out int ambiguousIdCount)
        {
            // A duplicate provider ID is not a valid choice. Previous code
            // silently selected whichever component Unity enumerated first,
            // potentially applying device settings to an unrelated source.
            var unique = new Dictionary<string, ITrackingRuntimeControl>(
                StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                // Disabled MonoBehaviours can still represent configurable
                // inputs, but components on inactive GameObjects are not live.
                if (behaviour == null || !behaviour.gameObject.activeInHierarchy ||
                    !(behaviour is ITrackingRuntimeControl control))
                    continue;
                var id = control.ControlId;
                if (string.IsNullOrWhiteSpace(id) || ambiguous.Contains(id))
                    continue;
                if (unique.ContainsKey(id))
                {
                    unique.Remove(id);
                    ambiguous.Add(id);
                    continue;
                }
                unique.Add(id, control);
            }
            ambiguousIdCount = ambiguous.Count;
            var found = new List<ITrackingRuntimeControl>(unique.Values);
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

            var sources = DiscoverWorkspaceTrackingSources();
            // Dropped/ambiguous provider IDs must not keep destroyed Unity
            // components alive via the restored-instance cache. Keep saved
            // preferences so the next valid provider can still reconnect.
            var presentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sources)
                presentIds.Add(source.ControlId);
            foreach (var previousId in new List<string>(_workspaceRestoredTracking.Keys))
                if (!presentIds.Contains(previousId) ||
                    !_workspaceConfiguredTracking.Contains(previousId))
                    _workspaceRestoredTracking.Remove(previousId);

            foreach (var source in sources)
            {
                var id = source.ControlId;
                if (!_workspaceConfiguredTracking.Contains(id) ||
                    (_workspaceRestoredTracking.TryGetValue(id, out var previous) &&
                     ReferenceEquals(previous, source)))
                    continue;
                // A new component with the same ID must receive the saved
                // settings and enabled state again. Mark before applying so a
                // synchronous callback cannot trigger duplicate restore work.
                _workspaceRestoredTracking[id] = source;
                try
                {
                    var persistenceId = GetWorkspaceTrackingPersistenceId(id);
                    var enabled = PlayerPrefs.GetInt(
                        TrackingEnabledKey + persistenceId, 0) != 0;
                    // If the saved preference is Off, stop capture before
                    // applying settings. MediaPipe may restart its webcam when
                    // capture parameters change, even if we will disable it.
                    if (!enabled && source.ControlEnabled &&
                        !source.TrySetControlEnabled(false, out var stopError))
                    {
                        Debug.LogWarning("U1 tracking disable restore failed for " +
                                         id + ": " + stopError, this);
                        _workspaceRestoredTracking.Remove(id);
                        continue;
                    }

                    if (source is ITrackingRuntimeConfigurable configurable)
                    {
                        var settings = configurable.GetConfigurationSettings();
                        // A configurable provider may return no schema while
                        // initializing. Treat null as not ready rather than
                        // permanently skipping the saved configuration.
                        if (settings == null)
                        {
                            _workspaceRestoredTracking.Remove(id);
                            continue;
                        }
                        if (settings.Count > 0)
                        {
                            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            var hasPersisted = false;
                            foreach (var setting in settings)
                            {
                                if (string.IsNullOrWhiteSpace(setting.Key)) continue;
                                var key = TrackingSettingKey + persistenceId + "." + setting.Key;
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

                    // Reconcile again after configuration: a provider may
                    // update its enabled state while applying settings.
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
            var sources = DiscoverWorkspaceTrackingSources(out var ambiguous);
            if (sources.Count == 0)
                return _cachedTrackingSummary = ambiguous > 0
                    ? "Tracking: " + ambiguous +
                      " duplicate source ID(s) blocked; no unambiguous sources."
                    : "Tracking: no configured source found";

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
                " | Enabled: " + enabled + " | Errors: " + unhealthy +
                (ambiguous > 0 ? " | Duplicate IDs blocked: " + ambiguous : "");
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
            _workspaceNativeTrackingProviders.Clear();
            _workspaceNativeTrackingConfigured.Clear();
            _workspaceNativeTrackingEnabled.Clear();
            _workspaceNativeTrackingVersions.Clear();
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeActiveTrackingId = null;
            _workspaceNativeActiveTrackingProvider = null;
            _workspaceNativeTrackingEditVersion = 0;
            _workspaceNativeTrackingOpenValues = null;
            _workspaceNativeSettings.Add(2, 4, DesktopNativeSettingsWindow.ControlKind.Label,
                "Tracking input sources");
            _workspaceNativeSettings.Add(2, 450, DesktopNativeSettingsWindow.ControlKind.Button,
                "Refresh device list");
            var sources = DiscoverWorkspaceTrackingSources(out var ambiguous);
            if (ambiguous > 0)
                _workspaceNativeSettings.Add(2, 6,
                    DesktopNativeSettingsWindow.ControlKind.Label,
                    "Duplicate tracking source IDs blocked: " + ambiguous);
            var count = Math.Min(sources.Count, MaxNativeTrackingSources);
            for (var i = 0; i < count; i++)
            {
                var source = sources[i];
                _workspaceNativeTrackingIds[i] = source.ControlId;
                _workspaceNativeTrackingProviders[i] = source;
                var title = source.DisplayName + " (" +
                    (source.ControlEnabled ? source.ControlHealthState.ToString() : "Off") + ")";
                var configured = _workspaceConfiguredTracking.Contains(source.ControlId);
                _workspaceNativeTrackingConfigured[i] = configured;
                _workspaceNativeTrackingEnabled[i] = source.ControlEnabled;
                _workspaceNativeTrackingVersions[i] =
                    GetWorkspaceTrackingEditVersion(source.ControlId);
                if (configured && source is ITrackingRuntimeConfigurable)
                    _workspaceNativeSettings.Add(2, 5000 + i,
                        DesktopNativeSettingsWindow.ControlKind.Button, title + " settings");
                // Unconfigured sources must be added (initially disabled)
                // before Toggle or Recover, matching the UI Toolkit panel.
                if (configured)
                {
                    _workspaceNativeSettings.Add(2, 5200 + i,
                        DesktopNativeSettingsWindow.ControlKind.Button,
                        (source.ControlEnabled ? "Disable " : "Enable ") + source.DisplayName);
                    // Recovery must not be offered for a disabled or saved-Off
                    // source: the provider explicitly rejects those requests.
                    if (IsWorkspaceTrackingRecoveryActionAvailable(source.ControlId, source))
                        _workspaceNativeSettings.Add(2, 5400 + i,
                            DesktopNativeSettingsWindow.ControlKind.Button,
                            "Recover " + source.DisplayName);
                }
                _workspaceNativeSettings.Add(2, 5600 + i,
                    DesktopNativeSettingsWindow.ControlKind.Button,
                    (configured ? "Remove " : "Add ") + source.DisplayName);
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

        private static bool IsWorkspaceTrackingEditorSourceCurrent(
            ITrackingRuntimeControl editorSource, ITrackingRuntimeControl liveSource)
        {
            return editorSource != null && ReferenceEquals(editorSource, liveSource);
        }

        private static string GetWorkspaceTrackingDisplayedValue(TrackingRuntimeSetting setting)
        {
            // The editor must show the value actually reported by the live
            // device, not a possibly stale saved preference. PlayerPrefs is
            // still the persistence source for startup recovery.
            return setting.Value ?? string.Empty;
        }

        private static bool IsWorkspaceTrackingEditableSchema(
            IReadOnlyList<TrackingRuntimeSetting> settings)
        {
            // A null schema means the provider is still initializing.
            // An empty non-null schema is valid (no editable fields).
            return settings != null && settings.Count <= MaxNativeTrackingFields;
        }

        private static Dictionary<string, string> CaptureWorkspaceTrackingSettings(
            IReadOnlyList<TrackingRuntimeSetting> settings)
        {
            if (!IsWorkspaceTrackingEditableSchema(settings))
                return null;
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var setting in settings)
            {
                if (string.IsNullOrWhiteSpace(setting.Key) ||
                    snapshot.ContainsKey(setting.Key))
                    continue;
                snapshot[setting.Key] = GetWorkspaceTrackingDisplayedValue(setting);
            }
            return snapshot;
        }

        private static bool AreWorkspaceTrackingSettingsUnchanged(
            ITrackingRuntimeConfigurable configurable,
            IReadOnlyDictionary<string, string> openedValues)
        {
            if (configurable == null || openedValues == null)
                return false;
            // Re-read provider-owned settings instead of trusting PlayerPrefs
            // or the in-process UI revision. External code can change the
            // running source without visiting either U1 Settings surface.
            var current = CaptureWorkspaceTrackingSettings(
                configurable.GetConfigurationSettings());
            if (current == null || current.Count != openedValues.Count)
                return false;
            foreach (var pair in openedValues)
            {
                if (!current.TryGetValue(pair.Key, out var value) ||
                    !string.Equals(pair.Value, value, StringComparison.Ordinal))
                    return false;
            }
            return true;
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
            if (!IsWorkspaceTrackingEditableSchema(settings))
            {
                // Do not retain a previous editor or offer Save with zero
                // fields while the requested provider has no schema yet.
                RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92, settings == null
                    ? "Tracking settings not ready. Refresh and try again."
                    : "Too many device settings.");
                return;
            }

            _workspaceNativeActiveTrackingId = controlId;
            _workspaceNativeActiveTrackingProvider = control;
            _workspaceNativeTrackingEditVersion =
                GetWorkspaceTrackingEditVersion(controlId);
            _workspaceNativeTrackingOpenValues = CaptureWorkspaceTrackingSettings(settings);
            _workspaceNativeTrackingFields.Clear();
            _workspaceNativeSettings.ClearCategory(2);
            _workspaceNativeSettings.Add(2, 6, DesktopNativeSettingsWindow.ControlKind.Label,
                configurable.ConfigurationTitle);
            // Match UI Toolkit's case-insensitive key semantics.
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < settings.Count; i++)
            {
                var setting = settings[i];
                if (string.IsNullOrWhiteSpace(setting.Key) ||
                    !seenKeys.Add(setting.Key))
                    continue;
                _workspaceNativeSettings.Add(2, 6000 + i,
                    DesktopNativeSettingsWindow.ControlKind.Edit,
                    setting.Label, GetWorkspaceTrackingDisplayedValue(setting));
                _workspaceNativeTrackingFields[6000 + i] = setting.Key;
            }
            _workspaceNativeSettings.Add(2, 6900, DesktopNativeSettingsWindow.ControlKind.Button,
                "Save and apply");
            _workspaceNativeSettings.Add(2, 6901, DesktopNativeSettingsWindow.ControlKind.Button,
                "Back to devices");
            _workspaceNativeSettings.Add(2, 92, DesktopNativeSettingsWindow.ControlKind.Label,
                "Values are validated by the selected tracking provider.");
            _workspaceNativeSettings.SelectCategory(2);
        }

        private bool IsWorkspaceTrackingMembershipStateCurrent(
            string sourceId, bool displayedConfigured)
        {
            return _workspaceConfiguredTracking.Contains(sourceId) == displayedConfigured;
        }

        private bool IsWorkspaceTrackingToggleIntentCurrent(
            string sourceId, ITrackingRuntimeControl control,
            bool displayedEnabled, int displayedVersion)
        {
            // A previous Enable label must never become a Disable command
            // because another UI, recovery or provider changed state.
            return control != null &&
                   _workspaceConfiguredTracking.Contains(sourceId) &&
                   control.ControlEnabled == displayedEnabled &&
                   IsWorkspaceTrackingEditVersionCurrent(sourceId, displayedVersion);
        }

        private bool IsWorkspaceTrackingRecoveryActionAvailable(
            string sourceId, ITrackingRuntimeControl control)
        {
            // A live On state alone is insufficient: a saved-Off preference
            // means capture is pending reconciliation to Off, not recoverable.
            return control != null &&
                   _workspaceConfiguredTracking.Contains(sourceId) &&
                   control.ControlEnabled &&
                   PlayerPrefs.GetInt(
                       TrackingEnabledKey + GetWorkspaceTrackingPersistenceId(sourceId), 0) != 0;
        }

        private bool IsWorkspaceTrackingRecoveryIntentCurrent(
            string sourceId, ITrackingRuntimeControl control,
            bool displayedEnabled, int displayedVersion)
        {
            // A successful Recover restarts a capture session and advances
            // the shared revision. Do not replay an old Recover button.
            return displayedEnabled &&
                   IsWorkspaceTrackingRecoveryActionAvailable(sourceId, control) &&
                   IsWorkspaceTrackingToggleIntentCurrent(
                       sourceId, control, displayedEnabled, displayedVersion);
        }

        private bool IsWorkspaceTrackingConfiguredEditorCurrent(
            string sourceId, ITrackingRuntimeControl displayed,
            ITrackingRuntimeControl live)
        {
            // A second UI may remove a source while its settings editor is
            // still open. Save must not silently re-add the removed source.
            return _workspaceConfiguredTracking.Contains(sourceId) &&
                   IsWorkspaceTrackingEditorSourceCurrent(displayed, live);
        }

        private void SaveWorkspaceNativeTrackingDetails()
        {
            var control = FindWorkspaceTrackingSource(_workspaceNativeActiveTrackingId);
            if (!IsWorkspaceTrackingConfiguredEditorCurrent(
                    _workspaceNativeActiveTrackingId,
                    _workspaceNativeActiveTrackingProvider, control) ||
                !IsWorkspaceTrackingEditVersionCurrent(
                    _workspaceNativeActiveTrackingId,
                    _workspaceNativeTrackingEditVersion))
            {
                // Rejected if the provider was replaced OR another panel
                // removed it after this editor was opened.
                RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92,
                    "Tracking source or settings changed. Reopen settings before saving.");
                return;
            }
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
                if (!AreWorkspaceTrackingSettingsUnchanged(
                        configurable, _workspaceNativeTrackingOpenValues))
                {
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SetText(92,
                        "Live tracking settings changed. Reopen the editor before saving.");
                    return;
                }
                if (!configurable.TryApplyConfiguration(values, out var error))
                {
                    _workspaceNativeSettings.SetText(92,
                        "Apply failed: " + (error ?? "unknown error"));
                    return;
                }
                foreach (var pair in values)
                    PlayerPrefs.SetString(TrackingSettingKey +
                                          GetWorkspaceTrackingPersistenceId(control.ControlId) +
                                          "." + pair.Key, pair.Value ?? "");
                _workspaceRestoredTracking[control.ControlId] = control;
                InvalidateWorkspaceTrackingEditors(control.ControlId);
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

        private bool TrySetWorkspaceTrackingEnabled(
            ITrackingRuntimeControl control, bool enabled, out string feedback)
        {
            if (control == null)
            {
                feedback = "Tracking source disconnected.";
                return false;
            }
            var id = control.ControlId;
            if (!_workspaceConfiguredTracking.Contains(id))
            {
                feedback = "Add the tracking source before enabling or disabling it.";
                return false;
            }
            try
            {
                if (!control.TrySetControlEnabled(enabled, out var error))
                {
                    feedback = "Tracking toggle failed: " + (error ?? "unknown error");
                    return false;
                }
                // State transitions may restart capture or reset settings
                // without changing the exposed readback. Invalidate editors
                // opened before the transition on either U1 surface.
                InvalidateWorkspaceTrackingEditors(id);
                _workspaceRestoredTracking[id] = control;
                PlayerPrefs.SetInt(
                    TrackingEnabledKey + GetWorkspaceTrackingPersistenceId(id),
                    enabled ? 1 : 0);
                PlayerPrefs.Save();
                feedback = (control.DisplayName ?? id) +
                    (enabled ? " enabled." : " disabled.");
                return true;
            }
            catch (Exception ex)
            {
                feedback = "Tracking toggle failed: " + ex.Message;
                Debug.LogException(ex, this);
                return false;
            }
        }

        private bool TryRequestWorkspaceTrackingRecovery(
            ITrackingRuntimeControl control, out string feedback)
        {
            if (control == null)
            {
                feedback = "Tracking source disconnected.";
                return false;
            }
            if (!_workspaceConfiguredTracking.Contains(control.ControlId))
            {
                feedback = "Add the tracking source before requesting recovery.";
                return false;
            }
            // MediaPipe and Ultraleap's TryRecover can enable a disabled
            // component. Recovery is not an implicit Enable command.
            if (!control.ControlEnabled)
            {
                feedback = "Enable the tracking source before requesting recovery.";
                return false;
            }
            // A provider can be active before startup preferences are
            // restored. Do not let Recover restart a device the user saved
            // as Off; current runtime state alone is not authorization.
            var persistenceId = GetWorkspaceTrackingPersistenceId(control.ControlId);
            if (PlayerPrefs.GetInt(TrackingEnabledKey + persistenceId, 0) == 0)
            {
                feedback = "Tracking source is saved as disabled. Enable it before recovery.";
                return false;
            }
            try
            {
                if (!control.TryRecover(out var error))
                {
                    feedback = "Recovery failed: " + (error ?? "unknown error");
                    return false;
                }
                // Recovery can reset internal device state without changing
                // the provider reference or immediately visible settings.
                // An editor opened before recovery must not save its old draft.
                InvalidateWorkspaceTrackingEditors(control.ControlId);
                // A successful restart may reset live configuration without
                // replacing the provider component. Replay saved settings on
                // the next scan, rather than treating the instance as restored.
                _workspaceRestoredTracking.Remove(control.ControlId);
                _nextTrackingRestoreScan = Time.unscaledTime + 2f;
                feedback = "Recovery requested. Saved tracking settings will be rechecked.";
                return true;
            }
            catch (Exception ex)
            {
                feedback = "Recovery failed: " + ex.Message;
                Debug.LogException(ex, this);
                return false;
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
            if (!_workspaceNativeTrackingProviders.TryGetValue(slot, out var displayed) ||
                !IsWorkspaceTrackingEditorSourceCurrent(displayed, control))
            {
                RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92,
                    "Tracking source changed. Refresh and select it again.");
                return true;
            }
            if (action < 5200)
                ShowWorkspaceNativeTrackingDetails(id);
            else if (action < 5400)
            {
                if (!_workspaceNativeTrackingEnabled.TryGetValue(slot, out var displayedEnabled) ||
                    !_workspaceNativeTrackingVersions.TryGetValue(slot, out var displayedVersion) ||
                    !IsWorkspaceTrackingToggleIntentCurrent(
                        id, control, displayedEnabled, displayedVersion))
                {
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SetText(92,
                        "Tracking state changed. Select Enable/Disable again.");
                    return true;
                }
                var changed = TrySetWorkspaceTrackingEnabled(
                    control, !displayedEnabled, out var feedback);
                if (changed) RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92, feedback);
            }
            else if (action < 5600)
            {
                if (!_workspaceNativeTrackingEnabled.TryGetValue(slot, out var displayedEnabled) ||
                    !_workspaceNativeTrackingVersions.TryGetValue(slot, out var displayedVersion) ||
                    !IsWorkspaceTrackingRecoveryIntentCurrent(
                        id, control, displayedEnabled, displayedVersion))
                {
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SetText(92,
                        "Tracking session changed. Select Recover again.");
                    return true;
                }
                var recovered = TryRequestWorkspaceTrackingRecovery(control, out var feedback);
                if (recovered) RebuildWorkspaceNativeTrackingSources();
                _workspaceNativeSettings.SetText(92, feedback);
            }
            else
            {
                if (!_workspaceNativeTrackingConfigured.TryGetValue(slot, out var displayedConfigured) ||
                    !IsWorkspaceTrackingMembershipStateCurrent(id, displayedConfigured))
                {
                    RebuildWorkspaceNativeTrackingSources();
                    _workspaceNativeSettings.SetText(92,
                        "Tracking membership changed. Select Add/Remove again.");
                    return true;
                }
                var remove = displayedConfigured;
                // Resolve before removal, before the original spelling is lost.
                var persistenceId = GetWorkspaceTrackingPersistenceId(id);
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
                    PlayerPrefs.SetInt(TrackingEnabledKey + persistenceId, 0);
                }
                else
                {
                    // Newly added inputs start disabled. Do not cache or
                    // persist membership before the provider accepts this.
                    if (control.ControlEnabled && !control.TrySetControlEnabled(false, out var addError))
                    {
                        _workspaceNativeSettings.SetText(92, "Add failed: " + addError);
                        return true;
                    }
                    _workspaceConfiguredTracking.Add(id);
                    _workspaceRestoredTracking[id] = control;
                    PlayerPrefs.SetInt(TrackingEnabledKey + persistenceId, 0);
                }
                InvalidateWorkspaceTrackingEditors(id);
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
                var sources = DiscoverWorkspaceTrackingSources(out var ambiguous);
                var signature = "blocked:" + ambiguous + ";";
                foreach (var source in sources)
                {
                    // Rebuild the controls when an instance is replaced even
                    // if ControlId, display name and health are unchanged.
                    var behaviour = source as MonoBehaviour;
                    signature += source.ControlId + "\u001f" +
                        (behaviour != null ? behaviour.GetInstanceID() : 0) + "\u001f" +
                        source.DisplayName + "\u001f" +
                        source.ControlEnabled + "\u001f" + source.ControlHealthState +
                        "\u001f" + source.ControlError + "\u001f" +
                        _workspaceConfiguredTracking.Contains(source.ControlId) + "\u001f" +
                        PlayerPrefs.GetInt(TrackingEnabledKey +
                            GetWorkspaceTrackingPersistenceId(source.ControlId), 0) + "\u001e";
                }
                if (signature == _workspaceTrackingPanelSignature)
                    return;
                _workspaceTrackingPanelSignature = signature;
                _workspaceTrackingList.Clear();
                if (ambiguous > 0)
                {
                    var blocked = new Label("Blocked " + ambiguous +
                        " duplicate tracking source ID(s). Resolve provider IDs before editing.");
                    blocked.style.whiteSpace = WhiteSpace.Normal;
                    blocked.style.color = new Color(1f, 0.7f, 0.42f);
                    _workspaceTrackingList.Add(blocked);
                }

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
                    var displayedEnabled = source.ControlEnabled;
                    var displayedVersion = GetWorkspaceTrackingEditVersion(sourceId);
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
                    buttons.Add(new Button(() => UpdateWorkspaceTrackingMembership(sourceId, source, configured))
                        { text = configured ? "Remove" : "Add" });
                    if (configured)
                    {
                        buttons.Add(new Button(() => ToggleWorkspaceTrackingEnabled(
                                sourceId, source, displayedEnabled, displayedVersion))
                            { text = displayedEnabled ? "Disable" : "Enable" });
                        if (IsWorkspaceTrackingRecoveryActionAvailable(sourceId, source))
                            buttons.Add(new Button(() => RecoverWorkspaceTrackingSource(
                                    sourceId, source, displayedEnabled, displayedVersion))
                                { text = "Recover" });
                        if (source is ITrackingRuntimeConfigurable)
                            buttons.Add(new Button(() => EditWorkspaceTrackingSettings(sourceId, source))
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

        private bool IsWorkspaceTrackingPanelActionCurrent(
            string sourceId, ITrackingRuntimeControl displayedSource)
        {
            var live = FindWorkspaceTrackingSource(sourceId);
            if (IsWorkspaceTrackingEditorSourceCurrent(displayedSource, live))
                return true;
            SetWorkspaceTrackingFeedback(
                "Tracking source changed. Select it again before continuing.");
            _workspaceTrackingPanelSignature = null;
            _nextWorkspaceTrackingPanelRefresh = 0f;
            RefreshWorkspaceTrackingPanel();
            return false;
        }

        private void UpdateWorkspaceTrackingMembership(
            string sourceId, ITrackingRuntimeControl displayedSource,
            bool displayedConfigured)
        {
            try
            {
                if (!IsWorkspaceTrackingPanelActionCurrent(sourceId, displayedSource))
                    return;
                if (!IsWorkspaceTrackingMembershipStateCurrent(sourceId, displayedConfigured))
                {
                    SetWorkspaceTrackingFeedback(
                        "Tracking membership changed. Select Add/Remove again.");
                    _workspaceTrackingPanelSignature = null;
                    _nextWorkspaceTrackingPanelRefresh = 0f;
                    RefreshWorkspaceTrackingPanel();
                    return;
                }
                var source = displayedSource;
                if (source == null)
                {
                    SetWorkspaceTrackingFeedback("Tracking source disconnected.");
                    return;
                }
                var configured = displayedConfigured;
                var persistenceId = GetWorkspaceTrackingPersistenceId(sourceId);
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
                    _workspaceRestoredTracking[sourceId] = source;
                }
                PlayerPrefs.SetInt(TrackingEnabledKey + persistenceId, 0);
                InvalidateWorkspaceTrackingEditors(sourceId);
                PersistConfiguredWorkspaceTracking();
                TrackingPanelActionFinished((source.DisplayName ?? sourceId) +
                    (configured ? " removed." : " added (disabled)."));

                if (!configured && source is ITrackingRuntimeConfigurable settings &&
                    settings.OpenConfigurationOnAdd)
                    EditWorkspaceTrackingSettings(sourceId, source);
            }
            catch (Exception ex)
            {
                SetWorkspaceTrackingFeedback("Tracking source update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ToggleWorkspaceTrackingEnabled(
            string sourceId, ITrackingRuntimeControl displayedSource,
            bool displayedEnabled, int displayedVersion)
        {
            if (!IsWorkspaceTrackingPanelActionCurrent(sourceId, displayedSource))
                return;
            if (!IsWorkspaceTrackingToggleIntentCurrent(
                    sourceId, displayedSource, displayedEnabled, displayedVersion))
            {
                TrackingPanelActionFinished(
                    "Tracking state changed. Select Enable/Disable again.");
                return;
            }
            if (TrySetWorkspaceTrackingEnabled(
                    displayedSource, !displayedEnabled, out var feedback))
                TrackingPanelActionFinished(feedback);
            else
                SetWorkspaceTrackingFeedback(feedback);
        }

        private void RecoverWorkspaceTrackingSource(
            string sourceId, ITrackingRuntimeControl displayedSource,
            bool displayedEnabled, int displayedVersion)
        {
            if (!IsWorkspaceTrackingPanelActionCurrent(sourceId, displayedSource))
                return;
            if (!IsWorkspaceTrackingRecoveryIntentCurrent(
                    sourceId, displayedSource, displayedEnabled, displayedVersion))
            {
                TrackingPanelActionFinished(
                    "Tracking session changed. Select Recover again.");
                return;
            }
            if (TryRequestWorkspaceTrackingRecovery(displayedSource, out var feedback))
                TrackingPanelActionFinished(feedback);
            else
                SetWorkspaceTrackingFeedback(feedback);
        }

        private void EditWorkspaceTrackingSettings(
            string sourceId, ITrackingRuntimeControl displayedSource)
        {
            if (_workspaceTrackingEditor == null) return;
            if (!IsWorkspaceTrackingPanelActionCurrent(sourceId, displayedSource))
            {
                _workspaceTrackingEditor.Clear();
                return;
            }
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
                if (!IsWorkspaceTrackingEditableSchema(settings))
                {
                    SetWorkspaceTrackingFeedback("Tracking configuration is unavailable.");
                    return;
                }
                _workspaceTrackingEditor.Add(new Label(configurable.ConfigurationTitle));
                var openedVersion = GetWorkspaceTrackingEditVersion(sourceId);
                var openedValues = CaptureWorkspaceTrackingSettings(settings);
                var fields = new Dictionary<string, TextField>(StringComparer.OrdinalIgnoreCase);
                foreach (var setting in settings)
                {
                    if (string.IsNullOrWhiteSpace(setting.Key) ||
                        fields.ContainsKey(setting.Key))
                        continue;
                    var field = new TextField(setting.Label)
                    {
                        value = GetWorkspaceTrackingDisplayedValue(setting),
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
                        if (!IsWorkspaceTrackingConfiguredEditorCurrent(
                                sourceId, source, live) ||
                            !IsWorkspaceTrackingEditVersionCurrent(
                                sourceId, openedVersion))
                        {
                            _workspaceTrackingEditor.Clear();
                            SetWorkspaceTrackingFeedback(
                                "Tracking source or settings changed. Reopen settings before saving.");
                            return;
                        }
                        if (!(live is ITrackingRuntimeConfigurable target))
                        {
                            SetWorkspaceTrackingFeedback("Tracking source disconnected.");
                            return;
                        }
                        if (!AreWorkspaceTrackingSettingsUnchanged(
                                target, openedValues))
                        {
                            _workspaceTrackingEditor.Clear();
                            SetWorkspaceTrackingFeedback(
                                "Live tracking settings changed. Reopen the editor before saving.");
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
                            PlayerPrefs.SetString(TrackingSettingKey +
                                GetWorkspaceTrackingPersistenceId(sourceId) +
                                "." + pair.Key, pair.Value);
                        _workspaceRestoredTracking[sourceId] = live;
                        InvalidateWorkspaceTrackingEditors(sourceId);
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
