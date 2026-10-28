using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Scene;
using VCR.Runtime.Tracking;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.UI
{
    // Uses existing pose/expression mixer contracts. No automatic creation of
    // providers: a disconnected manual layer must never report success.
    public sealed partial class WorkspaceToolkitShell
    {
        private TextField _workspaceMotionPoseWeight;
        private TextField _workspaceMotionExpressionName;
        private TextField _workspaceMotionExpressionValue;
        private Label _workspaceMotionStatus;
        private Label _workspaceMotionFeedback;
        private Button _workspaceMotionApplyWeight;
        private Button _workspaceMotionApplyExpression;
        private Button _workspaceMotionClearExpression;
        private Button _workspaceMotionClearAllExpressions;
        private string _workspaceMotionWeightDraft;
        private string _workspaceMotionExpressionDraft;
        private string _workspaceMotionValueDraft;
        private int _workspaceMotionObservedCharacter = int.MinValue;
        private float _nextWorkspaceMotionRefresh;

        private void ResetWorkspaceMotionPanel()
        {
            _workspaceMotionPoseWeight = null;
            _workspaceMotionExpressionName = null;
            _workspaceMotionExpressionValue = null;
            _workspaceMotionStatus = null;
            _workspaceMotionFeedback = null;
            _workspaceMotionApplyWeight = null;
            _workspaceMotionApplyExpression = null;
            _workspaceMotionClearExpression = null;
            _workspaceMotionClearAllExpressions = null;
            _workspaceMotionWeightDraft = null;
            _workspaceMotionExpressionDraft = null;
            _workspaceMotionValueDraft = null;
            _workspaceMotionObservedCharacter = int.MinValue;
            _nextWorkspaceMotionRefresh = 0f;
        }

        private void CaptureWorkspaceMotionDrafts()
        {
            if (_workspaceMotionPoseWeight != null)
                _workspaceMotionWeightDraft = _workspaceMotionPoseWeight.value;
            if (_workspaceMotionExpressionName != null)
                _workspaceMotionExpressionDraft = _workspaceMotionExpressionName.value;
            if (_workspaceMotionExpressionValue != null)
                _workspaceMotionValueDraft = _workspaceMotionExpressionValue.value;
        }

        private void BuildWorkspaceMotionPanel(VisualElement container)
        {
            var title = new Label("Motion / manual expression");
            title.style.marginTop = 10;
            container.Add(title);
            _workspaceMotionStatus = new Label("Checking motion mixer...");
            _workspaceMotionStatus.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_workspaceMotionStatus);
            _workspaceMotionPoseWeight = new TextField("Pose layer weight (0–1)");
            _workspaceMotionPoseWeight.SetValueWithoutNotify(
                _workspaceMotionWeightDraft ?? "");
            container.Add(_workspaceMotionPoseWeight);
            _workspaceMotionApplyWeight = new Button(ApplyWorkspacePoseWeight)
                { text = "Apply pose weight" };
            container.Add(_workspaceMotionApplyWeight);
            _workspaceMotionExpressionName = new TextField("Standard expression name");
            _workspaceMotionExpressionName.SetValueWithoutNotify(
                _workspaceMotionExpressionDraft ?? "happy");
            container.Add(_workspaceMotionExpressionName);
            _workspaceMotionExpressionValue = new TextField("Expression value (0–1)");
            _workspaceMotionExpressionValue.SetValueWithoutNotify(
                _workspaceMotionValueDraft ?? "1");
            container.Add(_workspaceMotionExpressionValue);
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            _workspaceMotionApplyExpression = new Button(() =>
                ApplyWorkspaceManualExpression(false)) { text = "Set expression" };
            _workspaceMotionClearExpression = new Button(() =>
                ApplyWorkspaceManualExpression(true)) { text = "Clear expression" };
            _workspaceMotionClearAllExpressions = new Button(ClearWorkspaceAllExpressions)
                { text = "Clear all expressions" };
            actions.Add(_workspaceMotionApplyExpression);
            actions.Add(_workspaceMotionClearExpression);
            actions.Add(_workspaceMotionClearAllExpressions);
            container.Add(actions);
            _workspaceMotionFeedback = new Label(
                "Manual expressions require a layer actually connected to the current mixer.");
            _workspaceMotionFeedback.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_workspaceMotionFeedback);
            _nextWorkspaceMotionRefresh = 0f;
            RefreshWorkspaceMotionPanel();
        }

        private bool ResolveWorkspaceMotionServices(
            out MotionExpressionMixer mixer, out ManualExpressionLayerSource manual)
        {
            mixer = null;
            manual = null;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            if (character == null) return false;
            // Match the active VRM. A service belonging to a different
            // still-alive model must not be selected as a fallback.
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
                var owner = behaviour.GetComponentInParent<UniVRM10.Vrm10Instance>();
                if (owner != null && owner != character) continue;
                if (behaviour is MotionExpressionMixer candidateMixer)
                {
                    // An ambiguous mixer must disable writes.
                    if (mixer != null) { mixer = null; return false; }
                    mixer = candidateMixer;
                }
            }
            if (mixer == null) return false;
            foreach (var behaviour in behaviours)
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled ||
                    !(behaviour is ManualExpressionLayerSource source) ||
                    !mixer.IsExpressionLayerProvider(source)) continue;
                var owner = behaviour.GetComponentInParent<UniVRM10.Vrm10Instance>();
                if (owner != null && owner != character) continue;
                if (manual != null) { manual = null; break; }
                manual = source;
            }
            return true;
        }

        private void RefreshWorkspaceMotionPanel()
        {
            if (_workspaceMotionStatus == null ||
                Time.unscaledTime < _nextWorkspaceMotionRefresh) return;
            _nextWorkspaceMotionRefresh = Time.unscaledTime + 0.7f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            var characterId = character != null ? character.GetInstanceID() : 0;
            if (_workspaceMotionObservedCharacter != int.MinValue &&
                characterId != _workspaceMotionObservedCharacter)
            {
                _workspaceMotionWeightDraft = "";
                _workspaceMotionExpressionDraft = "";
                _workspaceMotionValueDraft = "";
                _workspaceMotionPoseWeight?.SetValueWithoutNotify("");
                _workspaceMotionExpressionName?.SetValueWithoutNotify("");
                _workspaceMotionExpressionValue?.SetValueWithoutNotify("");
            }
            _workspaceMotionObservedCharacter = characterId;
            var found = ResolveWorkspaceMotionServices(out var mixer, out var manual);
            var allowed = found && sceneRuntime != null &&
                !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            _workspaceMotionApplyWeight?.SetEnabled(
                allowed && mixer.PrimaryPoseLayerConfigured);
            _workspaceMotionApplyExpression?.SetEnabled(allowed && manual != null);
            _workspaceMotionClearExpression?.SetEnabled(allowed && manual != null);
            _workspaceMotionClearAllExpressions?.SetEnabled(allowed && manual != null);
            _workspaceMotionStatus.text = !found
                ? "Motion mixer unavailable or ambiguous for the active VRM."
                : "Primary pose: " +
                  (mixer.PrimaryPoseLayerConfigured ? "configured" : "not configured") +
                  " | Weight: " + mixer.PrimaryPoseLayerWeight.ToString("0.###",
                      CultureInfo.InvariantCulture) +
                  "\nManual expression: " + (manual != null ? "connected" : "not connected");
            if (found && _workspaceMotionPoseWeight != null &&
                string.IsNullOrWhiteSpace(_workspaceMotionPoseWeight.value))
                _workspaceMotionPoseWeight.SetValueWithoutNotify(
                    mixer.PrimaryPoseLayerWeight.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private bool TryGetWorkspaceMotionForEdit(
            out MotionExpressionMixer mixer, out ManualExpressionLayerSource manual)
        {
            mixer = null;
            manual = null;
            if (sceneRuntime == null ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State) ||
                _characterActionPending || sceneRuntime.CurrentCharacter == null ||
                (_workspaceMotionObservedCharacter != int.MinValue &&
                 sceneRuntime.CurrentCharacter.GetInstanceID() !=
                    _workspaceMotionObservedCharacter))
            {
                _nextWorkspaceMotionRefresh = 0f;
                RefreshWorkspaceMotionPanel();
                ReportWorkspaceMotion("Motion edit unavailable: active VRM changed or busy.");
                return false;
            }
            if (!ResolveWorkspaceMotionServices(out mixer, out manual))
            {
                ReportWorkspaceMotion("No unambiguous motion mixer for active VRM.");
                return false;
            }
            return true;
        }

        private void ApplyWorkspacePoseWeight()
        {
            if (!TryGetWorkspaceMotionForEdit(out var mixer, out _)) return;
            if (!float.TryParse(_workspaceMotionPoseWeight?.value?.Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ||
                float.IsNaN(weight) || float.IsInfinity(weight) ||
                weight < 0f || weight > 1f)
            {
                ReportWorkspaceMotion("Pose weight must be finite and between 0 and 1.");
                return;
            }
            try
            {
                ReportWorkspaceMotion(mixer.TrySetPrimaryPoseLayerWeight(weight, out var error)
                    ? "Primary pose weight applied: " +
                      mixer.PrimaryPoseLayerWeight.ToString("0.###",
                          CultureInfo.InvariantCulture)
                    : "Pose layer update failed: " + (error ?? "unknown error"));
            }
            catch (Exception ex)
            {
                ReportWorkspaceMotion("Pose layer update failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
            _nextWorkspaceMotionRefresh = 0f;
        }

        private void ApplyWorkspaceManualExpression(bool clear)
        {
            if (!TryGetWorkspaceMotionForEdit(out _, out var manual)) return;
            if (manual == null)
            {
                ReportWorkspaceMotion("No connected manual expression layer.");
                return;
            }
            var name = _workspaceMotionExpressionName?.value?.Trim();
            if (!StandardExpressionNames.TryParse(name, out var expression))
            {
                ReportWorkspaceMotion("Unknown standard expression: " + (name ?? ""));
                return;
            }
            var value = 0f;
            if (!clear && (!float.TryParse(_workspaceMotionExpressionValue?.value?.Trim(),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value) ||
                value < 0f || value > 1f))
            {
                ReportWorkspaceMotion("Expression value must be finite and in the 0–1 range.");
                return;
            }
            try
            {
                var applied = clear ? manual.ClearExpression(expression) :
                    manual.SetExpression(expression, value);
                ReportWorkspaceMotion(applied
                    ? "Expression " + StandardExpressionNames.GetVrm1Name(expression) +
                      (clear ? " cleared." : " set.")
                    : "Expression layer rejected the edit.");
            }
            catch (Exception ex)
            {
                ReportWorkspaceMotion("Expression edit failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ClearWorkspaceAllExpressions()
        {
            if (!TryGetWorkspaceMotionForEdit(out _, out var manual)) return;
            if (manual == null)
            {
                ReportWorkspaceMotion("No connected manual expression layer.");
                return;
            }
            try
            {
                manual.ClearAll();
                ReportWorkspaceMotion("All manual expressions cleared.");
            }
            catch (Exception ex)
            {
                ReportWorkspaceMotion("Clear expressions failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
        }

        private void ReportWorkspaceMotion(string message)
        {
            if (_workspaceMotionFeedback != null)
                _workspaceMotionFeedback.text = message;
        }
    }
}
