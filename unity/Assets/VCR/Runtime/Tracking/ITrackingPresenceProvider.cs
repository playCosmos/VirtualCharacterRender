namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Non-destructive stable presence state derived from tracking.
    /// Consumers use Sequence to process transitions once.
    /// </summary>
    public interface ITrackingPresenceProvider
    {
        TrackingPresenceSnapshot Presence { get; }
    }
}
