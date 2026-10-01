namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral snapshot of the final character motion state for protocol
    /// output/recording. Implementations may sample a renderer/backend.
    /// </summary>
    public interface INormalizedMotionSnapshotProvider
    {
        bool TryCaptureMotion(out TrackingFrame frame);
    }
}
