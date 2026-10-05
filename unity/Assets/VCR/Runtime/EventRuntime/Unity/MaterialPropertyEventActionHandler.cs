using System;
using UnityEngine;
using VCR.Runtime.Materials.Unity;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MaterialPropertyEventActionHandler :
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
            var supported =
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetInt,
                    StringComparison.Ordinal) ||
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetBool,
                    StringComparison.Ordinal) ||
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetColor,
                    StringComparison.Ordinal) ||
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetVector,
                    StringComparison.Ordinal) ||
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetTexture,
                    StringComparison.Ordinal) ||
                string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetShader,
                    StringComparison.Ordinal);

            if (!supported)
            {
                return false;
            }

            ResolveController();
            return controller != null;
        }
        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Material property action controller is unavailable or action type is unsupported.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    command.TargetId))
            {
                error =
                    "Material property action requires a material slot id in TargetId.";
                return false;
            }

            if (string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetShader,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(
                        command.Text))
                {
                    error =
                        "material.set_shader requires a shader id in command text.";
                    return false;
                }

                return controller.TryApplyShaderId(
                    command.TargetId,
                    command.Text,
                    out error);
            }

            if (string.IsNullOrWhiteSpace(
                    command.Name))
            {
                error =
                    "Material property action requires a shader property name.";
                return false;
            }

            if (string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetTexture,
                    StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(
                        command.Text))
                {
                    error =
                        "material.set_texture requires a texture id in command text.";
                    return false;
                }

                return controller.TrySetTextureId(
                    command.TargetId,
                    command.Name,
                    command.Text,
                    out error);
            }

            if (!command.HasValue)
            {
                error =
                    "Material property action requires a numeric value.";
                return false;
            }

            if (string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetInt,
                    StringComparison.Ordinal))
            {
                if (!TryGetInteger(
                        command.Value,
                        out var integer))
                {
                    error =
                        "material.set_int requires an integral Int32-range value.";
                    return false;
                }

                return controller.TrySetInt(
                    command.TargetId,
                    command.Name,
                    integer,
                    out error);
            }

            if (string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetBool,
                    StringComparison.Ordinal))
            {
                if (command.Value != 0.0 &&
                    command.Value != 1.0)
                {
                    error =
                        "material.set_bool requires exactly 0 or 1.";
                    return false;
                }

                return controller.TrySetBool(
                    command.TargetId,
                    command.Name,
                    command.Value == 1.0,
                    out error);
            }

            if (!TryGetFloat(
                    command.Value,
                    out var x) ||
                !TryGetFloat(
                    command.ValueY,
                    out var y) ||
                !TryGetFloat(
                    command.ValueZ,
                    out var z) ||
                !TryGetFloat(
                    command.ValueW,
                    out var w))
            {
                error =
                    "material color/vector actions require finite float-range X/Y/Z/W values.";
                return false;
            }

            if (string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialSetColor,
                    StringComparison.Ordinal))
            {
                return controller.TrySetColor(
                    command.TargetId,
                    command.Name,
                    new Color(
                        x,
                        y,
                        z,
                        w),
                    out error);
            }

            return controller.TrySetVector(
                command.TargetId,
                command.Name,
                new Vector4(
                    x,
                    y,
                    z,
                    w),
                out error);
        }

        private static bool TryGetInteger(
            double value,
            out int result)
        {
            result = 0;

            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < int.MinValue ||
                value > int.MaxValue ||
                Math.Abs(
                    value -
                    Math.Round(value)) >
                0.0000001)
            {
                return false;
            }

            result =
                (int)Math.Round(value);
            return true;
        }

        private static bool TryGetFloat(
            double value,
            out float result)
        {
            result = 0f;

            if (double.IsNaN(value) ||
                double.IsInfinity(value) ||
                value < -float.MaxValue ||
                value > float.MaxValue)
            {
                return false;
            }

            result = (float)value;
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
                    MaterialOverrideController>(
                    FindObjectsInactive.Exclude);
        }
    }
}
