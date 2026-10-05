using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Tracking.Mixing;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class MotionCueEventActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe
    {
        [SerializeField] private MonoBehaviour motionRuntimeBehaviour;
        [SerializeField] private MonoBehaviour[] additionalMotionRuntimeBehaviours =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool autoFindMotionRuntime = true;
        [SerializeField, Min(0.1f)] private float autoFindRetrySeconds = 1f;

        private readonly List<IMotionCueRuntime>
            _runtimes =
                new();

        private float _nextRuntimeResolveTime;

        private void Awake()
        {
            RebuildRuntimes(
                force: true);
        }

        public void SetMotionRuntime(
            MonoBehaviour runtime)
        {
            motionRuntimeBehaviour =
                runtime;
            additionalMotionRuntimeBehaviours =
                Array.Empty<MonoBehaviour>();
            RebuildRuntimes(
                force: true);
        }

        public void SetMotionRuntimes(
            params MonoBehaviour[] runtimes)
        {
            runtimes ??=
                Array.Empty<MonoBehaviour>();

            motionRuntimeBehaviour =
                runtimes.Length > 0
                    ? runtimes[0]
                    : null;

            if (runtimes.Length <= 1)
            {
                additionalMotionRuntimeBehaviours =
                    Array.Empty<MonoBehaviour>();
            }
            else
            {
                additionalMotionRuntimeBehaviours =
                    new MonoBehaviour[
                        runtimes.Length - 1];

                Array.Copy(
                    runtimes,
                    1,
                    additionalMotionRuntimeBehaviours,
                    0,
                    additionalMotionRuntimeBehaviours.Length);
            }

            RebuildRuntimes(
                force: true);
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (command.ActionType !=
                    EventActionTypes.MotionPlay &&
                command.ActionType !=
                    EventActionTypes.MotionRelease)
            {
                return false;
            }

            ResolveRuntimes();

            return TryResolveRuntime(
                command,
                out _,
                out var count) &&
                count > 0;
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (command.ActionType !=
                    EventActionTypes.MotionPlay &&
                command.ActionType !=
                    EventActionTypes.MotionRelease)
            {
                error =
                    "Motion cue action type is unsupported.";
                return false;
            }

            ResolveRuntimes();

            if (command.ActionType ==
                    EventActionTypes.MotionPlay &&
                string.IsNullOrWhiteSpace(
                    command.Text))
            {
                error =
                    "motion.play requires a cue id in command text.";
                return false;
            }

            if (!TryResolveRuntime(
                    command,
                    out var runtime,
                    out var count) ||
                count == 0)
            {
                error =
                    string.IsNullOrWhiteSpace(
                        command.TargetId)
                        ? $"No motion cue runtime owns cue '{command.Text ?? "<none>"}'."
                        : $"Motion cue runtime '{command.TargetId}' is unavailable.";
                return false;
            }

            if (count > 1)
            {
                error =
                    string.IsNullOrWhiteSpace(
                        command.TargetId)
                        ? $"Multiple motion cue runtimes own cue '{command.Text ?? "<none>"}'. Specify TargetId."
                        : $"Multiple motion cue runtimes share runtime id '{command.TargetId}'.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.MotionPlay)
            {
                return runtime.TryPlayCue(
                    command.Text,
                    out error);
            }

            return runtime.TryReleaseCue(
                command.Text,
                out error);
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            if (command.ActionType !=
                    EventActionTypes.MotionPlay &&
                command.ActionType !=
                    EventActionTypes.MotionRelease)
            {
                return false;
            }

            ResolveRuntimes();

            return TryResolveRuntime(
                command,
                out _,
                out var count) &&
                count == 1;
        }

        public bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (!CanTrackCompletion(
                    command))
            {
                error =
                    "Motion action completion cannot be tracked because its runtime is unavailable or ambiguous.";
                return false;
            }

            TryResolveRuntime(
                command,
                out var runtime,
                out _);

            if (command.ActionType ==
                EventActionTypes.MotionRelease)
            {
                complete = true;
                return true;
            }

            complete =
                !runtime.Status.Playing ||
                !string.Equals(
                    runtime.Status.CueId,
                    command.Text,
                    StringComparison.Ordinal);
            return true;
        }

        private void ResolveRuntimes()
        {
            var staleRuntimeFound = false;

            if (_runtimes.Count > 0)
            {
                var valid = true;

                foreach (var runtime in
                         _runtimes)
                {
                    if (runtime is not
                            MonoBehaviour behaviour ||
                        behaviour == null)
                    {
                        valid = false;
                        staleRuntimeFound = true;
                        break;
                    }
                }

                if (valid)
                {
                    return;
                }
            }

            RebuildRuntimes(
                force: staleRuntimeFound);
        }

        private void RebuildRuntimes(
            bool force = false)
        {
            _runtimes.Clear();

            AddRuntime(
                motionRuntimeBehaviour);

            foreach (var behaviour in
                     additionalMotionRuntimeBehaviours ??
                     Array.Empty<MonoBehaviour>())
            {
                AddRuntime(
                    behaviour);
            }

            if (!autoFindMotionRuntime)
            {
                return;
            }

            var now =
                Time.unscaledTime;

            if (!force &&
                now < _nextRuntimeResolveTime)
            {
                return;
            }

            _nextRuntimeResolveTime =
                now +
                Mathf.Max(
                    0.1f,
                    autoFindRetrySeconds);

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                AddRuntime(
                    behaviour);
            }

            if (_runtimes.Count > 0)
            {
                _nextRuntimeResolveTime = 0f;
            }
        }

        private void AddRuntime(
            MonoBehaviour behaviour)
        {
            if (behaviour == null ||
                behaviour is not
                    IMotionCueRuntime runtime ||
                _runtimes.Contains(
                    runtime))
            {
                return;
            }

            _runtimes.Add(
                runtime);
        }

        private bool TryResolveRuntime(
            EventActionCommand command,
            out IMotionCueRuntime runtime,
            out int count)
        {
            runtime = null;
            count = 0;

            foreach (var candidate in
                     _runtimes)
            {
                if (!Matches(
                        candidate,
                        command))
                {
                    continue;
                }

                count++;

                if (runtime == null)
                {
                    runtime =
                        candidate;
                }
            }

            return count > 0;
        }

        private bool Matches(
            IMotionCueRuntime runtime,
            EventActionCommand command)
        {
            if (runtime == null ||
                runtime is UnityEngine.Object unityObject &&
                unityObject == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(
                    command.TargetId))
            {
                return string.Equals(
                    command.TargetId,
                    runtime.Status.RuntimeId,
                    StringComparison.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(
                    command.Text))
            {
                foreach (var cueId in
                         runtime.CueIds)
                {
                    if (string.Equals(
                            cueId,
                            command.Text,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            return
                command.ActionType ==
                    EventActionTypes.MotionRelease &&
                runtime.Status.Playing;
        }
    }
}
