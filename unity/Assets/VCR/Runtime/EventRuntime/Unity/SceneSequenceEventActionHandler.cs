using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class SceneSequenceEventActionHandler :
        MonoBehaviour,
        IEventActionHandler,
        IEventActionCompletionProbe,
        IRuntimeMetricsSource
    {
        [Serializable]
        public sealed class SequenceStep
        {
            [Min(0f)] public float TimeSeconds;
            public string ActionType;
            public string TargetId;
            public string Name;
            public string Text;
            public double Value;
            public bool HasValue;
            public bool Required = true;
        }

        [Serializable]
        public sealed class SequenceBinding
        {
            public string SequenceId;
            public SequenceStep[] Steps =
                Array.Empty<SequenceStep>();
            public SequenceStep[] CancellationSteps =
                Array.Empty<SequenceStep>();
        }

        [SerializeField] private string handlerId =
            "scene.sequences";
        [SerializeField] private SequenceBinding[] sequences =
            Array.Empty<SequenceBinding>();
        [SerializeField] private MonoBehaviour[] actionHandlerBehaviours =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool autoFindHandlers = true;

        private readonly Dictionary<string, SequenceBinding>
            _sequences =
                new(StringComparer.Ordinal);

        private IEventActionHandler[] _handlers =
            Array.Empty<IEventActionHandler>();
        private Coroutine _activeCoroutine;
        private SequenceBinding _activeSequence;
        private string _activeSequenceId;
        private string _lastTerminalSequenceId;
        private bool _lastTerminalSucceeded;
        private string _lastError;
        private long _playCount;
        private long _completedCount;
        private long _cancelledCount;
        private long _stepCount;
        private long _failureCount;
        private long _handlerProbeFailureCount;

        public string HandlerId => handlerId;
        public bool Busy => _activeCoroutine != null;
        public string ActiveSequenceId =>
            _activeSequenceId;
        public string LastError =>
            _lastError;

        private void Awake()
        {
            RebuildBindings(
                out _);
            RebuildHandlers();
        }

        private void OnDisable()
        {
            if (_activeCoroutine == null)
            {
                return;
            }

            StopCoroutine(
                _activeCoroutine);
            _activeCoroutine = null;

            var active =
                _activeSequence;
            var activeId =
                _activeSequenceId;

            var cleaned =
                TryRunCancellationCleanup(
                    active,
                    out var cleanupError);

            _cancelledCount++;

            if (!cleaned)
            {
                _failureCount++;
            }

            FinishTerminal(
                activeId,
                succeeded:
                    false,
                cleanupError ??
                "Scene sequence stopped because its handler was disabled.");
        }

        public void ConfigureSequences(
            params SequenceBinding[] bindings)
        {
            sequences =
                bindings ??
                Array.Empty<SequenceBinding>();

            if (!RebuildBindings(
                    out var error))
            {
                _lastError =
                    error;
            }
        }

        public void SetActionHandlers(
            params MonoBehaviour[] behaviours)
        {
            actionHandlerBehaviours =
                behaviours == null
                    ? Array.Empty<MonoBehaviour>()
                    : (MonoBehaviour[])
                        behaviours.Clone();
            RebuildHandlers();
        }

        public bool RebuildBindings(
            out string error)
        {
            error = null;

            var staged =
                new Dictionary<string, SequenceBinding>(
                    StringComparer.Ordinal);

            foreach (var binding in
                     sequences ??
                     Array.Empty<SequenceBinding>())
            {
                if (!ValidateBinding(
                        binding,
                        out error))
                {
                    _lastError =
                        error;
                    return false;
                }

                var id =
                    binding.SequenceId.Trim();

                if (!staged.TryAdd(
                        id,
                        binding))
                {
                    error =
                        $"Duplicate scene sequence id '{id}'.";
                    _lastError =
                        error;
                    return false;
                }
            }

            _sequences.Clear();

            foreach (var item in staged)
            {
                _sequences[item.Key] =
                    item.Value;
            }

            _lastError = null;
            return true;
        }

        public bool CanHandle(
            EventActionCommand command)
        {
            if (command.ActionType !=
                    EventActionTypes.SceneSequencePlay &&
                command.ActionType !=
                    EventActionTypes.SceneSequenceCancel)
            {
                return false;
            }

            return
                string.IsNullOrWhiteSpace(
                    command.TargetId) ||
                string.Equals(
                    command.TargetId,
                    handlerId,
                    StringComparison.Ordinal);
        }

        public bool TryExecute(
            EventActionCommand command,
            out string error)
        {
            error = null;

            if (!CanHandle(
                    command))
            {
                error =
                    "Scene sequence action target is unavailable or does not match.";
                return Fail(
                    error);
            }

            if (command.ActionType ==
                EventActionTypes.SceneSequenceCancel)
            {
                return CancelActive(
                    out error);
            }

            var sequenceId =
                command.Text?.Trim();

            if (string.IsNullOrWhiteSpace(
                    sequenceId) ||
                !_sequences.TryGetValue(
                    sequenceId,
                    out var sequence))
            {
                error =
                    $"Unknown scene sequence id '{sequenceId ?? "<null>"}'.";
                return Fail(
                    error);
            }

            if (Busy)
            {
                error =
                    $"Scene sequence '{_activeSequenceId}' is already running.";
                return Fail(
                    error);
            }

            EnsureHandlers();

            if (!Preflight(
                    sequence,
                    out error))
            {
                return Fail(
                    error);
            }

            _activeSequence =
                sequence;
            _activeSequenceId =
                sequenceId;
            _lastTerminalSequenceId =
                null;
            _lastTerminalSucceeded =
                false;
            _lastError = null;
            _playCount++;
            _activeCoroutine =
                StartCoroutine(
                    RunSequence(
                        sequence));

            return true;
        }

        public bool CanTrackCompletion(
            EventActionCommand command)
        {
            if (!CanHandle(
                    command))
            {
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.SceneSequenceCancel)
            {
                return true;
            }

            var id =
                command.Text?.Trim();

            return
                !string.IsNullOrWhiteSpace(
                    id) &&
                _sequences.ContainsKey(
                    id);
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
                    "Scene sequence action completion cannot be tracked.";
                return false;
            }

            if (command.ActionType ==
                EventActionTypes.SceneSequenceCancel)
            {
                complete =
                    !Busy;
                return true;
            }

            var id =
                command.Text.Trim();

            if (Busy &&
                string.Equals(
                    _activeSequenceId,
                    id,
                    StringComparison.Ordinal))
            {
                complete = false;
                return true;
            }

            if (string.Equals(
                    _lastTerminalSequenceId,
                    id,
                    StringComparison.Ordinal))
            {
                if (!_lastTerminalSucceeded)
                {
                    error =
                        _lastError ??
                        $"Scene sequence '{id}' did not complete successfully.";
                    return false;
                }

                complete = true;
                return true;
            }

            error =
                $"Scene sequence '{id}' has no completion result for the current play request.";
            return false;
        }

        private IEnumerator RunSequence(
            SequenceBinding sequence)
        {
            var started =
                Time.unscaledTimeAsDouble;

            foreach (var step in
                     sequence.Steps ??
                     Array.Empty<SequenceStep>())
            {
                while (Time.unscaledTimeAsDouble -
                       started <
                       step.TimeSeconds)
                {
                    yield return null;
                }

                if (TryDispatchStep(
                        step,
                        allowOptionalMissing:
                            true,
                        out var stepError))
                {
                    _stepCount++;
                    continue;
                }

                if (!step.Required)
                {
                    _lastError =
                        stepError;
                    continue;
                }

                TryRunCancellationCleanup(
                    sequence,
                    out var cleanupError);

                _failureCount++;
                FinishTerminal(
                    sequence.SequenceId,
                    succeeded:
                        false,
                    cleanupError ??
                    stepError);
                yield break;
            }

            _completedCount++;
            FinishTerminal(
                sequence.SequenceId,
                succeeded:
                    true,
                null);
        }

        private bool CancelActive(
            out string error)
        {
            error = null;

            if (!Busy ||
                _activeSequence == null)
            {
                error =
                    "No scene sequence is currently running.";
                return Fail(
                    error);
            }

            var cleanup =
                _activeSequence
                    .CancellationSteps ??
                Array.Empty<SequenceStep>();

            if (cleanup.Length == 0)
            {
                error =
                    $"Scene sequence '{_activeSequenceId}' has no explicit cancellation cleanup contract.";
                return Fail(
                    error);
            }

            var active =
                _activeSequence;
            var activeId =
                _activeSequenceId;

            StopCoroutine(
                _activeCoroutine);
            _activeCoroutine = null;

            var cleaned =
                TryRunCancellationCleanup(
                    active,
                    out var cleanupError);

            _cancelledCount++;
            FinishTerminal(
                activeId,
                succeeded:
                    false,
                cleaned
                    ? $"Scene sequence '{activeId}' was cancelled."
                    : cleanupError);

            if (!cleaned)
            {
                error =
                    cleanupError;
                return false;
            }

            return true;
        }

        private bool Preflight(
            SequenceBinding sequence,
            out string error)
        {
            error = null;

            foreach (var step in
                     sequence.Steps ??
                     Array.Empty<SequenceStep>())
            {
                var count =
                    FindHandlerCount(
                        ToCommand(
                            sequence.SequenceId,
                            step),
                        out _);

                if (count > 1)
                {
                    error =
                        $"Scene sequence '{sequence.SequenceId}' action '{step.ActionType}' is ambiguous across {count} handlers.";
                    return false;
                }

                if (step.Required &&
                    count != 1)
                {
                    error =
                        $"Scene sequence '{sequence.SequenceId}' required action '{step.ActionType}' has no unique handler.";
                    return false;
                }
            }

            foreach (var step in
                     sequence.CancellationSteps ??
                     Array.Empty<SequenceStep>())
            {
                var count =
                    FindHandlerCount(
                        ToCommand(
                            sequence.SequenceId,
                            step),
                        out _);

                if (count > 1 ||
                    (step.Required &&
                     count != 1))
                {
                    error =
                        $"Scene sequence '{sequence.SequenceId}' cancellation action '{step.ActionType}' has no unique handler.";
                    return false;
                }
            }

            return true;
        }

        private bool TryRunCancellationCleanup(
            SequenceBinding sequence,
            out string error)
        {
            error = null;

            if (sequence == null)
            {
                return true;
            }

            string firstRequiredError =
                null;

            foreach (var step in
                     sequence.CancellationSteps ??
                     Array.Empty<SequenceStep>())
            {
                if (TryDispatchStep(
                        step,
                        allowOptionalMissing:
                            true,
                        out var stepError))
                {
                    continue;
                }

                if (step.Required &&
                    firstRequiredError == null)
                {
                    firstRequiredError =
                        stepError;
                }
            }

            if (firstRequiredError != null)
            {
                error =
                    firstRequiredError;
                return false;
            }

            return true;
        }

        private bool TryDispatchStep(
            SequenceStep step,
            bool allowOptionalMissing,
            out string error)
        {
            error = null;
            EnsureHandlers();

            var command =
                ToCommand(
                    _activeSequenceId ??
                    "scene-sequence",
                    step);
            var count =
                FindHandlerCount(
                    command,
                    out var handler);

            if (count == 0)
            {
                if (!step.Required &&
                    allowOptionalMissing)
                {
                    return true;
                }

                error =
                    $"No event action handler accepts scene sequence action '{step.ActionType}'.";
                return false;
            }

            if (count > 1)
            {
                error =
                    $"Multiple event action handlers accept scene sequence action '{step.ActionType}'.";
                return false;
            }

            try
            {
                return handler.TryExecute(
                    command,
                    out error);
            }
            catch (Exception exception)
            {
                error =
                    exception.Message;
                return false;
            }
        }

        private void FinishTerminal(
            string sequenceId,
            bool succeeded,
            string error)
        {
            _activeCoroutine = null;
            _activeSequence = null;
            _activeSequenceId = null;
            _lastTerminalSequenceId =
                sequenceId;
            _lastTerminalSucceeded =
                succeeded;
            _lastError =
                error;
        }

        private static bool ValidateBinding(
            SequenceBinding binding,
            out string error)
        {
            error = null;

            if (binding == null ||
                string.IsNullOrWhiteSpace(
                    binding.SequenceId))
            {
                error =
                    "Every scene sequence requires a non-empty sequence id.";
                return false;
            }

            var previousTime =
                0f;
            var hasStep =
                false;

            foreach (var step in
                     binding.Steps ??
                     Array.Empty<SequenceStep>())
            {
                if (!ValidateStep(
                        binding.SequenceId,
                        step,
                        cancellation:
                            false,
                        out error))
                {
                    return false;
                }

                if (hasStep &&
                    step.TimeSeconds <
                        previousTime)
                {
                    error =
                        $"Scene sequence '{binding.SequenceId}' steps must use non-decreasing time.";
                    return false;
                }

                previousTime =
                    step.TimeSeconds;
                hasStep =
                    true;
            }

            if (!hasStep)
            {
                error =
                    $"Scene sequence '{binding.SequenceId}' requires at least one step.";
                return false;
            }

            foreach (var step in
                     binding.CancellationSteps ??
                     Array.Empty<SequenceStep>())
            {
                if (!ValidateStep(
                        binding.SequenceId,
                        step,
                        cancellation:
                            true,
                        out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateStep(
            string sequenceId,
            SequenceStep step,
            bool cancellation,
            out string error)
        {
            error = null;

            if (step == null ||
                float.IsNaN(
                    step.TimeSeconds) ||
                float.IsInfinity(
                    step.TimeSeconds) ||
                step.TimeSeconds < 0f)
            {
                error =
                    $"Scene sequence '{sequenceId}' contains an invalid step time.";
                return false;
            }

            if (cancellation &&
                step.TimeSeconds != 0f)
            {
                error =
                    $"Scene sequence '{sequenceId}' cancellation steps execute immediately and must use time 0.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    step.ActionType))
            {
                error =
                    $"Scene sequence '{sequenceId}' contains a step without ActionType.";
                return false;
            }

            if (string.Equals(
                    step.ActionType,
                    EventActionTypes.SceneSequencePlay,
                    StringComparison.Ordinal) ||
                string.Equals(
                    step.ActionType,
                    EventActionTypes.SceneSequenceCancel,
                    StringComparison.Ordinal))
            {
                error =
                    $"Scene sequence '{sequenceId}' cannot recursively execute scene.sequence actions.";
                return false;
            }

            return true;
        }

        private static EventActionCommand ToCommand(
            string sequenceId,
            SequenceStep step)
        {
            return new EventActionCommand(
                ruleId:
                    "scene-sequence:" +
                    sequenceId,
                actionType:
                    step.ActionType,
                targetId:
                    step.TargetId,
                name:
                    step.Name,
                text:
                    step.Text,
                value:
                    step.Value,
                hasValue:
                    step.HasValue,
                eventSequence:
                    0);
        }

        private void EnsureHandlers()
        {
            if (!autoFindHandlers)
            {
                return;
            }

            if (_handlers.Length == 0)
            {
                RebuildHandlers();
                return;
            }

            foreach (var handler in _handlers)
            {
                if (!IsServiceAlive(handler))
                {
                    RebuildHandlers();
                    return;
                }
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

        private void RebuildHandlers()
        {
            var list =
                new List<IEventActionHandler>();

            foreach (var behaviour in
                     actionHandlerBehaviours ??
                     Array.Empty<MonoBehaviour>())
            {
                if (behaviour == null ||
                    ReferenceEquals(
                        behaviour,
                        this) ||
                    behaviour is not
                        IEventActionHandler handler ||
                    list.Contains(
                        handler))
                {
                    continue;
                }

                list.Add(
                    handler);
            }

            if (autoFindHandlers)
            {
                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var behaviour in
                         behaviours)
                {
                    if (behaviour == null ||
                        ReferenceEquals(
                            behaviour,
                            this) ||
                        behaviour is not
                            IEventActionHandler handler ||
                        list.Contains(
                            handler))
                    {
                        continue;
                    }

                    list.Add(
                        handler);
                }
            }

            _handlers =
                list.ToArray();
        }

        private int FindHandlerCount(
            EventActionCommand command,
            out IEventActionHandler handler)
        {
            handler = null;
            var count = 0;

            foreach (var candidate in
                     _handlers)
            {
                if (!IsServiceAlive(candidate) ||
                    ReferenceEquals(
                        candidate,
                        this))
                {
                    continue;
                }

                bool canHandle;

                try
                {
                    canHandle =
                        candidate.CanHandle(
                            command);
                }
                catch (Exception exception)
                {
                    _handlerProbeFailureCount++;
                    _lastError =
                        "Scene sequence action handler capability probe failed: " +
                        exception.Message;
                    continue;
                }

                if (!canHandle)
                {
                    continue;
                }

                count++;

                if (handler == null)
                {
                    handler =
                        candidate;
                }
            }

            return count;
        }

        private bool Fail(
            string error)
        {
            _failureCount++;
            _lastError =
                error;
            return false;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "scene.sequence.busy",
                    Busy ? 1.0 : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.plays",
                    _playCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.completed",
                    _completedCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.cancelled",
                    _cancelledCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.steps",
                    _stepCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.failures",
                    _failureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "scene.sequence.handler_probe_failures",
                    _handlerProbeFailureCount,
                    "count"));
        }
    }
}
