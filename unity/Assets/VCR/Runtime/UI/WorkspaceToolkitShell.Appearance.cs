using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Appearance;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    // The workspace edits the appearance runtime for the active character only.
    // It does not create an appearance provider or mutate imported VRM assets.
    public sealed partial class WorkspaceToolkitShell
    {
        private enum WorkspaceAppearanceAction
        {
            Preset, Outfit, SetAccessory, ClearAccessory, Restore, Cancel
        }

        private TextField _appearancePresetInput;
        private TextField _appearanceOutfitInput;
        private TextField _appearanceAccessorySlotInput;
        private TextField _appearanceAccessoryIdInput;
        private Label _appearanceWorkspaceStatus;
        private Label _appearanceWorkspaceFeedback;
        private VisualElement _appearancePresetList;
        private VisualElement _appearanceTransitionList;
        private Button _appearanceCancelButton;
        private Button _appearanceRestoreButton;
        private Button _appearanceApplyPresetButton;
        private Button _appearanceApplyOutfitButton;
        private Button _appearanceSetAccessoryButton;
        private Button _appearanceClearAccessoryButton;
        private string _appearancePresetDraft;
        private string _appearanceOutfitDraft;
        private string _appearanceSlotDraft;
        private string _appearanceAccessoryDraft;
        private string _appearanceTransitionId = "Immediate";
        private string _appearanceWorkspaceSignature;
        private int _appearanceObservedCharacter = int.MinValue;
        private float _nextAppearanceWorkspaceRefresh;

        private void ResetWorkspaceAppearancePanel()
        {
            _appearancePresetInput = null;
            _appearanceOutfitInput = null;
            _appearanceAccessorySlotInput = null;
            _appearanceAccessoryIdInput = null;
            _appearanceWorkspaceStatus = null;
            _appearanceWorkspaceFeedback = null;
            _appearancePresetList = null;
            _appearanceTransitionList = null;
            _appearanceCancelButton = null;
            _appearanceRestoreButton = null;
            _appearanceApplyPresetButton = null;
            _appearanceApplyOutfitButton = null;
            _appearanceSetAccessoryButton = null;
            _appearanceClearAccessoryButton = null;
            _appearancePresetDraft = null;
            _appearanceOutfitDraft = null;
            _appearanceSlotDraft = null;
            _appearanceAccessoryDraft = null;
            _appearanceTransitionId = "Immediate";
            _appearanceWorkspaceSignature = null;
            _appearanceObservedCharacter = int.MinValue;
            _nextAppearanceWorkspaceRefresh = 0f;
        }

        private void CaptureWorkspaceAppearanceDraft()
        {
            if (_appearancePresetInput != null)
                _appearancePresetDraft = _appearancePresetInput.value;
            if (_appearanceOutfitInput != null)
                _appearanceOutfitDraft = _appearanceOutfitInput.value;
            if (_appearanceAccessorySlotInput != null)
                _appearanceSlotDraft = _appearanceAccessorySlotInput.value;
            if (_appearanceAccessoryIdInput != null)
                _appearanceAccessoryDraft = _appearanceAccessoryIdInput.value;
        }

        private void BuildWorkspaceAppearancePanel(VisualElement container)
        {
            _appearanceWorkspaceSignature = null;
            _nextAppearanceWorkspaceRefresh = 0f;
            var heading = new Label("Character appearance");
            heading.style.marginTop = 10;
            container.Add(heading);
            _appearanceWorkspaceStatus = new Label("Checking active character appearance...");
            _appearanceWorkspaceStatus.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_appearanceWorkspaceStatus);

            _appearanceTransitionList = new VisualElement();
            container.Add(_appearanceTransitionList);
            _appearancePresetList = new VisualElement();
            container.Add(_appearancePresetList);

            _appearancePresetInput = new TextField("Preset ID");
            _appearancePresetInput.SetValueWithoutNotify(_appearancePresetDraft ?? "");
            container.Add(_appearancePresetInput);
            _appearanceOutfitInput = new TextField("Outfit ID");
            _appearanceOutfitInput.SetValueWithoutNotify(_appearanceOutfitDraft ?? "");
            container.Add(_appearanceOutfitInput);
            _appearanceAccessorySlotInput = new TextField("Accessory slot");
            _appearanceAccessorySlotInput.SetValueWithoutNotify(_appearanceSlotDraft ?? "");
            container.Add(_appearanceAccessorySlotInput);
            _appearanceAccessoryIdInput = new TextField("Accessory ID");
            _appearanceAccessoryIdInput.SetValueWithoutNotify(_appearanceAccessoryDraft ?? "");
            container.Add(_appearanceAccessoryIdInput);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            _appearanceApplyPresetButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.Preset)) { text = "Apply preset" };
            _appearanceApplyOutfitButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.Outfit)) { text = "Apply outfit" };
            _appearanceSetAccessoryButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.SetAccessory)) { text = "Set accessory" };
            _appearanceClearAccessoryButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.ClearAccessory)) { text = "Clear accessory" };
            _appearanceRestoreButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.Restore)) { text = "Restore default" };
            _appearanceCancelButton = new Button(() =>
                ApplyWorkspaceAppearance(WorkspaceAppearanceAction.Cancel)) { text = "Cancel transition" };
            actions.Add(_appearanceApplyPresetButton);
            actions.Add(_appearanceApplyOutfitButton);
            actions.Add(_appearanceSetAccessoryButton);
            actions.Add(_appearanceClearAccessoryButton);
            actions.Add(_appearanceRestoreButton);
            actions.Add(_appearanceCancelButton);
            container.Add(actions);

            _appearanceWorkspaceFeedback = new Label(
                "Appearance actions apply to the active VRM. Choose a registered ID.");
            _appearanceWorkspaceFeedback.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_appearanceWorkspaceFeedback);
            RefreshWorkspaceAppearancePanel();
        }

        private IAppearanceRuntime FindWorkspaceAppearanceRuntime()
        {
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            if (character == null) return null;
            // Prefer a provider on the active VRM, rather than an arbitrary
            // inactive or previously-loaded character elsewhere in the scene.
            foreach (var behaviour in character.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.isActiveAndEnabled &&
                    behaviour is IAppearanceRuntime onCharacter)
                    return onCharacter;

            // A scene-level appearance service is valid only if unambiguous.
            IAppearanceRuntime only = null;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled ||
                    !(behaviour is IAppearanceRuntime candidate))
                    continue;
                var owner = behaviour.GetComponentInParent<UniVRM10.Vrm10Instance>();
                if (owner != null && owner != character)
                    continue; // Never target a still-alive, different VRM.
                if (only != null) return null;
                only = candidate;
            }
            return only;
        }

        private void RefreshWorkspaceAppearancePanel()
        {
            if (_appearanceWorkspaceStatus == null ||
                Time.unscaledTime < _nextAppearanceWorkspaceRefresh) return;
            _nextAppearanceWorkspaceRefresh = Time.unscaledTime + 0.5f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            var characterId = character != null ? character.GetInstanceID() : 0;
            if (_appearanceObservedCharacter != int.MinValue &&
                characterId != _appearanceObservedCharacter)
            {
                // IDs/slots are model-specific: never carry a pending edit to a
                // replacement VRM even if its registered IDs happen to match.
                _appearancePresetDraft = _appearanceOutfitDraft =
                    _appearanceSlotDraft = _appearanceAccessoryDraft = "";
                _appearancePresetInput?.SetValueWithoutNotify("");
                _appearanceOutfitInput?.SetValueWithoutNotify("");
                _appearanceAccessorySlotInput?.SetValueWithoutNotify("");
                _appearanceAccessoryIdInput?.SetValueWithoutNotify("");
                _appearanceTransitionId = "Immediate";
                _appearanceWorkspaceSignature = null;
            }
            _appearanceObservedCharacter = characterId;

            var runtime = FindWorkspaceAppearanceRuntime();
            var operational = runtime != null && sceneRuntime != null &&
                !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            var status = runtime != null ? runtime.Status : default;
            var editable = operational && ApplicationUiActionPolicy.CanMutateAppearance(
                true, status.State);
            _appearanceApplyPresetButton?.SetEnabled(editable);
            _appearanceApplyOutfitButton?.SetEnabled(editable);
            _appearanceSetAccessoryButton?.SetEnabled(editable);
            _appearanceClearAccessoryButton?.SetEnabled(editable);
            _appearanceRestoreButton?.SetEnabled(editable);
            _appearanceCancelButton?.SetEnabled(operational &&
                ApplicationUiActionPolicy.CanCancelAppearanceTransition(true, status));
            if (runtime == null)
            {
                _appearanceWorkspaceStatus.text = character == null
                    ? "Appearance unavailable: no active VRM."
                    : "Appearance provider unavailable or ambiguous.";
                _appearancePresetList?.Clear();
                _appearanceTransitionList?.Clear();
                _appearanceWorkspaceSignature = null;
                return;
            }

            try
            {
                _appearanceWorkspaceStatus.text =
                    "Runtime: " + status.RuntimeId + " | State: " + status.State +
                    "\nPreset: " + (status.CurrentPresetId ?? "none") +
                    " | Outfit: " + (status.CurrentOutfitId ?? "none") +
                    (status.Busy ? "\nTransition: " + status.ActiveTransitionId +
                        " (" + (status.TransitionProgress01 * 100d).ToString("0.#") + "%)"
                        : "") +
                    (string.IsNullOrWhiteSpace(status.LastError)
                        ? "" : "\nError: " + status.LastError);
                var signature = new StringBuilder(256);
                signature.Append(characterId).Append('|').Append(status.RuntimeId)
                    .Append('|').Append(_appearanceTransitionId);
                foreach (var id in runtime.TransitionIds)
                    signature.Append("|t:").Append(id);
                foreach (var id in runtime.PresetIds)
                    signature.Append("|p:").Append(id);
                if (signature.ToString() == _appearanceWorkspaceSignature) return;
                _appearanceWorkspaceSignature = signature.ToString();
                _appearanceTransitionList.Clear();
                _appearancePresetList.Clear();
                _appearanceTransitionList.Add(new Label(
                    "Transition: " + _appearanceTransitionId));
                _appearanceTransitionList.Add(new Button(() =>
                {
                    _appearanceTransitionId = "Immediate";
                    _appearanceWorkspaceSignature = null;
                    _nextAppearanceWorkspaceRefresh = 0f;
                    RefreshWorkspaceAppearancePanel();
                }) { text = "Immediate" });
                var count = 0;
                foreach (var transitionId in runtime.TransitionIds)
                {
                    if (++count > 32) break;
                    var selected = transitionId;
                    _appearanceTransitionList.Add(new Button(() =>
                    {
                        _appearanceTransitionId = selected;
                        _appearanceWorkspaceSignature = null;
                        _nextAppearanceWorkspaceRefresh = 0f;
                        RefreshWorkspaceAppearancePanel();
                    }) { text = selected });
                }
                if (runtime.TransitionIds.Count > 32)
                    _appearanceTransitionList.Add(new Label("Showing first 32 transitions."));
                _appearancePresetList.Add(new Label("Registered presets"));
                count = 0;
                foreach (var presetId in runtime.PresetIds)
                {
                    if (++count > 40) break;
                    var selected = presetId;
                    _appearancePresetList.Add(new Button(() =>
                        _appearancePresetInput.SetValueWithoutNotify(selected)) { text = selected });
                }
                if (runtime.PresetIds.Count > 40)
                    _appearancePresetList.Add(new Label("Showing first 40 presets; type other IDs above."));
            }
            catch (Exception ex)
            {
                _appearanceWorkspaceStatus.text = "Appearance status failed: " + ex.Message;
                Debug.LogException(ex, this);
            }
        }

        private void ApplyWorkspaceAppearance(WorkspaceAppearanceAction action)
        {
            // The model can change between the 0.5s status refresh and a click.
            // Never apply IDs typed for an old VRM to its replacement.
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var activeCharacter = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            if (activeCharacter == null ||
                (_appearanceObservedCharacter != int.MinValue &&
                 activeCharacter.GetInstanceID() != _appearanceObservedCharacter))
            {
                _nextAppearanceWorkspaceRefresh = 0f;
                RefreshWorkspaceAppearancePanel();
                SetWorkspaceAppearanceFeedback(
                    "Active character changed or is missing. Select appearance IDs again.");
                return;
            }
            var runtime = FindWorkspaceAppearanceRuntime();
            if (runtime == null || sceneRuntime == null || _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                SetWorkspaceAppearanceFeedback("Appearance operation unavailable.");
                return;
            }
            var status = runtime.Status;
            if (action == WorkspaceAppearanceAction.Cancel
                ? !ApplicationUiActionPolicy.CanCancelAppearanceTransition(true, status)
                : !ApplicationUiActionPolicy.CanMutateAppearance(true, status.State))
            {
                SetWorkspaceAppearanceFeedback("Appearance runtime is busy or not ready.");
                return;
            }
            var preset = _appearancePresetInput?.value?.Trim();
            var outfit = _appearanceOutfitInput?.value?.Trim();
            var slot = _appearanceAccessorySlotInput?.value?.Trim();
            var accessory = _appearanceAccessoryIdInput?.value?.Trim();
            if ((action == WorkspaceAppearanceAction.Preset && string.IsNullOrWhiteSpace(preset)) ||
                (action == WorkspaceAppearanceAction.Outfit && string.IsNullOrWhiteSpace(outfit)) ||
                ((action == WorkspaceAppearanceAction.SetAccessory ||
                  action == WorkspaceAppearanceAction.ClearAccessory) &&
                 string.IsNullOrWhiteSpace(slot)) ||
                (action == WorkspaceAppearanceAction.SetAccessory &&
                 string.IsNullOrWhiteSpace(accessory)))
            {
                SetWorkspaceAppearanceFeedback("Enter all required IDs for the selected action.");
                return;
            }
            try
            {
                string error;
                bool applied;
                switch (action)
                {
                    case WorkspaceAppearanceAction.Preset:
                        applied = runtime.SetPreset(preset, _appearanceTransitionId, out error);
                        break;
                    case WorkspaceAppearanceAction.Outfit:
                        applied = runtime.SetOutfit(outfit, _appearanceTransitionId, out error);
                        break;
                    case WorkspaceAppearanceAction.SetAccessory:
                        applied = runtime.SetAccessory(slot, accessory, _appearanceTransitionId, out error);
                        break;
                    case WorkspaceAppearanceAction.ClearAccessory:
                        applied = runtime.ClearAccessory(slot, _appearanceTransitionId, out error);
                        break;
                    case WorkspaceAppearanceAction.Restore:
                        applied = runtime.RestoreDefault(_appearanceTransitionId, out error);
                        break;
                    default:
                        applied = runtime.CancelTransition(out error);
                        break;
                }
                SetWorkspaceAppearanceFeedback(applied
                    ? "Appearance " + action + " requested."
                    : "Appearance " + action + " failed: " + (error ?? "unknown error"));
            }
            catch (Exception ex)
            {
                SetWorkspaceAppearanceFeedback("Appearance action failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
            _nextAppearanceWorkspaceRefresh = 0f;
            RefreshWorkspaceAppearancePanel();
        }

        private void SetWorkspaceAppearanceFeedback(string text)
        {
            if (_appearanceWorkspaceFeedback != null)
                _appearanceWorkspaceFeedback.text = text;
        }
    }
}
