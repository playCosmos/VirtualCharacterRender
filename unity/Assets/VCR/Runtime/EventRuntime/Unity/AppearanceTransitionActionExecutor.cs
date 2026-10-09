using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Appearance;
using VCR.Runtime.Core;

namespace VCR.Runtime.EventRuntime.Unity
{
    /// <summary>
    /// Reuses registered application-level event action handlers for wardrobe
    /// transition presentation steps. appearance.* actions are deliberately
    /// rejected to prevent recursive appearance transitions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppearanceTransitionActionExecutor :
        MonoBehaviour,
        IAppearanceTransitionStepExecutor,
        IAppearanceTransitionStepCompletionProbe,
        IRuntimeMetricsSource
    {
        [SerializeField] private MonoBehaviour[] actionHandlerBehaviours =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool autoFindHandlers = true;

        private const double HandlerDiscoveryRetrySeconds = 1.0;

        private IEventActionHandler[] _handlers =
            Array.Empty<IEventActionHandler>();
        private double _nextHandlerResolveAt;
        private long _handlerCapabilityProbeFailureCount;
        private long _completionCapabilityProbeFailureCount;

        private void Awake()
        {
            RebuildHandlers();
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

        public bool CanExecute(
            AppearanceTransitionStep step)
        {
            if (step == null ||
                step.Kind !=
                    AppearanceTransitionStepKind.Action ||
                string.IsNullOrWhiteSpace(
                    step.ActionType) ||
                step.ActionType.StartsWith(
                    "appearance.",
                    StringComparison.Ordinal))
            {
                return false;
            }

            EnsureHandlers();

            return
                TryFindHandlerCount(
                    ToCommand(step),
                    out var count,
                    out _,
                    out _) &&
                count == 1;
        }

        public bool TryExecute(
            AppearanceTransitionStep step,
            out string error)
        {
            error = null;

            if (step == null ||
                step.Kind !=
                    AppearanceTransitionStepKind.Action)
            {
                error =
                    "Appearance transition step is not an action.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(
                    step.ActionType) ||
                step.ActionType.StartsWith(
                    "appearance.",
                    StringComparison.Ordinal))
            {
                error =
                    "Appearance transitions cannot recursively execute appearance actions.";
                return false;
            }

            EnsureHandlers();

            var command =
                ToCommand(step);

            if (!TryFindHandlerCount(
                    command,
                    out var count,
                    out var handler,
                    out error))
            {
                return false;
            }

            if (count == 0)
            {
                error =
                    $"No event action handler accepts '{step.ActionType}'.";
                return false;
            }

            if (count > 1)
            {
                error =
                    $"Multiple event action handlers accept '{step.ActionType}'.";
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

        public bool CanTrackCompletion(
            AppearanceTransitionStep step)
        {
            return
                TryResolveCompletionProbe(
                    step,
                    out _,
                    out _);
        }

        public bool TryIsComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (!TryResolveCompletionProbe(
                    step,
                    out var probe,
                    out error))
            {
                error ??=
                    $"Transition action '{step?.ActionType ?? "<null>"}' does not expose completion tracking.";
                return false;
            }

            var command =
                ToCommand(
                    step);

            try
            {
                return probe
                    .TryIsComplete(
                        command,
                        out complete,
                        out error);
            }
            catch (Exception exception)
            {
                error =
                    exception.Message;
                return false;
            }
        }

        private static EventActionCommand ToCommand(
            AppearanceTransitionStep step)
        {
            return new EventActionCommand(
                ruleId:
                    "appearance-transition",
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

            var needsRebuild =
                _handlers.Length == 0;

            if (!needsRebuild)
            {
                foreach (var handler in _handlers)
                {
                    if (!IsServiceAlive(handler))
                    {
                        needsRebuild = true;
                        break;
                    }
                }
            }

            if (!needsRebuild)
            {
                _nextHandlerResolveAt = 0d;
                return;
            }

            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now <
                _nextHandlerResolveAt)
            {
                return;
            }

            _nextHandlerResolveAt =
                now +
                HandlerDiscoveryRetrySeconds;
            RebuildHandlers();

            if (_handlers.Length > 0)
            {
                _nextHandlerResolveAt = 0d;
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
                if (behaviour != null &&
                    behaviour is
                        IEventActionHandler handler)
                {
                    list.Add(handler);
                }
            }

            // Explicitly configured handlers form an authoritative scope.
            // Mixing unrelated scene-wide handlers into that set makes
            // otherwise unambiguous transition actions fail closed.
            if (autoFindHandlers &&
                (actionHandlerBehaviours == null ||
                 actionHandlerBehaviours.Length == 0))
            {
                var behaviours =
                    FindObjectsByType<MonoBehaviour>(
                        FindObjectsInactive.Exclude,
                        FindObjectsSortMode.None);

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is not
                            IEventActionHandler handler ||
                        list.Contains(handler))
                    {
                        continue;
                    }

                    list.Add(handler);
                }
            }

            _handlers =
                list.ToArray();

            if (_handlers.Length == 0 &&
                autoFindHandlers)
            {
                _nextHandlerResolveAt =
                    Time.realtimeSinceStartupAsDouble +
                    HandlerDiscoveryRetrySeconds;
            }
            else
            {
                _nextHandlerResolveAt = 0d;
            }
        }

        private bool TryResolveCompletionProbe(
            AppearanceTransitionStep step,
            out IEventActionCompletionProbe probe,
            out string error)
        {
            probe = null;
            error = null;

            if (step == null ||
                step.Kind !=
                    AppearanceTransitionStepKind.Action ||
                string.IsNullOrWhiteSpace(
                    step.ActionType) ||
                step.ActionType.StartsWith(
                    "appearance.",
                    StringComparison.Ordinal))
            {
                return false;
            }

            EnsureHandlers();

            var command =
                ToCommand(
                    step);

            if (!TryFindHandlerCount(
                    command,
                    out var count,
                    out var handler,
                    out error))
            {
                return false;
            }

            if (count != 1 ||
                handler is not
                    IEventActionCompletionProbe
                        completionProbe)
            {
                return false;
            }

            if (!TryCanTrackCompletion(
                    completionProbe,
                    command,
                    out var canTrack,
                    out error))
            {
                return false;
            }

            if (!canTrack)
            {
                return false;
            }

            probe =
                completionProbe;
            return true;
        }

        private bool TryFindHandlerCount(
            EventActionCommand command,
            out int count,
            out IEventActionHandler handler,
            out string error)
        {
            handler = null;
            count = 0;
            error = null;

            foreach (var candidate in _handlers)
            {
                if (!IsServiceAlive(candidate))
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
                    _handlerCapabilityProbeFailureCount++;
                    error =
                        "Event action handler CanHandle failed: " +
                        exception.Message;
                    return false;
                }

                if (!canHandle)
                {
                    continue;
                }

                count++;

                if (handler == null)
                {
                    handler = candidate;
                }
            }

            return true;
        }

        private bool TryCanTrackCompletion(
            IEventActionCompletionProbe probe,
            EventActionCommand command,
            out bool canTrack,
            out string error)
        {
            canTrack = false;
            error = null;

            try
            {
                canTrack =
                    probe.CanTrackCompletion(
                        command);
                return true;
            }
            catch (Exception exception)
            {
                _completionCapabilityProbeFailureCount++;
                error =
                    "Event action completion CanTrackCompletion failed: " +
                    exception.Message;
                return false;
            }
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
                    "appearance.transition.action_handler_probe_failures",
                    _handlerCapabilityProbeFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "appearance.transition.action_completion_probe_failures",
                    _completionCapabilityProbeFailureCount,
                    "count"));
        }
    }
}
