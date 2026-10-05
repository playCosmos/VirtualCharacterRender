using System;
using UnityEngine;
using VCR.Runtime.Environment;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class EnvironmentStateEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField]
        private MonoBehaviour environmentRuntimeBehaviour;

        [SerializeField]
        private bool autoFindEnvironmentRuntime = true;

        private IEnvironmentRuntime _runtime;

        private void Awake()
        {
            ResolveRuntime();
        }

        public void SetEnvironmentRuntime(
            MonoBehaviour runtime)
        {
            environmentRuntimeBehaviour =
                runtime != null
                    ? runtime
                    : null;
            _runtime =
                environmentRuntimeBehaviour != null
                    ? environmentRuntimeBehaviour as
                        IEnvironmentRuntime
                    : null;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (!string.Equals(
                    command.ActionType,
                    EventActionTypes.EnvironmentSetState,
                    StringComparison.Ordinal))
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
                    _runtime.Status.EnvironmentId,
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
                    "Environment action target is unavailable or does not match.";
                return false;
            }

            var stateId =
                command.Text;

            if (string.IsNullOrWhiteSpace(
                    stateId))
            {
                error =
                    "environment.set_state requires a non-empty state id in command text.";
                return false;
            }

            var hasTransitionMetadata =
                !string.IsNullOrWhiteSpace(
                    command.Name) ||
                command.HasValue;

            if (!hasTransitionMetadata)
            {
                return _runtime.SetState(
                    stateId,
                    out error);
            }

            if (string.IsNullOrWhiteSpace(
                    command.Name) ||
                !Enum.TryParse<
                    EnvironmentTransitionMode>(
                        command.Name,
                        ignoreCase: true,
                        out var mode))
            {
                error =
                    "environment.set_state transition Name must be Cut, Fade, Crossfade, or Dissolve.";
                return false;
            }

            var duration =
                command.HasValue
                    ? command.Value
                    : 0.0;

            if (double.IsNaN(duration) ||
                double.IsInfinity(duration) ||
                duration < 0.0 ||
                duration > float.MaxValue)
            {
                error =
                    "environment.set_state transition duration must be a finite non-negative float-range value.";
                return false;
            }

            return _runtime.SetState(
                stateId,
                new EnvironmentTransitionSpec(
                    mode,
                    (float)duration),
                out error);
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

        private void ResolveRuntime()
        {
            if (IsServiceAlive(_runtime))
            {
                return;
            }

            _runtime = null;

            if (environmentRuntimeBehaviour != null &&
                environmentRuntimeBehaviour is
                    IEnvironmentRuntime configured)
            {
                _runtime = configured;
                return;
            }

            if (!autoFindEnvironmentRuntime)
            {
                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IEnvironmentRuntime runtime)
                {
                    environmentRuntimeBehaviour =
                        behaviour;
                    _runtime = runtime;
                    return;
                }
            }
        }
    }
}
