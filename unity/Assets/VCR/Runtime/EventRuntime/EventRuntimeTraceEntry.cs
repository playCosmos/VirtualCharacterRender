namespace VCR.Runtime.EventRuntime
{
    public enum EventRuntimeTraceOutcome
    {
        FilterRejected = 0,
        ConditionRejected = 1,
        CooldownSuppressed = 2,
        RateLimitSuppressed = 3,
        Matched = 4
    }

    public readonly struct EventRuntimeTraceEntry
    {
        public EventRuntimeTraceEntry(
            string ruleId,
            long eventSequence,
            long eventTimestampUs,
            EventRuntimeTraceOutcome outcome,
            int emittedCommands,
            long droppedCommands)
        {
            RuleId = ruleId;
            EventSequence = eventSequence;
            EventTimestampUs = eventTimestampUs;
            Outcome = outcome;
            EmittedCommands = emittedCommands;
            DroppedCommands = droppedCommands;
        }

        public string RuleId { get; }
        public long EventSequence { get; }
        public long EventTimestampUs { get; }
        public EventRuntimeTraceOutcome Outcome { get; }
        public int EmittedCommands { get; }
        public long DroppedCommands { get; }
    }
}
