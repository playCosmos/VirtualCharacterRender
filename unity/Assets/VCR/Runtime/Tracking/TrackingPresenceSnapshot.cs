namespace VCR.Runtime.Tracking
{
    public readonly struct TrackingPresenceSnapshot
    {
        public TrackingPresenceSnapshot(
            long sequence,
            long timestampUs,
            SubjectPresenceState subjectState,
            bool faceSourceAvailable,
            bool bodyHandsSourceAvailable,
            bool fullBodySourceAvailable,
            bool faceSubjectEvidence,
            bool bodyHandsSubjectEvidence,
            bool fullBodySubjectEvidence,
            bool anySourceAvailable,
            bool subjectEvidence,
            TrackingPresenceEvents events)
        {
            Sequence = sequence;
            TimestampUs = timestampUs;
            SubjectState = subjectState;
            FaceSourceAvailable = faceSourceAvailable;
            BodyHandsSourceAvailable = bodyHandsSourceAvailable;
            FullBodySourceAvailable = fullBodySourceAvailable;
            FaceSubjectEvidence = faceSubjectEvidence;
            BodyHandsSubjectEvidence = bodyHandsSubjectEvidence;
            FullBodySubjectEvidence = fullBodySubjectEvidence;
            AnySourceAvailable = anySourceAvailable;
            SubjectEvidence = subjectEvidence;
            Events = events;
        }

        public long Sequence { get; }
        public long TimestampUs { get; }
        public SubjectPresenceState SubjectState { get; }
        public bool FaceSourceAvailable { get; }
        public bool BodyHandsSourceAvailable { get; }
        public bool FullBodySourceAvailable { get; }
        public bool FaceSubjectEvidence { get; }
        public bool BodyHandsSubjectEvidence { get; }
        public bool FullBodySubjectEvidence { get; }
        public bool AnySourceAvailable { get; }
        public bool SubjectEvidence { get; }
        public TrackingPresenceEvents Events { get; }

        public bool HasEvent(TrackingPresenceEvents value)
        {
            return (Events & value) != 0;
        }
    }
}
