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

        private void Awake()
        {
            ResolveController();
        }

        public void SetMaterialController(
            MaterialOverrideController value)
        {
            controller = value;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            ResolveController();

            return
                controller != null &&
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetFloat,
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

        private void ResolveController()
        {
            if (controller != null ||
                !autoFindController)
            {
                return;
            }

            controller =
                FindFirstObjectByType<
                    MaterialOverrideController>(
                    FindObjectsInactive.Exclude);
        }
    }
}
