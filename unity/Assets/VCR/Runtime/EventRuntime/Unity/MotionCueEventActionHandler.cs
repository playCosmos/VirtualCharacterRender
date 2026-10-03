using System;
using UnityEngine;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MotionCueEventActionHandler :
        MonoBehaviour,
        IEventActionHandler
    {
        [SerializeField] private MonoBehaviour motionRuntimeBehaviour;
        [SerializeField] private bool autoFindMotionRuntime = true;

        private IMotionCueRuntime _runtime;

        private void Awake()
        {
            ResolveRuntime();
        }

        public void SetMotionRuntime(
            MonoBehaviour runtime)
        {
            motionRuntimeBehaviour =
                runtime;
            _runtime =
                runtime as IMotionCueRuntime;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            ResolveRuntime();

            if (_runtime == null ||
                (command.ActionType !=
                    EventActionTypes.MotionPlay &&
                 command.ActionType !=
                    EventActionTypes.MotionRelease))
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
                    "Motion cue runtime is unavailable or target does not match.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.MotionPlay)
            {
                if (string.IsNullOrWhiteSpace(
                        command.Text))
                {
                    error =
                        "motion.play requires a cue id in command text.";
                    return false;
                }

                return _runtime.TryPlayCue(
                    command.Text,
                    out error);
            }

            return _runtime.TryReleaseCue(
                command.Text,
                out error);
        }

        private void ResolveRuntime()
        {
            if (_runtime != null)
            {
                return;
            }

            if (motionRuntimeBehaviour is
                IMotionCueRuntime configured)
            {
                _runtime = configured;
                return;
            }

            if (!autoFindMotionRuntime)
            {
                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IMotionCueRuntime runtime)
                {
                    motionRuntimeBehaviour =
                        behaviour;
                    _runtime = runtime;
                    return;
                }
            }
        }
    }
}
