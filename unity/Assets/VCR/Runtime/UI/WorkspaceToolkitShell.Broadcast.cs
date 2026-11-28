using System;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Output;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    // Applies the existing rendering/broadcast target contract. These controls
    // configure capture dimensions and pacing, not a streaming/recording session.
    public sealed partial class WorkspaceToolkitShell
    {
        private Label _workspaceBroadcastStatus;
        private Label _workspaceBroadcastFeedback;
        private Button _workspaceBroadcast720p;
        private Button _workspaceBroadcast1080p;
        private BroadcastCaptureTarget _workspaceBroadcastTarget =
            BroadcastCaptureTarget.Recommended1080p60;
        private float _nextWorkspaceBroadcastRefresh;

        private void ResetWorkspaceBroadcastPanel()
        {
            _workspaceBroadcastStatus = null;
            _workspaceBroadcastFeedback = null;
            _workspaceBroadcast720p = null;
            _workspaceBroadcast1080p = null;
            _nextWorkspaceBroadcastRefresh = 0f;
            _workspaceBroadcastTarget = BroadcastCaptureTarget.Recommended1080p60;
        }

        private void BuildWorkspaceBroadcastPanel(VisualElement panel)
        {
            var header = new Label("Broadcast capture profile (not streaming)");
            header.style.marginTop = 9;
            panel.Add(header);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexWrap = Wrap.Wrap;
            _workspaceBroadcast720p = new Button(() =>
                ApplyWorkspaceBroadcastProfile(BroadcastCaptureTarget.Minimum720p60, "720p60"))
                { text = "1280×720 / 60" };
            _workspaceBroadcast1080p = new Button(() =>
                ApplyWorkspaceBroadcastProfile(BroadcastCaptureTarget.Recommended1080p60, "1080p60"))
                { text = "1920×1080 / 60" };
            buttons.Add(_workspaceBroadcast720p);
            buttons.Add(_workspaceBroadcast1080p);
            panel.Add(buttons);

            _workspaceBroadcastStatus = new Label("Capture readiness: unknown");
            _workspaceBroadcastStatus.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceBroadcastStatus);
            _workspaceBroadcastFeedback = new Label(
                "Selecting a profile configures render size, FPS and background execution; it does not start capture.");
            _workspaceBroadcastFeedback.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_workspaceBroadcastFeedback);
            RefreshWorkspaceBroadcastStatus();
        }

        private void ApplyWorkspaceBroadcastProfile(BroadcastCaptureTarget target, string label)
        {
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            if (sceneRuntime == null ||
                _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyBroadcastTarget(true, sceneRuntime.State))
            {
                SetWorkspaceBroadcastFeedback(label + " unavailable while runtime is busy or missing.");
                return;
            }

            try
            {
                if (!sceneRuntime.TryApplyBroadcastCaptureTarget(target, out var error))
                {
                    SetWorkspaceBroadcastFeedback(label + " apply failed: " +
                        (error ?? "runtime rejected the profile"));
                    return;
                }

                _workspaceBroadcastTarget = target;
                _renderInputsRuntime = null;
                SynchronizeRenderInputs();
                SetWorkspaceBroadcastFeedback(label +
                    " render target applied. Check capture readiness below before broadcasting.");
            }
            catch (Exception exception)
            {
                SetWorkspaceBroadcastFeedback(label + " apply failed: " + exception.Message);
                Debug.LogException(exception, this);
            }
            finally
            {
                _nextWorkspaceBroadcastRefresh = 0f;
                RefreshWorkspaceBroadcastStatus();
            }
        }

        private void SetWorkspaceBroadcastFeedback(string message)
        {
            if (_workspaceBroadcastFeedback != null)
                _workspaceBroadcastFeedback.text = message;
        }

        private void RefreshWorkspaceBroadcastStatus()
        {
            if (_workspaceBroadcastStatus == null ||
                Time.unscaledTime < _nextWorkspaceBroadcastRefresh)
                return;
            _nextWorkspaceBroadcastRefresh = Time.unscaledTime + 2f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();

            var allowed = sceneRuntime != null && !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyBroadcastTarget(true, sceneRuntime.State);
            _workspaceBroadcast720p?.SetEnabled(allowed);
            _workspaceBroadcast1080p?.SetEnabled(allowed);

            if (sceneRuntime == null || !sceneRuntime.TryCaptureRenderSettings(out var render))
            {
                _workspaceBroadcastStatus.text = "Capture readiness: scene renderer unavailable.";
                return;
            }

            try
            {
                var readiness = sceneRuntime.EvaluateBroadcastCaptureTarget(
                    _workspaceBroadcastTarget);
                _workspaceBroadcastStatus.text = "Render target: " +
                    render.Width + "×" + render.Height +
                    " @ " + render.TargetFrameRate + " FPS" +
                    " | Background: " + (render.RunInBackground ? "on" : "off") +
                    "\n" + _workspaceBroadcastTarget.Width + "×" +
                    _workspaceBroadcastTarget.Height + " readiness: " +
                    (readiness.Ready ? "ready" :
                        "not ready — " + (readiness.Message ??
                        readiness.Failure.ToString()));
            }
            catch (Exception exception)
            {
                _workspaceBroadcastStatus.text =
                    "Capture readiness unavailable: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }
    }
}
