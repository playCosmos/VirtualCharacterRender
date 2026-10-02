namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Marker for the final one-performer tracking/motion mix presented to
    /// character targets. Mixers are already routed, so targets prefer this
    /// over a raw route provider when both exist.
    /// </summary>
    public interface ITrackingMixProvider :
        ITrackingRouteProvider
    {
    }
}
