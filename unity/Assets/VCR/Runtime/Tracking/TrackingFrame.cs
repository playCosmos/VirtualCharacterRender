namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral immutable tracking frame envelope.
    ///
    /// A source may populate only the domains it owns. Region routing/mixing
    /// combines frames from multiple sources before character application.
    /// </summary>
    public sealed class TrackingFrame
    {
        public TrackingFrame(
            long sequence,
            long sourceTimestampUs,
            TrackingRegion validRegions,
            float confidence,
            bool subjectDetected,
            NormalizedFaceState face = null,
            NormalizedUpperBodyState upperBody = null,
            NormalizedHandState leftHand = null,
            NormalizedHandState rightHand = null)
        {
            Sequence = sequence;
            SourceTimestampUs = sourceTimestampUs;
            ValidRegions = validRegions;
            Confidence = confidence;
            SubjectDetected = subjectDetected;
            Face = face;
            UpperBody = upperBody;
            LeftHand = leftHand;
            RightHand = rightHand;
        }

        public long Sequence { get; }
        public long SourceTimestampUs { get; }
        public TrackingRegion ValidRegions { get; }
        public float Confidence { get; }
        public bool SubjectDetected { get; }

        public NormalizedFaceState Face { get; }
        public NormalizedUpperBodyState UpperBody { get; }
        public NormalizedHandState LeftHand { get; }
        public NormalizedHandState RightHand { get; }
    }
}
