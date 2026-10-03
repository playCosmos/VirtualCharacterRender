namespace VCR.Runtime.Environment
{
    public readonly struct EnvironmentTransitionContext
    {
        public EnvironmentTransitionContext(
            long sequence,
            long timestampUs,
            string previousStateId,
            string stateId,
            EnvironmentTransitionMode mode,
            float progress,
            float deltaSeconds)
        {
            Sequence = sequence;
            TimestampUs = timestampUs;
            PreviousStateId = previousStateId;
            StateId = stateId;
            Mode = mode;
            Progress = progress;
            DeltaSeconds = deltaSeconds;
        }

        public long Sequence { get; }
        public long TimestampUs { get; }
        public string PreviousStateId { get; }
        public string StateId { get; }
        public EnvironmentTransitionMode Mode { get; }
        public float Progress { get; }
        public float DeltaSeconds { get; }
    }
}
