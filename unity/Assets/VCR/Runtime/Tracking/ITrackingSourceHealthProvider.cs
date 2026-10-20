namespace VCR.Runtime.Tracking
{
    /// <summary>
    /// Region-addressable health view for a tracking provider that may wrap
    /// one or more internal tracking sources.
    ///
    /// Callers query one routing domain at a time. Providers with independent
    /// face/body sources return the health snapshot that owns the requested
    /// region without exposing source-specific implementation types.
    /// </summary>
    public interface ITrackingSourceHealthProvider
    {
        bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot);
    }
}
