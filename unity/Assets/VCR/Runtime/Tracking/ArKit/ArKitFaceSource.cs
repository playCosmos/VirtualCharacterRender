using System;
using System.Threading;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.ArKit
{
    /// <summary>
    /// Transport-neutral ARKit-compatible face source.
    /// A UDP/TCP/app adapter converts incoming data to NormalizedFaceState and
    /// publishes it here.
    /// </summary>
    public sealed class ArKitFaceSource : ITrackingSource
    {
        private readonly object _sync = new();
        private readonly LatestValueBuffer<TrackingFrame> _latest = new();

        private TrackingSourceHealth _health;
        private long _sequence;
        private bool _started;
        private bool _disposed;

        public ArKitFaceSource(string sourceId = "arkit-face")
        {
            SourceId = sourceId;
            _health = new TrackingSourceHealth(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        }

        public string SourceId { get; }

        public TrackingSourceKind Kind => TrackingSourceKind.ArKitFace;

        public TrackingRegion Regions =>
            TrackingRegion.Face | TrackingRegion.Head;

        public TrackingSourceHealth Health
        {
            get
            {
                lock (_sync)
                {
                    return _health;
                }
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _started = true;
                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Starting,
                    0,
                    float.NaN,
                    null);
            }
        }

        public void Publish(
            NormalizedFaceState face,
            long timestampUs)
        {
            if (face == null)
            {
                PublishNoSubject(timestampUs);
                return;
            }

            PublishFrame(
                face,
                timestampUs,
                subjectDetected: true);
        }

        public void PublishNoSubject(long timestampUs)
        {
            PublishFrame(
                face: null,
                timestampUs,
                subjectDetected: false);
        }

        public void MarkSourceLost(string error = null)
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.SourceLost,
                    _health.LastUpdateTimestampUs,
                    _health.Confidence,
                    error);
            }
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
        }

        public void Stop()
        {
            lock (_sync)
            {
                _started = false;
                _latest.TakeLatest();
                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Stopped,
                    _health.LastUpdateTimestampUs,
                    _health.Confidence,
                    null);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _started = false;
                _disposed = true;
                _latest.TakeLatest();
                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Stopped,
                    _health.LastUpdateTimestampUs,
                    _health.Confidence,
                    null);
            }
        }

        private void PublishFrame(
            NormalizedFaceState face,
            long timestampUs,
            bool subjectDetected)
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (!_started)
                {
                    throw new InvalidOperationException(
                        "ARKit face source is not started.");
                }

                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Healthy,
                    timestampUs,
                    subjectDetected ? 1f : 0f,
                    null);

                var sequence =
                    Interlocked.Increment(
                        ref _sequence);
                var regions =
                    subjectDetected
                        ? TrackingRegion.Face |
                          TrackingRegion.Head
                        : TrackingRegion.None;

                _latest.Publish(
                    new TrackingFrame(
                        sequence,
                        timestampUs,
                        regions,
                        subjectDetected ? 1f : 0f,
                        subjectDetected,
                        face: face,
                        sourceId: SourceId,
                        runtimeTimestampUs:
                            MonotonicClock
                                .NowMicroseconds()));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArKitFaceSource));
            }
        }
    }
}
