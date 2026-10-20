using System;
using UnityEngine;
using VCR.Runtime.Materials.Unity;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MaterialFloatEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private MaterialOverrideController controller;

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

        public void SetMaterialController(
            MaterialOverrideController value)
        {
            controller = value;
            _nextResolveTime = 0f;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetFloat,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ResolveController();

            if (controller == null)
            {
                return false;
            }
            
            return true;
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Material float action controller is unavailable.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    command.TargetId))
            {
                error =
                    "material.set_float requires a material slot id in TargetId.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    command.Name))
            {
                error =
                    "material.set_float requires a shader property name.";
                return false;
            }

            if (!command.HasValue ||
                double.IsNaN(command.Value) ||
                double.IsInfinity(command.Value) ||
                command.Value > float.MaxValue ||
                command.Value < -float.MaxValue)
            {
                error =
                    "material.set_float requires a finite float-range numeric value.";
                return false;
            }

            return controller.TrySetFloat(
                command.TargetId,
                command.Name,
                (float)command.Value,
                out error);
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
                    MaterialOverrideController>(
                    FindObjectsInactive.Exclude);

            if (controller != null)
            {
                _nextResolveTime = 0f;
            }
        }
    }
}
