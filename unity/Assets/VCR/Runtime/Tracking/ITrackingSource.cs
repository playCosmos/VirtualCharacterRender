using System;

namespace VCR.Runtime.Tracking
{
    public interface ITrackingSource : IDisposable
    {
        string SourceId { get; }
        TrackingSourceKind Kind { get; }
        TrackingRegion Regions { get; }
        TrackingSourceHealth Health { get; }

        void Start();
        void Stop();

        /// <summary>
        /// Returns the freshest available frame.
        /// Implementations must not require consumers to drain stale frames.
        /// </summary>
        bool TryTakeLatest(out TrackingFrame frame);
    }
}
