namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral snapshot of the final character motion state for protocol
    /// output/recording. Implementations may sample a renderer/backend.
    /// </summary>
    public interface INormalizedMotionSnapshotProvider
    {
        bool TryCaptureMotion(
            out TrackingFrame frame);
    }

    /// <summary>
    /// Optional selective capture contract for outputs that do not need every
    /// motion domain on every sample.
    /// </summary>
    public interface ISelectiveNormalizedMotionSnapshotProvider :
        INormalizedMotionSnapshotProvider
    {
        bool TryCaptureMotion(
            in NormalizedMotionSnapshotRequest request,
            out TrackingFrame frame);
    }

    public readonly struct NormalizedMotionSnapshotRequest
    {
        public NormalizedMotionSnapshotRequest(
            bool includeHumanoidPose,
            bool includeExpressions)
        {
            IncludeHumanoidPose =
                includeHumanoidPose;
            IncludeExpressions =
                includeExpressions;
        }

        public bool IncludeHumanoidPose { get; }
        public bool IncludeExpressions { get; }

        public bool HasAnyDomain =>
            IncludeHumanoidPose ||
            IncludeExpressions;

        public static NormalizedMotionSnapshotRequest Full =>
            new(
                includeHumanoidPose: true,
                includeExpressions: true);
    }
}
