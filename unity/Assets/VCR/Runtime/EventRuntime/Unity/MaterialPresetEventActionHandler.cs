using System;
using UnityEngine;
using VCR.Runtime.Materials.Unity;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MaterialPresetEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private MaterialOverrideController controller;

        [SerializeField]
        private MonoBehaviour presetResolverBehaviour;

        [SerializeField]
        private bool autoFindDependencies = true;

        [SerializeField, Min(0.1f)]
        private float autoFindRetrySeconds = 1f;

        private IMaterialPresetResolver _presetResolver;
        private float _nextResolveTime;

        private void Awake()
        {
            ResolveDependencies(
                force: true);
        }

        public void SetMaterialController(
            MaterialOverrideController value)
        {
            controller =
                value != null
                    ? value
                    : null;
            _nextResolveTime = 0f;
        }

        public void SetPresetResolver(
            MonoBehaviour value)
        {
            presetResolverBehaviour =
                value != null
                    ? value
                    : null;
            _presetResolver =
                presetResolverBehaviour != null
                    ? presetResolverBehaviour as
                        IMaterialPresetResolver
                    : null;
            _nextResolveTime = 0f;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.MaterialApplyPreset,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ResolveDependencies();

            return
                controller != null &&
                IsServiceAlive(_presetResolver);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(command))
            {
                error =
                    "Material preset action dependencies are unavailable.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    command.TargetId))
            {
                error =
                    "material.apply_preset requires a material slot id in TargetId.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    command.Text))
            {
                error =
                    "material.apply_preset requires a preset id in command text.";
                return false;
            }

            if (!_presetResolver.TryResolvePreset(
                    command.Text,
                    out var preset) ||
                preset == null)
            {
                error =
                    $"Material preset '{command.Text}' could not be resolved.";
                return false;
            }

            if (!controller.TryApplyPreset(
                    command.TargetId,
                    preset,
                    out var report,
                    out error))
            {
                return false;
            }

            if (!report.Compatible)
            {
                error =
                    report.Issues.Length > 0
                        ? report.Issues[0].Message
                        : "Material preset is incompatible.";
                return false;
            }

            return true;
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private void ResolveDependencies(
            bool force = false)
        {
            if (!IsServiceAlive(_presetResolver))
            {
                _presetResolver = null;
            }

            if (presetResolverBehaviour != null &&
                presetResolverBehaviour is
                    IMaterialPresetResolver configured)
            {
                _presetResolver =
                    configured;
            }

            if (!autoFindDependencies)
            {
                return;
            }

            if (controller != null &&
                IsServiceAlive(_presetResolver))
            {
                _nextResolveTime = 0f;
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

            if (controller == null)
            {
                controller =
                    FindFirstObjectByType<
                        MaterialOverrideController>(
                        FindObjectsInactive.Exclude);
            }

            if (IsServiceAlive(_presetResolver))
            {
                if (controller != null)
                {
                    _nextResolveTime = 0f;
                }

                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IMaterialPresetResolver resolver)
                {
                    presetResolverBehaviour =
                        behaviour;
                    _presetResolver =
                        resolver;

                    if (controller != null)
                    {
                        _nextResolveTime = 0f;
                    }

                    return;
                }
            }
        }
    }
}
