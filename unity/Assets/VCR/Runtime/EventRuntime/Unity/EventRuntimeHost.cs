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
        [SerializeField, Min(0.25f)] private float eventHubResolveIntervalSeconds = 1f;

        [Header("Rules")]
        [SerializeField] private EventRuntimeRule[] rules =
            Array.Empty<EventRuntimeRule>();
        [SerializeField, Range(1, 256)]
        private int maxCommandsPerEvent = 32;

        [Header("Action handlers")]
        [SerializeField] private MonoBehaviour[] actionHandlerBehaviours =
            Array.Empty<MonoBehaviour>();

        [Header("Diagnostics")]
        [SerializeField] private bool enableRuleTracing = false;

        private readonly EventRuntimeEngine _engine =
            new();

        private readonly List<EventActionCommand> _commands =
            new(32);

        private IEventActionHandler[] _handlers =
            Array.Empty<IEventActionHandler>();

        private NormalizedEventHub _subscribedHub;
        private long _executedActions;
        private long _failedActions;
        private long _unhandledActions;
        private long _ambiguousActions;
        private long _handlerProbeFailureCount;
        private string _lastError;

        public EventRuntimeEngine Engine => _engine;
        public long ExecutedActions => _executedActions;
        public long FailedActions => _failedActions;
        public long UnhandledActions => _unhandledActions;
        public long AmbiguousActions => _ambiguousActions;
        public long HandlerProbeFailureCount =>
            _handlerProbeFailureCount;
        public string LastError => _lastError;
        public int MaxCommandsPerEvent =>
            maxCommandsPerEvent;
        public int RuleCount =>
            rules?.Length ?? 0;

        public EventRuntimeRule GetRuleAt(
            int index)
        {
            var current =
                rules;

            if (current == null ||
                index < 0 ||
                index >= current.Length)
            {
                return null;
            }

            return EventRuntimeRuleCloner
                .CloneRule(
                    current[index]);
        }

        public bool TryGetRule(
            string ruleId,
            out EventRuntimeRule rule)
        {
            rule = null;

            if (!TryFindRule(
                    ruleId,
                    out var candidate))
            {
                return false;
            }

            rule =
                EventRuntimeRuleCloner
                    .CloneRule(
                        candidate);
            return true;
        }

        public bool TryGetRuleSummaryAt(
            int index,
            out EventRuntimeRuleSummary summary)
        {
            summary = default;

            var current =
                rules;

            if (current == null ||
                index < 0 ||
                index >= current.Length ||
                current[index] == null)
            {
                return false;
            }

            var rule =
                current[index];

            summary =
                new EventRuntimeRuleSummary(
                    rule.Id,
                    rule.Enabled);
            return true;
        }

        public bool TryGetRuleSummary(
            string ruleId,
            out EventRuntimeRuleSummary summary)
        {
            summary = default;

            if (!TryFindRule(
                    ruleId,
                    out var rule))
            {
                return false;
            }

            summary =
                new EventRuntimeRuleSummary(
                    rule.Id,
                    rule.Enabled);
            return true;
        }

        private bool TryFindRule(
            string ruleId,
            out EventRuntimeRule rule)
        {
            rule = null;

            if (string.IsNullOrWhiteSpace(
                    ruleId))
            {
                return false;
            }

            foreach (var candidate in
                     rules ??
                     Array.Empty<EventRuntimeRule>())
            {
                if (candidate != null &&
                    string.Equals(
                        candidate.Id,
                        ruleId,
                        StringComparison.Ordinal))
                {
                    rule =
                        candidate;
                    return true;
                }
            }

            return false;
        }

        public EventRuntimeRuleDiagnostics[]
            GetRuleDiagnostics() =>
                _engine.GetRuleDiagnostics();

        public void SetRuleTracingEnabled(
            bool enabled)
        {
            enableRuleTracing = enabled;
            _engine.TraceEnabled = enabled;
        }

        private void Awake()
        {
            TryApplyRules(
                rules,
                out _);
            RebuildHandlers();
            ResolveEventHub();
        }

        private void OnEnable()
        {
            RefreshEventHubSubscription();

            if (Application.isPlaying &&
                autoFindEventHub)
            {
                CancelInvoke(
                    nameof(
                        RefreshEventHubSubscription));
                InvokeRepeating(
                    nameof(
                        RefreshEventHubSubscription),
                    Mathf.Max(
                        0.25f,
                        eventHubResolveIntervalSeconds),
                    Mathf.Max(
                        0.25f,
                        eventHubResolveIntervalSeconds));
            }
        }

        private void OnDisable()
        {
            CancelInvoke(
                nameof(
                    RefreshEventHubSubscription));
            Unsubscribe();
        }

        public void SetEventHub(
            NormalizedEventHub hub)
        {
            Unsubscribe();
            eventHub = hub;

            if (isActiveAndEnabled)
            {
                RefreshEventHubSubscription();
            }
        }

        public void SetRules(
            params EventRuntimeRule[] nextRules)
        {
            var staged =
                EventRuntimeRuleCloner
                    .CloneRules(
                        nextRules);

            if (!EventRuntimeRuleSetBounds
                .TryValidate(
                    staged,
                    out var error))
            {
                _lastError =
                    error;
                return;
            }

            if (!TryApplyRules(
                    staged,
                    out _))
            {
                return;
            }

            rules =
                staged;
        }

        public EventRuntimeRule[] CaptureRules()
        {
            return EventRuntimeRuleCloner
                .CloneRules(
                    rules);
        }

        public bool TrySetRuleEnabled(
            string ruleId,
            bool enabled,
            out string error)
        {
            error = null;
            var id =
                ruleId?.Trim();

            if (string.IsNullOrWhiteSpace(
                    id))
            {
                error =
                    "Event rule id is required.";
                return false;
            }

            EventRuntimeRule match =
                null;

            foreach (var rule in
                     rules ??
                     Array.Empty<
                         EventRuntimeRule>())
            {
                if (rule == null ||
                    !string.Equals(
                        rule.Id,
                        id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    error =
                        $"Event rule id '{id}' is ambiguous.";
                    return false;
                }

                match =
                    rule;
            }

            if (match == null)
            {
                error =
                    $"Unknown event rule '{id}'.";
                return false;
            }

            var previousEnabled =
                match.Enabled;

            match.Enabled =
                enabled;

            if (!TryApplyRules(
                    rules,
                    out error))
            {
                match.Enabled =
                    previousEnabled;
                return false;
            }

            return true;
        }

        public bool TrySetMaxCommandsPerEvent(
            int value,
            out string error)
        {
            error = null;

            if (value < 1 ||
                value > 256)
            {
                error =
                    "Max commands per event must be in the 1..256 range.";
                return false;
            }

            var previousValue =
                maxCommandsPerEvent;

            maxCommandsPerEvent =
                value;

            if (!TryApplyRules(
                    rules,
                    out error))
            {
                maxCommandsPerEvent =
                    previousValue;
                return false;
            }

            return true;
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

        private bool TryApplyRules(
            EventRuntimeRule[] nextRules,
            out string error)
        {
            if (!_engine.TrySetRules(
                    nextRules,
                    out error))
            {
                _lastError =
                    error;
                return false;
            }

            _engine.MaxCommandsPerEvent =
                maxCommandsPerEvent;
            _engine.TraceEnabled =
                enableRuleTracing;
            _lastError = null;
            return true;
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
                if (behaviour != null &&
                    behaviour is
                        IEventActionHandler handler &&
                    !list.Contains(handler))
                {
                    list.Add(handler);
                }
            }

            _handlers = list.ToArray();
        }

        private void RefreshEventHubSubscription()
        {
            if (eventHub == null)
            {
                ResolveEventHub();
            }

            if (_subscribedHub == eventHub &&
                eventHub != null)
            {
                return;
            }

            Unsubscribe();
            Subscribe();
        }

        private void Subscribe()
        {
            if (eventHub == null)
            {
                return;
            }

            if (_subscribedHub == eventHub)
            {
                return;
            }

            Unsubscribe();
            eventHub.Published +=
                OnEventPublished;
            _subscribedHub =
                eventHub;
        }

        private void Unsubscribe()
        {
            var hub =
                _subscribedHub;
            _subscribedHub = null;

            if (hub != null)
            {
                hub.Published -=
                    OnEventPublished;
            }
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

        private void Dispatch(
            EventActionCommand command)
        {
            IEventActionHandler handler = null;
            var handlerCount = 0;

            string firstProbeError = null;

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
                        candidate.CanHandle(command);
                }
                catch (Exception exception)
                {
                    _handlerProbeFailureCount++;
                    firstProbeError ??=
                        exception.Message;

                    Debug.LogException(
                        exception,
                        this);
                    continue;
                }

                if (!canHandle)
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
                    firstProbeError == null
                        ? $"No event action handler for '{command.ActionType}'."
                        : $"No event action handler completed capability probing for '{command.ActionType}': {firstProbeError}";
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
                    "events.runtime.rule_tracing_enabled",
                    _engine.TraceEnabled
                        ? 1.0
                        : 0.0,
                    "bool"));
            output.Add(
                new RuntimeMetric(
                    "events.runtime.trace_subscriber_failures",
                    _engine.TraceSubscriberFailureCount,
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
            output.Add(
                new RuntimeMetric(
                    "events.runtime.handler_probe_failures",
                    _handlerProbeFailureCount,
                    "count"));
        }
    }
}
