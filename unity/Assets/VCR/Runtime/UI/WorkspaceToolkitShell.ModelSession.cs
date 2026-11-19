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
        private ModelSessionAdoptionService _modelSessionAdoptionService;
        private ModelSessionAdoptionPreview _reviewedModelAdoption;
        private float _adoptionReviewExpiresAt;
        private float _rollbackReviewExpiresAt;
        private float _keepReviewExpiresAt;
        private Label _modelSessionSnapshotStatus;

        private void BuildWorkspaceModelSessionSnapshot(VisualElement parent)
        {
            DisarmWorkspaceModelAdoption();
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
            actions.Add(new Button(ReviewWorkspaceModelSessionAdoption)
                { text = "Review remembered VRM adoption" });
            actions.Add(new Button(ApplyWorkspaceModelSessionAdoption)
                { text = "Adopt reviewed preference" });
            actions.Add(new Button(RollbackWorkspaceModelSessionAdoption)
                { text = "Rollback last adoption" });
            actions.Add(new Button(KeepWorkspaceModelSessionAdoption)
                { text = "Keep adopted preference" });
            parent.Add(actions);
            _modelSessionSnapshotStatus = new Label(
                "No checkpoint read. The last-successful VRM preference remains authoritative.");
            parent.Add(_modelSessionSnapshotStatus);
        }

        private void SaveWorkspaceModelSessionSnapshot()
        {
            DisarmWorkspaceModelAdoption();
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
            DisarmWorkspaceModelAdoption();
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

        private void DisarmWorkspaceModelAdoption()
        {
            _reviewedModelAdoption = null;
            _adoptionReviewExpiresAt = 0f;
            _rollbackReviewExpiresAt = 0f;
            _keepReviewExpiresAt = 0f;
        }

        private void ReviewWorkspaceModelSessionAdoption()
        {
            DisarmWorkspaceModelAdoption();
            if (_modelSessionSnapshotStatus == null)
                return;
            try
            {
                _modelSessionAdoptionService ??=
                    ModelSessionAdoptionService.CreateDefault();
                if (!_modelSessionAdoptionService.TryPreview(
                        out var preview, out var error))
                {
                    _modelSessionSnapshotStatus.text =
                        "Checkpoint adoption unavailable: " + error;
                    return;
                }
                _reviewedModelAdoption = preview;
                _adoptionReviewExpiresAt = Time.unscaledTime + 20f;
                _modelSessionSnapshotStatus.text =
                    "Review: remembered VRM " +
                    BaseNameOrNone(preview.PreviousPath) + " -> " +
                    BaseNameOrNone(preview.TargetPath) +
                    ". Click Adopt reviewed preference within 20 seconds. " +
                    "This affects next startup, not the active model.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Review unavailable: " + exception.GetType().Name;
            }
        }

        private void ApplyWorkspaceModelSessionAdoption()
        {
            if (_modelSessionSnapshotStatus == null)
                return;
            var reviewed = _reviewedModelAdoption;
            var withinReview = reviewed != null &&
                Time.unscaledTime <= _adoptionReviewExpiresAt;
            DisarmWorkspaceModelAdoption();
            if (!withinReview)
            {
                _modelSessionSnapshotStatus.text =
                    "Adoption requires a fresh review (20-second limit).";
                return;
            }
            try
            {
                _modelSessionAdoptionService ??=
                    ModelSessionAdoptionService.CreateDefault();
                if (!_modelSessionAdoptionService.TryAdopt(reviewed, out var error))
                {
                    _modelSessionSnapshotStatus.text =
                        "Adoption rejected: " + error;
                    return;
                }
                _modelSessionSnapshotStatus.text =
                    "Remembered VRM preference adopted for next startup: " +
                    BaseNameOrNone(reviewed.TargetPath) +
                    ". Active character unchanged. Rollback remains available.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Adoption unavailable: " + exception.GetType().Name;
            }
        }

        private void RollbackWorkspaceModelSessionAdoption()
        {
            if (_modelSessionSnapshotStatus == null)
                return;
            if (_rollbackReviewExpiresAt == 0f ||
                Time.unscaledTime > _rollbackReviewExpiresAt)
            {
                DisarmWorkspaceModelAdoption();
                _rollbackReviewExpiresAt = Time.unscaledTime + 20f;
                _modelSessionSnapshotStatus.text =
                    "Confirm rollback: click Rollback last adoption again " +
                    "within 20 seconds. Active model will not change.";
                return;
            }
            DisarmWorkspaceModelAdoption();
            try
            {
                _modelSessionAdoptionService ??=
                    ModelSessionAdoptionService.CreateDefault();
                if (!_modelSessionAdoptionService.TryRollback(out var error))
                {
                    _modelSessionSnapshotStatus.text =
                        "Rollback rejected: " + error;
                    return;
                }
                _modelSessionSnapshotStatus.text =
                    "Previous remembered preference restored. Active character unchanged.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Rollback unavailable: " + exception.GetType().Name;
            }
        }

        private void KeepWorkspaceModelSessionAdoption()
        {
            if (_modelSessionSnapshotStatus == null)
                return;
            if (_keepReviewExpiresAt == 0f ||
                Time.unscaledTime > _keepReviewExpiresAt)
            {
                DisarmWorkspaceModelAdoption();
                _keepReviewExpiresAt = Time.unscaledTime + 20f;
                _modelSessionSnapshotStatus.text =
                    "Confirm keep: click Keep adopted preference again within " +
                    "20 seconds to close the previous rollback opportunity.";
                return;
            }
            DisarmWorkspaceModelAdoption();
            try
            {
                _modelSessionAdoptionService ??=
                    ModelSessionAdoptionService.CreateDefault();
                if (!_modelSessionAdoptionService.TryKeepAdoptedPreference(out var error))
                {
                    _modelSessionSnapshotStatus.text =
                        "Keep rejected: " + error;
                    return;
                }
                _modelSessionSnapshotStatus.text =
                    "Adoption kept. Rollback journal closed; no model loaded.";
            }
            catch (Exception exception)
            {
                _modelSessionSnapshotStatus.text =
                    "Keep unavailable: " + exception.GetType().Name;
            }
        }

        private static string BaseNameOrNone(string path)
        {
            return string.IsNullOrEmpty(path)
                ? "<none>" : Path.GetFileName(path);
        }
    }
}
