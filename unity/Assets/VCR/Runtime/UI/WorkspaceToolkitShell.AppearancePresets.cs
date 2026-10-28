using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;
using VCR.Runtime.Appearance;

namespace VCR.Runtime.UI
{
    // User appearance presets are persisted per character via the existing
    // bounded, atomic profile store; runtime mutations roll back on save failure.
    public sealed partial class WorkspaceToolkitShell
    {
        private enum WorkspacePresetEdit { SaveCurrent, Delete, Rename, Duplicate, Up, Down }

        private TextField _userPresetNameInput;
        private TextField _userPresetTargetInput;
        private Label _userPresetFeedback;
        private VisualElement _userPresetList;
        private VisualElement _userPresetActions;
        private string _userPresetNameDraft;
        private string _userPresetTargetDraft;
        private string _userPresetListSignature;
        private AppearanceUserPresetStore _workspacePresetStore;
        // Do not reset on UI re-enable: that would overwrite unsaved edits.
        private IAppearanceUserPresetRegistry _workspacePresetLoadedRegistry;
        private string _workspacePresetLoadedPath;
        private string _workspacePresetLoadError;

        private void ResetWorkspaceUserPresetPanel()
        {
            _userPresetNameInput = null;
            _userPresetTargetInput = null;
            _userPresetFeedback = null;
            _userPresetList = null;
            _userPresetActions = null;
            _userPresetNameDraft = null;
            _userPresetTargetDraft = null;
            _userPresetListSignature = null;
        }

        private void CaptureWorkspaceUserPresetDrafts()
        {
            if (_userPresetNameInput != null)
                _userPresetNameDraft = _userPresetNameInput.value;
            if (_userPresetTargetInput != null)
                _userPresetTargetDraft = _userPresetTargetInput.value;
        }

        private void BuildWorkspaceUserPresetPanel(VisualElement container)
        {
            _userPresetListSignature = null;
            var heading = new Label("User appearance presets (per VRM)");
            heading.style.marginTop = 10;
            container.Add(heading);
            _userPresetList = new VisualElement();
            container.Add(_userPresetList);
            _userPresetNameInput = new TextField("User preset ID");
            _userPresetNameInput.SetValueWithoutNotify(_userPresetNameDraft ?? "");
            container.Add(_userPresetNameInput);
            _userPresetTargetInput = new TextField("New ID (rename / duplicate)");
            _userPresetTargetInput.SetValueWithoutNotify(_userPresetTargetDraft ?? "");
            container.Add(_userPresetTargetInput);
            _userPresetActions = new VisualElement();
            _userPresetActions.style.flexDirection = FlexDirection.Row;
            _userPresetActions.style.flexWrap = Wrap.Wrap;
            AddWorkspacePresetButton("Save current", WorkspacePresetEdit.SaveCurrent);
            AddWorkspacePresetButton("Delete", WorkspacePresetEdit.Delete);
            AddWorkspacePresetButton("Rename", WorkspacePresetEdit.Rename);
            AddWorkspacePresetButton("Duplicate", WorkspacePresetEdit.Duplicate);
            AddWorkspacePresetButton("Move up", WorkspacePresetEdit.Up);
            AddWorkspacePresetButton("Move down", WorkspacePresetEdit.Down);
            container.Add(_userPresetActions);
            _userPresetFeedback = new Label(
                "Save, rename, duplicate, remove and reorder use the current model's profile.");
            _userPresetFeedback.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_userPresetFeedback);
        }

        private void AddWorkspacePresetButton(string title, WorkspacePresetEdit edit)
        {
            _userPresetActions.Add(new Button(() => EditWorkspaceUserPreset(edit))
                { text = title });
        }

        private void SetWorkspaceUserPresetFeedback(string message)
        {
            if (_userPresetFeedback != null)
                _userPresetFeedback.text = message;
        }

        private bool EnsureWorkspaceUserPresetsLoaded(
            IAppearanceRuntime runtime, string path)
        {
            if (!(runtime is IAppearanceUserPresetRegistry registry) ||
                string.IsNullOrWhiteSpace(path)) return false;
            if (ReferenceEquals(registry, _workspacePresetLoadedRegistry) &&
                string.Equals(path, _workspacePresetLoadedPath, StringComparison.Ordinal))
                return _workspacePresetLoadError == null;
            // Mark first, even on malformed input: do not repeat a load each
            // frame or during dock/mode and component enable/disable changes.
            _workspacePresetLoadedRegistry = registry;
            _workspacePresetLoadedPath = path;
            _workspacePresetLoadError = null;
            try
            {
                _workspacePresetStore ??= AppearanceUserPresetStore.CreateDefault();
                var file = _workspacePresetStore.GetProfilePath(path);
                // The legacy controller loads this profile as well. Do not
                // race it or overwrite its in-memory pending changes.
                if (FindFirstObjectByType<ApplicationUiController>() != null ||
                    !File.Exists(file)) return true;
                if (!_workspacePresetStore.TryLoad(path, out var presets, out var error))
                {
                    _workspacePresetLoadError = error ?? "read rejected";
                    SetWorkspaceUserPresetFeedback("Profile read failed: " +
                        _workspacePresetLoadError + ". Save disabled to protect the file.");
                    return false;
                }
                if (!registry.ReplaceUserPresets(presets, out error))
                {
                    _workspacePresetLoadError = error ?? "presets incompatible";
                    SetWorkspaceUserPresetFeedback("Profile apply failed: " +
                        _workspacePresetLoadError + ". Save disabled to protect the file.");
                    return false;
                }
                SetWorkspaceUserPresetFeedback("Loaded " + presets.Length +
                    " user appearance preset(s) for the current VRM.");
                return true;
            }
            catch (Exception exception)
            {
                _workspacePresetLoadError = exception.Message;
                SetWorkspaceUserPresetFeedback("Profile read failed: " +
                    exception.Message + ". Save disabled.");
                Debug.LogException(exception, this);
                return false;
            }
        }

        private void RefreshWorkspaceUserPresets(
            IAppearanceRuntime runtime, bool editable)
        {
            if (_userPresetList == null) return;
            var path = sceneRuntime != null ? sceneRuntime.CurrentCharacterPath : null;
            var registry = runtime as IAppearanceUserPresetRegistry;
            var loaded = runtime != null && registry != null &&
                EnsureWorkspaceUserPresetsLoaded(runtime, path);
            _userPresetActions?.SetEnabled(editable && loaded);
            if (registry == null || !loaded)
            {
                _userPresetList.Clear();
                _userPresetListSignature = null;
                _userPresetList.Add(new Label(_workspacePresetLoadError != null
                    ? "Profile unavailable: " + _workspacePresetLoadError
                    : "No user preset registry or loaded VRM path."));
                return;
            }
            var ids = registry.UserPresetIds;
            var signature = path + "|" + registry.GetHashCode() + "|" +
                string.Join("\u001f", ids);
            if (signature == _userPresetListSignature) return;
            _userPresetListSignature = signature;
            _userPresetList.Clear();
            if (ids.Count == 0)
                _userPresetList.Add(new Label("No user presets saved for this model."));
            for (var i = 0; i < Math.Min(ids.Count, 60); i++)
            {
                var id = ids[i];
                _userPresetList.Add(new Button(() =>
                {
                    _userPresetNameInput.SetValueWithoutNotify(id);
                    _appearancePresetInput?.SetValueWithoutNotify(id);
                }) { text = id });
            }
            if (ids.Count > 60)
                _userPresetList.Add(new Label("Showing first 60 of " +
                    ids.Count + " user presets; enter an ID directly."));
        }

        private void EditWorkspaceUserPreset(WorkspacePresetEdit action)
        {
            if (sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                SetWorkspaceUserPresetFeedback("Model runtime unavailable or busy.");
                return;
            }
            var currentCharacter = sceneRuntime.CurrentCharacter;
            if (currentCharacter == null ||
                (_appearanceObservedCharacter != int.MinValue &&
                 currentCharacter.GetInstanceID() != _appearanceObservedCharacter))
            {
                _nextAppearanceWorkspaceRefresh = 0f;
                RefreshWorkspaceAppearancePanel();
                SetWorkspaceUserPresetFeedback("Active VRM changed; select a preset again.");
                return;
            }
            var runtime = FindWorkspaceAppearanceRuntime();
            var registry = runtime as IAppearanceUserPresetRegistry;
            var path = sceneRuntime.CurrentCharacterPath;
            var id = _userPresetNameInput?.value?.Trim();
            var newId = _userPresetTargetInput?.value?.Trim();
            if (registry == null || !EnsureWorkspaceUserPresetsLoaded(runtime, path) ||
                !ApplicationUiActionPolicy.CanMutateAppearance(true, runtime.Status.State) ||
                string.IsNullOrWhiteSpace(id) ||
                ((action == WorkspacePresetEdit.Rename || action == WorkspacePresetEdit.Duplicate)
                 && string.IsNullOrWhiteSpace(newId)))
            {
                SetWorkspaceUserPresetFeedback("Enter valid IDs with an available user preset registry.");
                return;
            }
            try
            {
                var previous = registry.CaptureUserPresets();
                var previousCurrent = runtime.Status.CurrentPresetId;
                string error;
                AppearancePreset saved = null;
                bool changed;
                switch (action)
                {
                    case WorkspacePresetEdit.SaveCurrent:
                        changed = registry.SaveCurrentAsUserPreset(id,
                            _appearanceTransitionId, out saved, out error);
                        break;
                    case WorkspacePresetEdit.Delete:
                        changed = registry.RemoveUserPreset(id, out error);
                        break;
                    case WorkspacePresetEdit.Rename:
                        changed = registry.RenameUserPreset(id, newId, out saved, out error);
                        break;
                    case WorkspacePresetEdit.Duplicate:
                        changed = registry.DuplicateUserPreset(id, newId, out saved, out error);
                        break;
                    default:
                        changed = registry.MoveUserPreset(id,
                            action == WorkspacePresetEdit.Up ? -1 : 1, out error);
                        break;
                }
                if (!changed)
                {
                    SetWorkspaceUserPresetFeedback(action + " failed: " +
                        (error ?? "provider rejected the request"));
                    return;
                }
                if (_workspacePresetStore == null ||
                    !_workspacePresetStore.TrySave(path, registry.CaptureUserPresets(),
                        out error))
                {
                    var restored = registry.ReplaceUserPresets(previous, out var rollbackError);
                    if (restored && !string.IsNullOrWhiteSpace(previousCurrent) &&
                        !string.Equals(runtime.Status.CurrentPresetId, previousCurrent,
                            StringComparison.Ordinal))
                        runtime.SetPreset(previousCurrent, "Immediate", out _);
                    SetWorkspaceUserPresetFeedback("Save failed; runtime rollback " +
                        (restored ? "completed" : "failed: " + rollbackError) +
                        ". " + (error ?? "profile store missing"));
                    return;
                }
                if (saved != null)
                {
                    _userPresetNameInput.SetValueWithoutNotify(saved.Id);
                    _appearancePresetInput?.SetValueWithoutNotify(saved.Id);
                    _userPresetTargetInput?.SetValueWithoutNotify("");
                }
                if (action == WorkspacePresetEdit.Delete &&
                    string.Equals(_appearancePresetInput?.value, id, StringComparison.Ordinal))
                    _appearancePresetInput.SetValueWithoutNotify("");
                _userPresetListSignature = null;
                _appearanceWorkspaceSignature = null;
                _nextAppearanceWorkspaceRefresh = 0f;
                SetWorkspaceUserPresetFeedback(action + " succeeded and profile saved.");
                RefreshWorkspaceAppearancePanel();
            }
            catch (Exception exception)
            {
                SetWorkspaceUserPresetFeedback("Preset operation failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
        }
    }
}
