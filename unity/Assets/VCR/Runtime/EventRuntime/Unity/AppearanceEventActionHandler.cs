using System;
using UnityEngine;
using VCR.Runtime.Appearance;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class AppearanceEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField] private MonoBehaviour appearanceRuntimeBehaviour;
        [SerializeField] private bool autoFindAppearanceRuntime = true;
        [SerializeField, Min(0.1f)] private float autoFindRetrySeconds = 1f;

        private IAppearanceRuntime _runtime;
        private float _nextResolveTime;

        private void Awake()
        {
            ResolveRuntime(
                force: true);
        }

        public void SetAppearanceRuntime(
            MonoBehaviour runtime)
        {
            appearanceRuntimeBehaviour =
                runtime != null
                    ? runtime
                    : null;
            _runtime =
                appearanceRuntimeBehaviour != null
                    ? appearanceRuntimeBehaviour as
                        IAppearanceRuntime
                    : null;
            _nextResolveTime = 0f;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (string.IsNullOrWhiteSpace(
                    command.ActionType))
            {
                return false;
            }

            var supported =
                command.ActionType ==
                    EventActionTypes.AppearanceSetPreset ||
                command.ActionType ==
                    EventActionTypes.AppearanceSetOutfit ||
                command.ActionType ==
                    EventActionTypes.AppearanceSetAccessory ||
                command.ActionType ==
                    EventActionTypes.AppearanceClearAccessory ||
                command.ActionType ==
                    EventActionTypes.AppearanceRestoreDefault ||
                command.ActionType ==
                    EventActionTypes.AppearanceCancelTransition;

            if (!supported)
            {
                return false;
            }

            ResolveRuntime();

            if (!IsServiceAlive(_runtime))
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    _runtime.Status.RuntimeId,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;
            ResolveRuntime();

            if (!CanHandle(command))
            {
                error =
                    "Appearance runtime is unavailable or target does not match.";
                return false;
            }

            switch (command.ActionType)
            {
                case EventActionTypes.AppearanceSetPreset:
                    if (string.IsNullOrWhiteSpace(
                            command.Text))
                    {
                        error =
                            "appearance.set_preset requires a preset id in command text.";
                        return false;
                    }

                    return _runtime.SetPreset(
                        command.Text,
                        command.Name,
                        out error);

                case EventActionTypes.AppearanceSetOutfit:
                    if (string.IsNullOrWhiteSpace(
                            command.Text))
                    {
                        error =
                            "appearance.set_outfit requires an outfit id in command text.";
                        return false;
                    }

                    return _runtime.SetOutfit(
                        command.Text,
                        command.Name,
                        out error);

                case EventActionTypes.AppearanceSetAccessory:
                    if (string.IsNullOrWhiteSpace(
                            command.Name) ||
                        string.IsNullOrWhiteSpace(
                            command.Text))
                    {
                        error =
                            "appearance.set_accessory requires slot id in Name and accessory id in Text.";
                        return false;
                    }

                    return _runtime.SetAccessory(
                        command.Name,
                        command.Text,
                        transitionId: null,
                        out error);

                case EventActionTypes.AppearanceClearAccessory:
                    if (string.IsNullOrWhiteSpace(
                            command.Name))
                    {
                        error =
                            "appearance.clear_accessory requires slot id in Name.";
                        return false;
                    }

                    return _runtime.ClearAccessory(
                        command.Name,
                        transitionId: null,
                        out error);

                case EventActionTypes.AppearanceRestoreDefault:
                    return _runtime.RestoreDefault(
                        command.Text,
                        out error);

                case EventActionTypes.AppearanceCancelTransition:
                    return _runtime.CancelTransition(
                        out error);

                default:
                    error =
                        $"Unsupported appearance action '{command.ActionType}'.";
                    return false;
            }
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

        private void ResolveRuntime(
            bool force = false)
        {
            if (IsServiceAlive(_runtime))
            {
                return;
            }

            _runtime = null;

            if (appearanceRuntimeBehaviour != null &&
                appearanceRuntimeBehaviour is
                    IAppearanceRuntime configured)
            {
                _runtime = configured;
                _nextResolveTime = 0f;
                return;
            }

            if (!autoFindAppearanceRuntime)
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

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IAppearanceRuntime runtime)
                {
                    appearanceRuntimeBehaviour =
                        behaviour;
                    _runtime = runtime;
                    _nextResolveTime = 0f;
                    return;
                }
            }
        }
    }
}
