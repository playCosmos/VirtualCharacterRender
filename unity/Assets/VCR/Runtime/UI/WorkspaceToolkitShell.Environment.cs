using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Environment;

namespace VCR.Runtime.UI
{
    // Delegates state changes and transitions to the existing environment
    // runtime. This is not a new environment lifecycle or asset importer.
    public sealed partial class WorkspaceToolkitShell
    {
        private TextField _workspaceEnvironmentStateInput;
        private TextField _workspaceEnvironmentDurationInput;
        private Button _workspaceEnvironmentModeButton;
        private Button _workspaceEnvironmentApplyButton;
        private Label _workspaceEnvironmentStatus;
        private Label _workspaceEnvironmentFeedback;
        private string _workspaceEnvironmentStateDraft;
        private string _workspaceEnvironmentDurationDraft;
        private EnvironmentTransitionMode _workspaceEnvironmentMode = EnvironmentTransitionMode.Cut;
        private float _nextWorkspaceEnvironmentRefresh;

        private void ResetWorkspaceEnvironmentPanel()
        {
            _workspaceEnvironmentStateInput = null;
            _workspaceEnvironmentDurationInput = null;
            _workspaceEnvironmentModeButton = null;
            _workspaceEnvironmentApplyButton = null;
            _workspaceEnvironmentStatus = null;
            _workspaceEnvironmentFeedback = null;
            _workspaceEnvironmentStateDraft = null;
            _workspaceEnvironmentDurationDraft = null;
            _workspaceEnvironmentMode = EnvironmentTransitionMode.Cut;
            _nextWorkspaceEnvironmentRefresh = 0f;
        }

        private void CaptureWorkspaceEnvironmentDraft()
        {
            if (_workspaceEnvironmentStateInput != null)
                _workspaceEnvironmentStateDraft = _workspaceEnvironmentStateInput.value;
            if (_workspaceEnvironmentDurationInput != null)
                _workspaceEnvironmentDurationDraft = _workspaceEnvironmentDurationInput.value;
        }

        private void BuildWorkspaceEnvironmentPanel(VisualElement panel)
        {
            var heading = new Label("Environment state");
            heading.style.marginTop = 10;
            panel.Add(heading);
            _workspaceEnvironmentStatus = new Label("Environment runtime: checking...");
            _workspaceEnvironmentStatus.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceEnvironmentStatus);

            _workspaceEnvironmentStateInput = new TextField("State ID");
            _workspaceEnvironmentStateInput.tooltip =
                "Use a state ID registered with the active environment provider.";
            _workspaceEnvironmentStateInput.SetValueWithoutNotify(
                _workspaceEnvironmentStateDraft ?? "");
            panel.Add(_workspaceEnvironmentStateInput);
            _workspaceEnvironmentDurationInput = new TextField("Transition seconds");
            _workspaceEnvironmentDurationInput.SetValueWithoutNotify(
                _workspaceEnvironmentDurationDraft ?? "0");
            panel.Add(_workspaceEnvironmentDurationInput);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            _workspaceEnvironmentModeButton = new Button(() =>
            {
                _workspaceEnvironmentMode = (EnvironmentTransitionMode)(
                    ((int)_workspaceEnvironmentMode + 1) %
                    ((int)EnvironmentTransitionMode.Dissolve + 1));
                _workspaceEnvironmentModeButton.text =
                    "Transition: " + _workspaceEnvironmentMode;
            }) { text = "Transition: " + _workspaceEnvironmentMode };
            actions.Add(_workspaceEnvironmentModeButton);
            actions.Add(new Button(() =>
            {
                var runtime = sceneRuntime != null ? sceneRuntime.EnvironmentRuntime : null;
                if (runtime == null)
                {
                    SetWorkspaceEnvironmentFeedback("Environment runtime unavailable.");
                    return;
                }
                var stateId = runtime.Status.StateId;
                if (!string.IsNullOrWhiteSpace(stateId))
                    _workspaceEnvironmentStateInput.SetValueWithoutNotify(stateId);
            }) { text = "Use current" });
            _workspaceEnvironmentApplyButton = new Button(ApplyWorkspaceEnvironmentState)
                { text = "Apply environment" };
            actions.Add(_workspaceEnvironmentApplyButton);
            panel.Add(actions);

            _workspaceEnvironmentFeedback = new Label(
                "Only state IDs registered with the environment runtime are accepted.");
            _workspaceEnvironmentFeedback.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceEnvironmentFeedback);
            _nextWorkspaceEnvironmentRefresh = 0f;
            RefreshWorkspaceEnvironmentPanel();
        }

        private void SetWorkspaceEnvironmentFeedback(string message)
        {
            if (_workspaceEnvironmentFeedback != null)
                _workspaceEnvironmentFeedback.text = message;
        }

        private void RefreshWorkspaceEnvironmentPanel()
        {
            if (_workspaceEnvironmentStatus == null ||
                Time.unscaledTime < _nextWorkspaceEnvironmentRefresh)
                return;
            _nextWorkspaceEnvironmentRefresh = Time.unscaledTime + 2f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<VCR.Runtime.Scene.SingleCharacterSceneRuntime>();
            var allowed = sceneRuntime != null && !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            var runtime = sceneRuntime != null ? sceneRuntime.EnvironmentRuntime : null;
            _workspaceEnvironmentApplyButton?.SetEnabled(allowed && runtime != null);
            if (runtime == null)
            {
                _workspaceEnvironmentStatus.text =
                    "Environment provider unavailable (no environment runtime registered).";
                return;
            }

            try
            {
                var state = runtime.Status;
                var transition = runtime.TransitionStatus;
                _workspaceEnvironmentStatus.text =
                    "Environment: " + state.EnvironmentId +
                    " | State: " + state.StateId +
                    " | Space: " + runtime.SpaceMode +
                    " | Active: " + state.Active +
                    (transition.Active
                        ? "\nTransition: " + transition.Mode +
                          " (" + (transition.Progress * 100f).ToString("0.#",
                              CultureInfo.InvariantCulture) + "%)"
                        : "") +
                    (string.IsNullOrWhiteSpace(state.Error)
                        ? "" : "\nError: " + state.Error);
            }
            catch (Exception exception)
            {
                _workspaceEnvironmentStatus.text =
                    "Environment status failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void ApplyWorkspaceEnvironmentState()
        {
            if (sceneRuntime == null ||
                _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                SetWorkspaceEnvironmentFeedback("Environment edit unavailable while scene is busy.");
                return;
            }
            var runtime = sceneRuntime.EnvironmentRuntime;
            if (runtime == null)
            {
                SetWorkspaceEnvironmentFeedback("No environment runtime is configured.");
                return;
            }
            var stateId = _workspaceEnvironmentStateInput?.value?.Trim();
            if (string.IsNullOrWhiteSpace(stateId))
            {
                SetWorkspaceEnvironmentFeedback("Enter a non-empty environment state ID.");
                return;
            }
            var durationText = _workspaceEnvironmentDurationInput?.value?.Trim();
            var duration = 0f;
            if (!string.IsNullOrWhiteSpace(durationText) &&
                (!float.TryParse(durationText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out duration) ||
                 float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f))
            {
                SetWorkspaceEnvironmentFeedback(
                    "Transition seconds must be a finite non-negative number.");
                return;
            }

            try
            {
                var transition = new EnvironmentTransitionSpec(
                    _workspaceEnvironmentMode, duration);
                if (!runtime.SetState(stateId, transition, out var error))
                {
                    SetWorkspaceEnvironmentFeedback(
                        "Environment apply failed: " + (error ?? "unknown error"));
                    return;
                }
                SetWorkspaceEnvironmentFeedback("Environment state '" +
                    stateId + "' applied using " + _workspaceEnvironmentMode +
                    " (" + duration.ToString("0.###", CultureInfo.InvariantCulture) + "s).");
            }
            catch (Exception exception)
            {
                SetWorkspaceEnvironmentFeedback(
                    "Environment apply failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
            finally
            {
                _nextWorkspaceEnvironmentRefresh = 0f;
                RefreshWorkspaceEnvironmentPanel();
            }
        }
    }
}
