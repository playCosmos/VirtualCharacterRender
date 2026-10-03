namespace VCR.Runtime.Environment
{
    public readonly struct EnvironmentTransitionStatus
    {
        public EnvironmentTransitionStatus(
            bool active,
            EnvironmentTransitionMode mode,
            string previousStateId,
            string stateId,
            long startedAtTimestampUs,
            float durationSeconds,
            float progress)
        {
            Active = active;
            Mode = mode;
            PreviousStateId = previousStateId;
            StateId = stateId;
            StartedAtTimestampUs = startedAtTimestampUs;
            DurationSeconds = durationSeconds;
            Progress = progress;
        }

        public bool Active { get; }
        public EnvironmentTransitionMode Mode { get; }
        public string PreviousStateId { get; }
        public string StateId { get; }
        public long StartedAtTimestampUs { get; }
        public float DurationSeconds { get; }
        public float Progress { get; }
    }
}
