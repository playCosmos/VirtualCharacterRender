namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Marker for a provider that already resolved source priority/routing.
    /// Character targets prefer this over direct device providers when both
    /// exist in the same scene.
    /// </summary>
    public interface ITrackingRouteProvider :
        ITrackingFrameProvider,
        ITrackingPresenceProvider
    {
    }
}
