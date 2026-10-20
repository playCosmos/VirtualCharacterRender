namespace VCR.Runtime.Environment
{
    public readonly struct EnvironmentUpdateContext
    {
        public EnvironmentUpdateContext(
            long sequence,
            long timestampUs,
            float deltaSeconds,
            EnvironmentUpdateReason reason,
            string stateId)
        {
            Sequence = sequence;
            TimestampUs = timestampUs;
            DeltaSeconds = deltaSeconds;
            Reason = reason;
            StateId = stateId;
        }

        public long Sequence { get; }
        public long TimestampUs { get; }
        public float DeltaSeconds { get; }
        public EnvironmentUpdateReason Reason { get; }
        public string StateId { get; }
    }
}
