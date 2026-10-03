namespace VCR.Runtime.Tracking.Mixing
{
    public readonly struct MotionApplicationAvailability
    {
        public MotionApplicationAvailability(
            bool poseAvailable,
            bool expressionsAvailable)
        {
            PoseAvailable = poseAvailable;
            ExpressionsAvailable = expressionsAvailable;
        }

        public bool PoseAvailable { get; }
        public bool ExpressionsAvailable { get; }
    }

    /// <summary>
    /// Resolves application-domain availability after routing/mixing.
    ///
    /// Full-body pose remains tied to visual performer/source presence.
    /// Expressions are intentionally independent: an audio or other
    /// expression-only fallback may remain valid without a full-body source and
    /// must never become performer-presence evidence by itself.
    /// </summary>
    public static class MotionApplicationAvailabilityResolver
    {
        public static MotionApplicationAvailability Resolve(
            TrackingPresenceSnapshot? presence,
            bool hasPoseFrame,
            bool hasExpressionFrame)
        {
            var posePresenceAvailable =
                !presence.HasValue ||
                (presence.Value.SubjectState ==
                    SubjectPresenceState.Present &&
                 presence.Value.FullBodySourceAvailable &&
                 presence.Value.FullBodySubjectEvidence);

            return new MotionApplicationAvailability(
                posePresenceAvailable &&
                    hasPoseFrame,
                hasExpressionFrame);
        }
    }
}
