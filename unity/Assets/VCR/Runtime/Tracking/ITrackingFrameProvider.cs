namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral frame provider consumed by routing/mixer or P0 preview targets.
    /// Implementations may be MediaPipe, ARKit/VMC routing, or a future mixer.
    /// </summary>
    public interface ITrackingFrameProvider
    {
        bool TryTakeLatestFace(out TrackingFrame frame);
        bool TryTakeLatestBodyHands(out TrackingFrame frame);
    }
}
