using System;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    // P1 owns one live scene, not an OBS multi-scene switcher. This surface
    // reports and controls that actual scene without cloning its lifecycle.
    public sealed partial class WorkspaceToolkitShell
    {
        private Label _workspaceSceneSummary;
        private Label _workspaceSceneFeedback;
        private Button _workspaceSceneSuspendButton;
        private Button _workspaceSceneResumeButton;
        private float _nextWorkspaceSceneRefresh;

        private void ResetWorkspaceScenesPanel()
        {
            _workspaceSceneSummary = null;
            _workspaceSceneFeedback = null;
            _workspaceSceneSuspendButton = null;
            _workspaceSceneResumeButton = null;
            _nextWorkspaceSceneRefresh = 0f;
        }

        private void BuildWorkspaceScenesPanel(VisualElement panel)
        {
            panel.Add(new Label("Active scene (single-scene runtime)"));
            _workspaceSceneSummary = new Label("Waiting for scene runtime...");
            _workspaceSceneSummary.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceSceneSummary);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            _workspaceSceneSuspendButton = new Button(() => ChangeWorkspaceSceneSuspension(true))
                { text = "Suspend scene" };
            _workspaceSceneResumeButton = new Button(() => ChangeWorkspaceSceneSuspension(false))
                { text = "Resume scene" };
            actions.Add(_workspaceSceneSuspendButton);
            actions.Add(_workspaceSceneResumeButton);
            panel.Add(actions);
            _workspaceSceneFeedback = new Label("Scene suspend pauses output; resume restores runtime resources.");
            _workspaceSceneFeedback.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceSceneFeedback);
            RefreshWorkspaceScenesPanel();
        }

        private void RefreshWorkspaceScenesPanel()
        {
            if (_workspaceSceneSummary == null ||
                Time.unscaledTime < _nextWorkspaceSceneRefresh)
                return;
            _nextWorkspaceSceneRefresh = Time.unscaledTime + 0.5f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();

            if (sceneRuntime == null)
            {
                _workspaceSceneSummary.text = "No active scene runtime.";
                _workspaceSceneSuspendButton?.SetEnabled(false);
                _workspaceSceneResumeButton?.SetEnabled(false);
                return;
            }

            var status = sceneRuntime.Status;
            var file = sceneRuntime.CurrentCharacterPath;
            var model = string.IsNullOrWhiteSpace(file)
                ? "<none>" : System.IO.Path.GetFileName(file);
            _workspaceSceneSummary.text = "State: " + status.State +
                "\nModel: " + model +
                "\nLoaded: " + status.HasCharacter +
                (string.IsNullOrWhiteSpace(status.LastError)
                    ? "" : "\nError: " + status.LastError);

            var bootstrap = FindFirstObjectByType<ApplicationRuntimeBootstrap>();
            var canOperate = !_characterActionPending &&
                (UnityEngine.Application.isEditor ||
                 (bootstrap != null && bootstrap.IsStarted));
            _workspaceSceneSuspendButton?.SetEnabled(canOperate &&
                (status.State == SceneRuntimeState.Ready ||
                 status.State == SceneRuntimeState.CharacterReady));
            _workspaceSceneResumeButton?.SetEnabled(canOperate &&
                status.State == SceneRuntimeState.Suspended);
        }

        private void ChangeWorkspaceSceneSuspension(bool suspend)
        {
            if (sceneRuntime == null)
            {
                if (_workspaceSceneFeedback != null)
                    _workspaceSceneFeedback.text = "Scene runtime unavailable.";
                return;
            }
            try
            {
                var bootstrap = FindFirstObjectByType<ApplicationRuntimeBootstrap>();
                if (!UnityEngine.Application.isEditor &&
                    (bootstrap == null || !bootstrap.IsStarted))
                {
                    _workspaceSceneFeedback.text = "Application startup is incomplete.";
                    return;
                }
                var state = sceneRuntime.State;
                if (_characterActionPending ||
                    (suspend && state != SceneRuntimeState.Ready &&
                                state != SceneRuntimeState.CharacterReady) ||
                    (!suspend && state != SceneRuntimeState.Suspended))
                {
                    _workspaceSceneFeedback.text = "Scene cannot " +
                        (suspend ? "suspend" : "resume") + " in state " + state + ".";
                    return;
                }

                var success = bootstrap != null && bootstrap.IsStarted
                    ? (suspend ? bootstrap.Suspend() : bootstrap.Resume())
                    : (suspend ? sceneRuntime.Suspend() : sceneRuntime.Resume());
                _workspaceSceneFeedback.text = success
                    ? (suspend ? "Scene suspended." : "Scene resumed.")
                    : "Scene " + (suspend ? "suspend" : "resume") + " failed: " +
                      (sceneRuntime.Status.LastError ?? "runtime rejected the request");
            }
            catch (Exception exception)
            {
                if (_workspaceSceneFeedback != null)
                    _workspaceSceneFeedback.text = "Scene action failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
            _nextWorkspaceSceneRefresh = 0f;
            RefreshWorkspaceScenesPanel();
        }
    }
}
