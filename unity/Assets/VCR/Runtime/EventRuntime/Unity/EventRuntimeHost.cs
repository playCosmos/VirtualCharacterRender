using System;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Events;
using VCR.Runtime.Events.Unity;

namespace VCR.Runtime.EventRuntime.Unity
{
    [DisallowMultipleComponent]
    public sealed class EventRuntimeHost :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [Header("Ingress")]
        [SerializeField] private NormalizedEventHub eventHub;
        [SerializeField] private bool autoFindEventHub = true;

        [Header("Rules")]
        [SerializeField] private EventRuntimeRule[] rules =
            Array.Empty<EventRuntimeRule>();
        [SerializeField, Range(1, 256)]
        private int maxCommandsPerEvent = 32;

        [Header("Action handlers")]
        [SerializeField] private MonoBehaviour[] actionHandlerBehaviours =
            Array.Empty<MonoBehaviour>();

        private readonly EventRuntimeEngine _engine =
            new();

        private readonly List<EventActionCommand> _commands =
            new(32);

        private IEventActionHandler[] _handlers =
            Array.Empty<IEventActionHandler>();

        private bool _subscribed;
        private long _executedActions;
        private long _failedActions;
        private long _unhandledActions;
        private long _ambiguousActions;
        private string _lastError;

        public EventRuntimeEngine Engine => _engine;
        public long ExecutedActions => _executedActions;
        public long FailedActions => _failedActions;
        public long UnhandledActions => _unhandledActions;
        public long AmbiguousActions => _ambiguousActions;
        public string LastError => _lastError;

        private void Awake()
        {
            ApplyRules();
            RebuildHandlers();
            ResolveEventHub();
        }

        private void OnEnable()
        {
            ResolveEventHub();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void SetEventHub(
            NormalizedEventHub hub)
        {
            Unsubscribe();
            eventHub = hub;

            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        public void SetRules(
            params EventRuntimeRule[] nextRules)
        {
            rules =
                nextRules == null
                    ? Array.Empty<EventRuntimeRule>()
                    : (EventRuntimeRule[])
                        nextRules.Clone();

            ApplyRules();
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

        private void ApplyRules()
        {
            _engine.MaxCommandsPerEvent =
                maxCommandsPerEvent;
            _engine.SetRules(rules);
        }

        private void ResolveEventHub()
        {
            if (eventHub != null ||
                !autoFindEventHub)
            {
                return;
            }

            eventHub =
                FindFirstObjectByType<
                    NormalizedEventHub>(
                    FindObjectsInactive.Exclude);
        }

        private void RebuildHandlers()
        {
            if (actionHandlerBehaviours == null ||
                actionHandlerBehaviours.Length == 0)
            {
                _handlers =
                    Array.Empty<IEventActionHandler>();
                return;
            }

            var list =
                new List<IEventActionHandler>(
                    actionHandlerBehaviours.Length);

            foreach (var behaviour in
                     actionHandlerBehaviours)
            {
                if (behaviour is
                    IEventActionHandler handler)
                {
                    list.Add(handler);
                }
            }

            _handlers = list.ToArray();
        }

        private void Subscribe()
        {
            if (_subscribed ||
                eventHub == null)
            {
                return;
            }

            eventHub.Published +=
                OnEventPublished;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (eventHub != null)
            {
                eventHub.Published -=
                    OnEventPublished;
            }

            _subscribed = false;
        }

        private void OnEventPublished(
            NormalizedEvent value)
        {
            _engine.MaxCommandsPerEvent =
                maxCommandsPerEvent;
            _engine.Process(
                value,
                _commands);

            foreach (var command in _commands)
            {
                Dispatch(command);
            }
        }

        private void Dispatch(
            EventActionCommand command)
        {
            IEventActionHandler handler = null;
            var handlerCount = 0;

            foreach (var candidate in _handlers)
            {
                if (candidate == null ||
                    !candidate.CanHandle(command))
                {
                    continue;
                }

                handlerCount++;

                if (handler == null)
                {
                    handler = candidate;
                }
            }

            if (handlerCount == 0)
            {
                _unhandledActions++;
                _lastError =
                    $"No event action handler for '{command.ActionType}'.";
                return;
            }

            if (handlerCount > 1)
            {
                _ambiguousActions++;
                _failedActions++;
                _lastError =
                    $"Multiple event action handlers ({handlerCount}) claim '{command.ActionType}'.";
                return;
            }

            try
            {
                if (handler.TryExecute(
                        command,
                        out var error))
                {
                    _executedActions++;
                    _lastError = null;
                }
                else
                {
                    _failedActions++;
                    _lastError =
                        string.IsNullOrWhiteSpace(error)
                            ? $"Event action '{command.ActionType}' failed."
                            : error;
                }
            }
            catch (Exception exception)
            {
                _failedActions++;
                _lastError =
                    exception.Message;
                Debug.LogException(
                    exception,
                    this);
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
                    "events.runtime.processed",
                    _engine.ProcessedEvents,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.matched_rules",
                    _engine.MatchedRules,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.commands",
                    _engine.EmittedCommands,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.commands_dropped",
                    _engine.DroppedCommands,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.cooldown_suppressed",
                    _engine.CooldownSuppressedRules,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.rate_limit_suppressed",
                    _engine.RateLimitSuppressedRules,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.actions_executed",
                    _executedActions,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.actions_failed",
                    _failedActions,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.actions_unhandled",
                    _unhandledActions,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.actions_ambiguous",
                    _ambiguousActions,
                    "count"));
        }
    }
}
