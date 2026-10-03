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
                runtime;
            _runtime =
                runtime as IEnvironmentRuntime;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            ResolveRuntime();

            if (_runtime == null ||
                !string.Equals(
                    command.ActionType,
                    EventActionTypes.EnvironmentSetState,
                    StringComparison.Ordinal))
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

            return _runtime.SetState(
                stateId,
                out error);
        }

        private void ResolveRuntime()
        {
            if (_runtime != null)
            {
                return;
            }

            if (environmentRuntimeBehaviour is
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
