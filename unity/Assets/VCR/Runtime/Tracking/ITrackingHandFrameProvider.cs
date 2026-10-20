namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Optional source-neutral hand-only snapshot route.
    ///
    /// This is separate from ITrackingFrameProvider.TryGetLatestBodyHands so
    /// the body router can preserve child-frame identity and remain allocation
    /// free while a specialist hand source (for example Ultraleap) overrides
    /// only the hand domain.
    /// </summary>
    public interface ITrackingHandFrameProvider
    {
        bool TryGetLatestHands(
            out TrackingFrame frame);
    }
}
