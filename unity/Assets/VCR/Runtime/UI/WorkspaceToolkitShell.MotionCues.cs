using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Scene;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.UI
{
    // Cue sources can disable themselves on release, so discovery includes
    // disabled components on active objects, but never another loaded VRM.
    public sealed partial class WorkspaceToolkitShell
    {
        private const int WorkspaceMotionCueVisibleLimit = 48;
        private TextField _workspaceCueRuntimeInput;
        private TextField _workspaceCueIdInput;
        private Label _workspaceCueStatus;
        private Label _workspaceCueFeedback;
        private VisualElement _workspaceCueList;
        private Button _workspaceCuePlayButton;
        private Button _workspaceCueReleaseButton;
        private string _workspaceCueRuntimeDraft;
        private string _workspaceCueIdDraft;
        private string _workspaceCueListSignature;
        private float _nextWorkspaceCueRefresh;

        private void ResetWorkspaceMotionCues()
        {
            _workspaceCueRuntimeInput = null;
            _workspaceCueIdInput = null;
            _workspaceCueStatus = null;
            _workspaceCueFeedback = null;
            _workspaceCueList = null;
            _workspaceCuePlayButton = null;
            _workspaceCueReleaseButton = null;
            _workspaceCueRuntimeDraft = null;
            _workspaceCueIdDraft = null;
            _workspaceCueListSignature = null;
            _nextWorkspaceCueRefresh = 0f;
        }

        private void CaptureWorkspaceCueDrafts()
        {
            if (_workspaceCueRuntimeInput != null)
                _workspaceCueRuntimeDraft = _workspaceCueRuntimeInput.value;
            if (_workspaceCueIdInput != null)
                _workspaceCueIdDraft = _workspaceCueIdInput.value;
        }

        private void ClearWorkspaceCueSelection()
        {
            _workspaceCueRuntimeDraft = "";
            _workspaceCueIdDraft = "";
            _workspaceCueRuntimeInput?.SetValueWithoutNotify("");
            _workspaceCueIdInput?.SetValueWithoutNotify("");
            _workspaceCueListSignature = null;
        }

        private void BuildWorkspaceMotionCues(VisualElement container)
        {
            _workspaceCueListSignature = null;
            _nextWorkspaceCueRefresh = 0f;
            var heading = new Label("Motion cue playback");
            heading.style.marginTop = 10;
            container.Add(heading);
            _workspaceCueStatus = new Label("Looking for motion cue runtimes...");
            _workspaceCueStatus.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_workspaceCueStatus);
            _workspaceCueList = new VisualElement();
            container.Add(_workspaceCueList);
            _workspaceCueRuntimeInput = new TextField("Cue runtime ID");
            _workspaceCueRuntimeInput.SetValueWithoutNotify(_workspaceCueRuntimeDraft ?? "");
            container.Add(_workspaceCueRuntimeInput);
            _workspaceCueIdInput = new TextField("Cue ID");
            _workspaceCueIdInput.SetValueWithoutNotify(_workspaceCueIdDraft ?? "");
            container.Add(_workspaceCueIdInput);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            _workspaceCuePlayButton = new Button(() => ExecuteWorkspaceCue(false))
                { text = "Play cue" };
            _workspaceCueReleaseButton = new Button(() => ExecuteWorkspaceCue(true))
                { text = "Release cue" };
            row.Add(_workspaceCuePlayButton);
            row.Add(_workspaceCueReleaseButton);
            container.Add(row);
            _workspaceCueFeedback = new Label(
                "Select an available runtime and cue. A released source can be played again.");
            _workspaceCueFeedback.style.whiteSpace = WhiteSpace.Normal;
            container.Add(_workspaceCueFeedback);
            RefreshWorkspaceMotionCues();
        }

        private bool TryResolveWorkspaceCue(
            string runtimeId, string cueId, bool release,
            out IMotionCueRuntime selected, out string message)
        {
            selected = null;
            message = null;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            if (character == null)
            {
                message = "No active VRM.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(runtimeId))
            {
                message = "Select a specific cue runtime ID.";
                return false;
            }
            var count = 0;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || !behaviour.gameObject.activeInHierarchy ||
                    !(behaviour is IMotionCueRuntime candidate)) continue;
                var owner = behaviour.GetComponentInParent<UniVRM10.Vrm10Instance>();
                if (owner != null && owner != character) continue;
                MotionCueStatus status;
                try { status = candidate.Status; }
                catch (Exception ex)
                {
                    message = "Cue status lookup failed: " + ex.Message;
                    return false;
                }
                if (!string.Equals(status.RuntimeId, runtimeId, StringComparison.Ordinal))
                    continue;
                count++;
                if (count > 1) break;
                selected = candidate;
            }
            if (count != 1)
            {
                selected = null;
                message = count == 0 ? "Cue runtime ID not available for this VRM." :
                    "Multiple cue runtimes share that runtime ID; operation blocked.";
                return false;
            }
            try
            {
                var ids = selected.CueIds;
                if (!release)
                {
                    if (string.IsNullOrWhiteSpace(cueId))
                    {
                        message = "Enter a cue ID to play.";
                        return false;
                    }
                    var known = false;
                    if (ids != null)
                        foreach (var id in ids)
                            if (string.Equals(id, cueId, StringComparison.Ordinal))
                            { known = true; break; }
                    if (!known)
                    {
                        message = "This runtime has no registered cue with that ID.";
                        return false;
                    }
                }
                else
                {
                    var active = selected.Status;
                    if (!active.Playing)
                    {
                        message = "The selected runtime is not playing a cue.";
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(cueId) &&
                        !string.Equals(cueId, active.CueId, StringComparison.Ordinal))
                    {
                        message = "The selected cue does not match the active cue (" +
                            active.CueId + ").";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                message = "Cue validation failed: " + ex.Message;
                selected = null;
                return false;
            }
            return true;
        }

        private void RefreshWorkspaceMotionCues()
        {
            if (_workspaceCueStatus == null ||
                Time.unscaledTime < _nextWorkspaceCueRefresh) return;
            _nextWorkspaceCueRefresh = Time.unscaledTime + 1f;
            if (sceneRuntime == null)
                sceneRuntime = FindFirstObjectByType<SingleCharacterSceneRuntime>();
            var character = sceneRuntime != null ? sceneRuntime.CurrentCharacter : null;
            var allowed = character != null && !_characterActionPending &&
                ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State);
            _workspaceCuePlayButton?.SetEnabled(allowed);
            _workspaceCueReleaseButton?.SetEnabled(allowed);
            if (!allowed)
            {
                _workspaceCueStatus.text = "Cue controls unavailable: no active/ready VRM.";
                _workspaceCueList?.Clear();
                _workspaceCueListSignature = null;
                return;
            }
            try
            {
                var signature = new StringBuilder();
                var entries = new System.Collections.Generic.List<(string RuntimeId, string CueId)>();
                var visible = 0;
                var total = 0;
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (behaviour == null || !behaviour.gameObject.activeInHierarchy ||
                        !(behaviour is IMotionCueRuntime runtime)) continue;
                    var owner = behaviour.GetComponentInParent<UniVRM10.Vrm10Instance>();
                    if (owner != null && owner != character) continue;
                    var status = runtime.Status;
                    var ids = runtime.CueIds;
                    total++;
                    signature.Append(behaviour.GetInstanceID()).Append('|')
                        .Append(status.RuntimeId).Append('|')
                        .Append(status.Playing).Append('|').Append(status.CueId);
                    if (ids == null) continue;
                    foreach (var id in ids)
                    {
                        signature.Append('|').Append(id);
                        if (visible++ >= WorkspaceMotionCueVisibleLimit) continue;
                        entries.Add((status.RuntimeId, id));
                    }
                }
                _workspaceCueStatus.text = total == 0 ?
                    "No motion cue runtimes registered for this VRM." :
                    "Cue runtime(s): " + total +
                    (visible > WorkspaceMotionCueVisibleLimit
                        ? " | Showing first " + WorkspaceMotionCueVisibleLimit +
                          " cues; other IDs can be entered."
                        : "");
                var value = signature.ToString();
                if (value == _workspaceCueListSignature) return;
                _workspaceCueListSignature = value;
                _workspaceCueList.Clear();
                foreach (var entry in entries)
                {
                    var runtimeId = entry.RuntimeId;
                    var cueId = entry.CueId;
                    _workspaceCueList.Add(new Button(() =>
                    {
                        _workspaceCueRuntimeInput.SetValueWithoutNotify(runtimeId);
                        _workspaceCueIdInput.SetValueWithoutNotify(cueId);
                    }) { text = runtimeId + " / " + cueId });
                }
                if (entries.Count == 0)
                    _workspaceCueList.Add(new Label("No cue IDs found; check cue assets."));
            }
            catch (Exception ex)
            {
                _workspaceCueStatus.text = "Cue discovery failed: " + ex.Message;
                Debug.LogException(ex, this);
            }
        }

        private void ExecuteWorkspaceCue(bool release)
        {
            if (sceneRuntime == null || sceneRuntime.CurrentCharacter == null ||
                _characterActionPending ||
                !ApplicationUiActionPolicy.CanApplyRuntimeSettings(true, sceneRuntime.State))
            {
                ReportWorkspaceCue("Cue action unavailable while scene is missing/busy.");
                return;
            }
            // The actions panel may not have ticked since the active character
            // was replaced. Recheck against the last observed character ID.
            if (_workspaceMotionObservedCharacter != int.MinValue &&
                sceneRuntime.CurrentCharacter.GetInstanceID() !=
                _workspaceMotionObservedCharacter)
            {
                _nextWorkspaceMotionRefresh = 0f;
                RefreshWorkspaceMotionPanel();
                ReportWorkspaceCue("Active VRM changed. Select a cue again.");
                return;
            }
            var runtimeId = _workspaceCueRuntimeInput?.value?.Trim();
            var cueId = _workspaceCueIdInput?.value?.Trim();
            if (!TryResolveWorkspaceCue(runtimeId, cueId, release,
                    out var runtime, out var reason))
            {
                ReportWorkspaceCue(reason ?? "Cue selection unavailable.");
                return;
            }
            try
            {
                var success = release
                    ? runtime.TryReleaseCue(cueId, out reason)
                    : runtime.TryPlayCue(cueId, out reason);
                ReportWorkspaceCue(success
                    ? (release ? "Released " : "Playing ") + runtimeId +
                      " / " + cueId
                    : "Cue action failed: " + (reason ?? "unknown error"));
            }
            catch (Exception ex)
            {
                ReportWorkspaceCue("Cue action failed: " + ex.Message);
                Debug.LogException(ex, this);
            }
            _workspaceCueListSignature = null;
            _nextWorkspaceCueRefresh = 0f;
            RefreshWorkspaceMotionCues();
        }

        private void ReportWorkspaceCue(string message)
        {
            if (_workspaceCueFeedback != null)
                _workspaceCueFeedback.text = message;
        }
    }
}
