namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral tracking frame envelope.
    ///
    /// Concrete normalized face/hand/body payloads are added only after P0
    /// verifies the MediaPipe/ARKit coordinate and confidence conventions.
    /// </summary>
    public sealed class TrackingFrame
    {
        public TrackingFrame(
            long sequence,
            long sourceTimestampUs,
            TrackingRegion validRegions,
            float confidence,
            bool subjectDetected)
        {
            Sequence = sequence;
            SourceTimestampUs = sourceTimestampUs;
            ValidRegions = validRegions;
            Confidence = confidence;
            SubjectDetected = subjectDetected;
        }

        public long Sequence { get; }
        public long SourceTimestampUs { get; }
        public TrackingRegion ValidRegions { get; }
        public float Confidence { get; }
        public bool SubjectDetected { get; }
    }
}
