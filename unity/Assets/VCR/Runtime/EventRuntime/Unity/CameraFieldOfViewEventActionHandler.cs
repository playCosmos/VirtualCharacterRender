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

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        private float _nextResolveTime;

        private void Awake()
        {
            ResolveController(
                force: true);
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
            _nextResolveTime = 0f;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.CameraSetFieldOfView,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ResolveController();

            if (controller == null)
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

        private void ResolveController(
            bool force = false)
        {
            if (controller != null ||
                !autoFindController)
            {
                return;
            }

            var now =
                Time.unscaledTime;

            if (!force &&
                now < _nextResolveTime)
            {
                return;
            }

            _nextResolveTime =
                now +
                Mathf.Max(
                    0.1f,
                    autoFindRetrySeconds);

            controller =
                FindFirstObjectByType<
                    PrimaryCameraController>(
                    FindObjectsInactive.Exclude);

            if (controller != null)
            {
                _nextResolveTime = 0f;
            }
        }
    }
}
