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

        public EventRuntimeStateStore State { get; } = new();

        public long ProcessedEvents { get; private set; }
        public long MatchedRules { get; private set; }
        public long EmittedCommands { get; private set; }
        public long DroppedCommands { get; private set; }

        public int MaxCommandsPerEvent
        {
            get => _maxCommandsPerEvent;
            set => _maxCommandsPerEvent =
                Math.Max(1, Math.Min(256, value));
        }

        public void SetRules(params EventRuntimeRule[] rules)
        {
            _rules =
                rules == null
                    ? Array.Empty<EventRuntimeRule>()
                    : (EventRuntimeRule[])rules.Clone();
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
                    !rule.Enabled ||
                    !(rule.Filter?.Matches(value) ?? true) ||
                    !ConditionsPass(rule))
                {
                    continue;
                }

                MatchedRules++;
                ApplyMutations(rule, value);
                EmitActions(rule, value, output);

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
