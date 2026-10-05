using System;
using System.Collections.Generic;
using VCR.Runtime.Events;

namespace VCR.Runtime.EventRuntime
{
    public sealed class EventRuntimeEngine
    {
        private EventRuntimeRule[] _rules =
            Array.Empty<EventRuntimeRule>();
        private int _maxCommandsPerEvent = 32;

        private readonly Dictionary<EventRuntimeRule, long>
            _lastRuleExecutionUs = new();

        private readonly Dictionary<EventRuntimeRule, RateWindowState>
            _rateWindows = new();

        private readonly Dictionary<EventRuntimeRule, RuleDiagnosticsCounter>
            _ruleDiagnostics = new();

        public EventRuntimeStateStore State { get; } = new();

        public bool TraceEnabled { get; set; }

        public event Action<EventRuntimeTraceEntry>
            TraceEmitted;

        public long ProcessedEvents { get; private set; }
        public long MatchedRules { get; private set; }
        public long EmittedCommands { get; private set; }
        public long DroppedCommands { get; private set; }
        public long CooldownSuppressedRules { get; private set; }
        public long RateLimitSuppressedRules { get; private set; }

        public int MaxCommandsPerEvent
        {
            get => _maxCommandsPerEvent;
            set => _maxCommandsPerEvent =
                Math.Max(1, Math.Min(256, value));
        }

        public void SetRules(params EventRuntimeRule[] rules)
        {
            if (!TrySetRules(
                    rules,
                    out var error))
            {
                throw new ArgumentException(
                    error,
                    nameof(rules));
            }
        }

        public bool TrySetRules(
            EventRuntimeRule[] rules,
            out string error)
        {
            var next =
                rules == null
                    ? Array.Empty<EventRuntimeRule>()
                    : (EventRuntimeRule[])
                        rules.Clone();

            if (!EventRuntimeRuleSetBounds
                .TryValidate(
                    next,
                    out error))
            {
                return false;
            }

            _rules =
                next;

            _lastRuleExecutionUs.Clear();
            _rateWindows.Clear();
            _ruleDiagnostics.Clear();

            foreach (var rule in _rules)
            {
                if (rule != null)
                {
                    _ruleDiagnostics[rule] =
                        new RuleDiagnosticsCounter();
                }
            }

            error = null;
            return true;
        }

        public int Process(
            NormalizedEvent value,
            List<EventActionCommand> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            ProcessedEvents++;

            foreach (var rule in _rules)
            {
                if (rule == null ||
                    !rule.Enabled)
                {
                    continue;
                }

                var diagnostics =
                    GetRuleCounter(rule);
                diagnostics.EvaluatedEvents++;

                if (!(rule.Filter?.Matches(value) ?? true))
                {
                    diagnostics.FilterRejectedEvents++;
                    Trace(
                        rule,
                        value,
                        EventRuntimeTraceOutcome.FilterRejected,
                        0,
                        0);
                    continue;
                }

                if (!ConditionsPass(rule))
                {
                    diagnostics.ConditionRejectedEvents++;
                    Trace(
                        rule,
                        value,
                        EventRuntimeTraceOutcome.ConditionRejected,
                        0,
                        0);
                    continue;
                }

                if (IsCoolingDown(
                        rule,
                        value.TimestampUs))
                {
                    CooldownSuppressedRules++;
                    diagnostics.CooldownSuppressedEvents++;
                    Trace(
                        rule,
                        value,
                        EventRuntimeTraceOutcome.CooldownSuppressed,
                        0,
                        0);
                    continue;
                }

                if (IsRateLimited(
                        rule,
                        value.TimestampUs))
                {
                    RateLimitSuppressedRules++;
                    diagnostics.RateLimitSuppressedEvents++;
                    Trace(
                        rule,
                        value,
                        EventRuntimeTraceOutcome.RateLimitSuppressed,
                        0,
                        0);
                    continue;
                }

                MatchedRules++;
                diagnostics.MatchedEvents++;
                diagnostics.LastMatchedTimestampUs =
                    value.TimestampUs;

                RecordExecution(
                    rule,
                    value.TimestampUs);
                ApplyMutations(rule, value);

                var commandCountBefore =
                    output.Count;
                var droppedBefore =
                    DroppedCommands;

                EmitActions(
                    rule,
                    value,
                    output);

                var emitted =
                    output.Count -
                    commandCountBefore;
                var dropped =
                    DroppedCommands -
                    droppedBefore;

                diagnostics.EmittedCommands +=
                    emitted;
                diagnostics.DroppedCommands +=
                    dropped;

                Trace(
                    rule,
                    value,
                    EventRuntimeTraceOutcome.Matched,
                    emitted,
                    dropped);

                if (rule.StopAfterMatch)
                {
                    break;
                }
            }

            EmittedCommands += output.Count;
            return output.Count;
        }

        public void ResetState()
        {
            State.Clear();
        }

        public EventRuntimeRuleDiagnostics[]
            GetRuleDiagnostics()
        {
            var count = 0;

            foreach (var rule in _rules)
            {
                if (rule != null)
                {
                    count++;
                }
            }

            var result =
                new EventRuntimeRuleDiagnostics[
                    count];

            var index = 0;

            foreach (var rule in _rules)
            {
                if (rule == null)
                {
                    continue;
                }

                var counter =
                    GetRuleCounter(rule);

                result[index++] =
                    new EventRuntimeRuleDiagnostics(
                        rule.Id,
                        counter.EvaluatedEvents,
                        counter.FilterRejectedEvents,
                        counter.ConditionRejectedEvents,
                        counter.CooldownSuppressedEvents,
                        counter.RateLimitSuppressedEvents,
                        counter.MatchedEvents,
                        counter.EmittedCommands,
                        counter.DroppedCommands,
                        counter.LastMatchedTimestampUs);
            }

            return result;
        }

        private RuleDiagnosticsCounter
            GetRuleCounter(
                EventRuntimeRule rule)
        {
            if (!_ruleDiagnostics.TryGetValue(
                    rule,
                    out var counter))
            {
                counter =
                    new RuleDiagnosticsCounter();
                _ruleDiagnostics[rule] =
                    counter;
            }

            return counter;
        }

        private void Trace(
            EventRuntimeRule rule,
            NormalizedEvent value,
            EventRuntimeTraceOutcome outcome,
            int emittedCommands,
            long droppedCommands)
        {
            if (!TraceEnabled ||
                TraceEmitted == null)
            {
                return;
            }

            TraceEmitted.Invoke(
                new EventRuntimeTraceEntry(
                    rule?.Id,
                    value.Sequence,
                    value.TimestampUs,
                    outcome,
                    emittedCommands,
                    droppedCommands));
        }

        private bool IsCoolingDown(
            EventRuntimeRule rule,
            long timestampUs)
        {
            if (rule.CooldownSeconds <= 0.0 ||
                double.IsNaN(rule.CooldownSeconds) ||
                double.IsInfinity(rule.CooldownSeconds) ||
                timestampUs <= 0 ||
                !_lastRuleExecutionUs.TryGetValue(
                    rule,
                    out var previousUs))
            {
                return false;
            }

            var cooldownUs =
                rule.CooldownSeconds *
                1_000_000.0;

            if (cooldownUs >= long.MaxValue)
            {
                return true;
            }

            return
                timestampUs - previousUs <
                (long)cooldownUs;
        }

        private bool IsRateLimited(
            EventRuntimeRule rule,
            long timestampUs)
        {
            if (!TryGetRateLimit(
                    rule,
                    out var windowUs,
                    out var maxExecutions) ||
                timestampUs <= 0 ||
                !_rateWindows.TryGetValue(
                    rule,
                    out var state))
            {
                return false;
            }

            if (timestampUs < state.WindowStartUs ||
                timestampUs - state.WindowStartUs >=
                    windowUs)
            {
                return false;
            }

            return state.Executions >= maxExecutions;
        }

        private void RecordExecution(
            EventRuntimeRule rule,
            long timestampUs)
        {
            if (timestampUs <= 0)
            {
                return;
            }

            _lastRuleExecutionUs[rule] =
                timestampUs;

            if (!TryGetRateLimit(
                    rule,
                    out var windowUs,
                    out _))
            {
                return;
            }

            if (!_rateWindows.TryGetValue(
                    rule,
                    out var state) ||
                timestampUs < state.WindowStartUs ||
                timestampUs - state.WindowStartUs >=
                    windowUs)
            {
                _rateWindows[rule] =
                    new RateWindowState(
                        timestampUs,
                        1);
                return;
            }

            _rateWindows[rule] =
                new RateWindowState(
                    state.WindowStartUs,
                    state.Executions + 1);
        }

        private static bool TryGetRateLimit(
            EventRuntimeRule rule,
            out long windowUs,
            out int maxExecutions)
        {
            windowUs = 0;
            maxExecutions = 0;

            if (rule == null ||
                rule.RateLimitWindowSeconds <= 0.0 ||
                double.IsNaN(
                    rule.RateLimitWindowSeconds) ||
                double.IsInfinity(
                    rule.RateLimitWindowSeconds) ||
                rule.RateLimitMaxExecutions <= 0)
            {
                return false;
            }

            var calculated =
                rule.RateLimitWindowSeconds *
                1_000_000.0;

            windowUs =
                calculated >= long.MaxValue
                    ? long.MaxValue
                    : Math.Max(
                        1L,
                        (long)calculated);

            maxExecutions =
                rule.RateLimitMaxExecutions;

            return true;
        }

        private sealed class RuleDiagnosticsCounter
        {
            public long EvaluatedEvents;
            public long FilterRejectedEvents;
            public long ConditionRejectedEvents;
            public long CooldownSuppressedEvents;
            public long RateLimitSuppressedEvents;
            public long MatchedEvents;
            public long EmittedCommands;
            public long DroppedCommands;
            public long LastMatchedTimestampUs;
        }

        private readonly struct RateWindowState
        {
            public RateWindowState(
                long windowStartUs,
                int executions)
            {
                WindowStartUs = windowStartUs;
                Executions = executions;
            }

            public long WindowStartUs { get; }
            public int Executions { get; }
        }

        private bool ConditionsPass(EventRuntimeRule rule)
        {
            if (rule.Conditions == null)
            {
                return true;
            }

            foreach (var condition in rule.Conditions)
            {
                if (condition != null &&
                    !condition.Evaluate(State))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyMutations(
            EventRuntimeRule rule,
            NormalizedEvent value)
        {
            if (rule.StateMutations == null)
            {
                return;
            }

            foreach (var mutation in rule.StateMutations)
            {
                mutation?.Apply(value, State);
            }
        }

        private void EmitActions(
            EventRuntimeRule rule,
            NormalizedEvent value,
            List<EventActionCommand> output)
        {
            if (rule.Actions == null)
            {
                return;
            }

            foreach (var action in rule.Actions)
            {
                if (action == null ||
                    string.IsNullOrWhiteSpace(action.ActionType))
                {
                    continue;
                }

                if (output.Count >= MaxCommandsPerEvent)
                {
                    DroppedCommands++;
                    continue;
                }

                output.Add(action.Build(rule.Id, value));
            }
        }
    }
}
