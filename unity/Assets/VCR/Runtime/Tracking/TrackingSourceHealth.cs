namespace VCR.Runtime.Tracking
{
    public enum TrackingSourceHealthState
    {
        Stopped = 0,
        Starting = 1,
        Healthy = 2,
        Degraded = 3,
        SourceLost = 4,
        Faulted = 5
    }

    public readonly struct TrackingSourceHealth
    {
        public TrackingSourceHealth(
            TrackingSourceHealthState state,
            long lastUpdateTimestampUs,
            float confidence,
            string error)
        {
            State = state;
            LastUpdateTimestampUs = lastUpdateTimestampUs;
            Confidence = confidence;
            Error = error;
        }

        public TrackingSourceHealthState State { get; }
        public long LastUpdateTimestampUs { get; }
        public float Confidence { get; }
        public string Error { get; }

        public bool IsUsable =>
            State == TrackingSourceHealthState.Healthy ||
            State == TrackingSourceHealthState.Degraded;
    }
}
