using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using VCR.Runtime.Application;
using VCR.Runtime.Scene;

namespace VCR.Runtime.UI
{
    public sealed partial class WorkspaceToolkitShell
    {
        private ModelSessionSnapshotStore _modelSessionSnapshotStore;
        private Label _modelSessionSnapshotStatus;

        private void BuildWorkspaceModelSessionSnapshot(VisualElement parent)
        {
            parent.Add(new Label("Model session checkpoint (manual, local only)"));
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.Add(new Button(SaveWorkspaceModelSessionSnapshot)
                { text = "Save session checkpoint" });
            actions.Add(new Button(InspectWorkspaceModelSessionSnapshot)
                { text = "Inspect checkpoint" });
            actions.Add(new Button(InspectWorkspaceModelSessionBackup)
                { text = "Inspect backup" });
            parent.Add(actions);
            _modelSessionSnapshotStatus = new Label(
                "No checkpoint read. The last-successful VRM preference remains authoritative.");
            parent.Add(_modelSessionSnapshotStatus);
        }

        private void SaveWorkspaceModelSessionSnapshot()
        {
            if (_modelSessionSnapshotStatus == null)
                return;
            if (sceneRuntime == null ||
                sceneRuntime.State != SceneRuntimeState.Ready &&
                sceneRuntime.State != SceneRuntimeState.CharacterReady)
            {
                _modelSessionSnapshotStatus.text =
                    "Cannot capture a session while scene state is unavailable or busy.";
                return;
            }

            var active = sceneRuntime.Status.HasCharacter
                ? sceneRuntime.CurrentCharacterPath : "";
            if (sceneRuntime.Status.HasCharacter &&
                string.IsNullOrWhiteSpace(active))
            {
                _modelSessionSnapshotStatus.text =
                    "Active model has no reliable file path. Checkpoint rejected.";
                return;
            }
            try
            {
                _modelSessionSnapshotStore ??= ModelSessionSnapshotStore.CreateDefault();
                if (!_modelSessionSnapshotStore.TrySaveExplicitCheckpoint(
                        ModelSessionController.RememberedPath, active, out var error))
                {
                    _modelSessionSnapshotStatus.text =
                        "Checkpoint rejected: " + error;
                    return;
                }
                _modelSessionSnapshotStatus.text =
                    "Checkpoint saved. This does not change the active model or auto-restore.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Checkpoint unavailable: " + exception.GetType().Name;
            }
        }

        private void InspectWorkspaceModelSessionSnapshot()
        {
            InspectWorkspaceModelSessionFile(false);
        }

        private void InspectWorkspaceModelSessionBackup()
        {
            InspectWorkspaceModelSessionFile(true);
        }

        private void InspectWorkspaceModelSessionFile(bool backup)
        {
            if (_modelSessionSnapshotStatus == null)
                return;
            try
            {
                _modelSessionSnapshotStore ??= ModelSessionSnapshotStore.CreateDefault();
                ModelSessionSnapshot snapshot;
                string error;
                var read = backup
                    ? _modelSessionSnapshotStore.TryReadBackup(
                        out snapshot, out error)
                    : _modelSessionSnapshotStore.TryRead(
                        out snapshot, out error);
                if (!read)
                {
                    _modelSessionSnapshotStatus.text =
                        (backup ? "Backup" : "Checkpoint") +
                        " unavailable: " + error;
                    return;
                }
                if (snapshot == null)
                {
                    _modelSessionSnapshotStatus.text =
                        "No checkpoint on disk. No session state was restored.";
                    return;
                }
                // Do not copy raw personal paths into GUI status or logs.
                _modelSessionSnapshotStatus.text =
                    (backup ? "Backup" : "Checkpoint") +
                    " (v" + snapshot.Version + ") " +
                    snapshot.CapturedUtc +
                    " | active: " + BaseNameOrNone(snapshot.ActivePath) +
                    " | remembered: " + BaseNameOrNone(snapshot.RememberedPath) +
                    ". Read-only: not a model load or automatic restore.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Session file inspect failed: " + exception.GetType().Name;
            }
        }

        private static string BaseNameOrNone(string path)
        {
            return string.IsNullOrEmpty(path)
                ? "<none>" : Path.GetFileName(path);
        }
    }
}
