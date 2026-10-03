using System;
using UnityEngine;
using VCR.Runtime.Scene;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class CameraFieldOfViewEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private string cameraId =
            "camera.primary";

        [SerializeField]
        private PrimaryCameraController controller;

        [SerializeField]
        private bool autoFindController = true;

        private void Awake()
        {
            ResolveController();
        }

        public void SetCameraController(
            PrimaryCameraController value,
            string id = "camera.primary")
        {
            controller = value;

            if (!string.IsNullOrWhiteSpace(id))
            {
                cameraId = id;
            }
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            ResolveController();

            if (controller == null ||
                !string.Equals(
                    command.ActionType,
                    EventActionTypes.CameraSetFieldOfView,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    cameraId,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Camera FOV action target is unavailable or does not match.";
                return false;
            }

            if (!command.HasValue ||
                double.IsNaN(command.Value) ||
                double.IsInfinity(command.Value))
            {
                error =
                    "camera.set_fov requires a finite numeric command value.";
                return false;
            }

            var settings =
                controller.Settings;
            settings.FieldOfView =
                (float)command.Value;

            controller.Configure(
                controller.TargetCamera,
                settings);

            return true;
        }

        private void ResolveController()
        {
            if (controller != null ||
                !autoFindController)
            {
                return;
            }

            controller =
                FindFirstObjectByType<
                    PrimaryCameraController>(
                    FindObjectsInactive.Exclude);
        }
    }
}
