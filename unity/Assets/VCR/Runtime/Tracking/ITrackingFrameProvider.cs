namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Source-neutral non-destructive snapshot provider.
    ///
    /// Multiple consumers may read the same latest frame. Consumers use the
    /// frame sequence/timestamp to decide whether it is new for their purpose.
    /// </summary>
    public interface ITrackingFrameProvider
    {
        bool TryGetLatestFace(out TrackingFrame frame);
        bool TryGetLatestBodyHands(out TrackingFrame frame);
    }
}
