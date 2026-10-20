namespace VCR.Runtime.EventRuntime
{
    public readonly struct EventRuntimeRuleDiagnostics
    {
        public EventRuntimeRuleDiagnostics(
            string ruleId,
            long evaluatedEvents,
            long filterRejectedEvents,
            long conditionRejectedEvents,
            long cooldownSuppressedEvents,
            long rateLimitSuppressedEvents,
            long matchedEvents,
            long emittedCommands,
            long droppedCommands,
            long lastMatchedTimestampUs)
        {
            RuleId = ruleId;
            EvaluatedEvents = evaluatedEvents;
            FilterRejectedEvents = filterRejectedEvents;
            ConditionRejectedEvents = conditionRejectedEvents;
            CooldownSuppressedEvents = cooldownSuppressedEvents;
            RateLimitSuppressedEvents = rateLimitSuppressedEvents;
            MatchedEvents = matchedEvents;
            EmittedCommands = emittedCommands;
            DroppedCommands = droppedCommands;
            LastMatchedTimestampUs =
                lastMatchedTimestampUs;
        }

        public string RuleId { get; }
        public long EvaluatedEvents { get; }
        public long FilterRejectedEvents { get; }
        public long ConditionRejectedEvents { get; }
        public long CooldownSuppressedEvents { get; }
        public long RateLimitSuppressedEvents { get; }
        public long MatchedEvents { get; }
        public long EmittedCommands { get; }
        public long DroppedCommands { get; }
        public long LastMatchedTimestampUs { get; }
    }
}
