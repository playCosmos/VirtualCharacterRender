using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Appearance;

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
        IAppearanceTransitionStepCompletionProbe
    {
        [SerializeField] private MonoBehaviour[] actionHandlerBehaviours =
            Array.Empty<MonoBehaviour>();
        [SerializeField] private bool autoFindHandlers = true;

        private const double HandlerDiscoveryRetrySeconds = 1.0;

        private IEventActionHandler[] _handlers =
            Array.Empty<IEventActionHandler>();
        private double _nextHandlerResolveAt;

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
                FindHandlerCount(
                    ToCommand(step),
                    out _) == 1;
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
            var count =
                FindHandlerCount(
                    command,
                    out var handler);

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
            if (!CanExecute(
                    step))
            {
                return false;
            }

            EnsureHandlers();

            var command =
                ToCommand(
                    step);
            var count =
                FindHandlerCount(
                    command,
                    out var handler);

            return
                count == 1 &&
                handler is
                    IEventActionCompletionProbe
                        probe &&
                probe.CanTrackCompletion(
                    command);
        }

        public bool TryIsComplete(
            AppearanceTransitionStep step,
            out bool complete,
            out string error)
        {
            complete = false;
            error = null;

            if (!CanTrackCompletion(
                    step))
            {
                error =
                    $"Transition action '{step?.ActionType ?? "<null>"}' does not expose completion tracking.";
                return false;
            }

            var command =
                ToCommand(
                    step);
            FindHandlerCount(
                command,
                out var handler);

            try
            {
                return ((IEventActionCompletionProbe)
                        handler)
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

            if (autoFindHandlers)
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

        private int FindHandlerCount(
            EventActionCommand command,
            out IEventActionHandler handler)
        {
            handler = null;
            var count = 0;

            foreach (var candidate in _handlers)
            {
                if (!IsServiceAlive(candidate) ||
                    !candidate.CanHandle(
                        command))
                {
                    continue;
                }

                count++;

                if (handler == null)
                {
                    handler = candidate;
                }
            }

            return count;
        }
    }
}
