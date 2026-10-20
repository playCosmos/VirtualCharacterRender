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
                out var count,
                out _) &&
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
                    out var count,
                    out var resolveError) ||
                count == 0)
            {
                error =
                    resolveError ??
                    (string.IsNullOrWhiteSpace(
                        command.TargetId)
                        ? $"No motion cue runtime owns cue '{command.Text ?? "<none>"}'."
                        : $"Motion cue runtime '{command.TargetId}' is unavailable.");
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

            try
            {
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
            catch (Exception exception)
            {
                error =
                    "Motion cue runtime execution failed: " +
                    exception.Message;
                return false;
            }
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
                out var count,
                out _) &&
                count == 1;
        }

        public bool TryIsComplete(
            EventActionCommand command,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (command.ActionType !=
                    EventActionTypes.MotionPlay &&
                command.ActionType !=
                    EventActionTypes.MotionRelease)
            {
                error =
                    "Motion action completion cannot be tracked for this action type.";
                return false;
            }

            ResolveRuntimes();

            if (!TryResolveRuntime(
                    command,
                    out var runtime,
                    out var count,
                    out var resolveError) ||
                count != 1)
            {
                error =
                    resolveError ??
                    "Motion action completion cannot be tracked because its runtime is unavailable or ambiguous.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.MotionRelease)
            {
                complete = true;
                return true;
            }

            try
            {
                var status =
                    runtime.Status;

                complete =
                    !status.Playing ||
                    !string.Equals(
                        status.CueId,
                        command.Text,
                        StringComparison.Ordinal);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Motion cue completion probe failed: " +
                    exception.Message;
                return false;
            }
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
            out int count,
            out string error)
        {
            runtime = null;
            count = 0;
            error = null;

            foreach (var candidate in
                     _runtimes)
            {
                if (!TryMatches(
                        candidate,
                        command,
                        out var matches,
                        out var candidateError))
                {
                    error ??=
                        candidateError;
                    continue;
                }

                if (!matches)
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

        private static bool TryMatches(
            IMotionCueRuntime runtime,
            EventActionCommand command,
            out bool matches,
            out string error)
        {
            matches = false;
            error = null;

            if (runtime == null ||
                runtime is UnityEngine.Object unityObject &&
                unityObject == null)
            {
                return true;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(
                        command.TargetId))
                {
                    matches =
                        string.Equals(
                            command.TargetId,
                            runtime.Status.RuntimeId,
                            StringComparison.Ordinal);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(
                        command.Text))
                {
                    var cueIds =
                        runtime.CueIds;

                    if (cueIds == null)
                    {
                        return true;
                    }

                    foreach (var cueId in cueIds)
                    {
                        if (string.Equals(
                                cueId,
                                command.Text,
                                StringComparison.Ordinal))
                        {
                            matches = true;
                            return true;
                        }
                    }

                    return true;
                }

                matches =
                    command.ActionType ==
                        EventActionTypes.MotionRelease &&
                    runtime.Status.Playing;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Motion cue runtime capability probe failed: " +
                    exception.Message;
                return false;
            }
        }
    }
}
