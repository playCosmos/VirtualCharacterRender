using System;
using System.Collections.Generic;
using VCR.Runtime.Core;
using VCR.Runtime.Protocols.Osc;

namespace VCR.Runtime.Protocols.Vmc
{
    public sealed class VmcTrackingSource : ITrackingSource
    {
        private readonly object _sync = new();
        private readonly LatestValueBuffer<TrackingFrame> _latest = new();
        private readonly VmcFrameAccumulator _accumulator;

        private TrackingSourceHealth _health;
        private bool _started;
        private bool _disposed;

        public VmcTrackingSource(string sourceId = "vmc-udp")
        {
            SourceId = sourceId;
            _accumulator = new VmcFrameAccumulator(SourceId);
            _health = new TrackingSourceHealth(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        }

        public string SourceId { get; }
        public TrackingSourceKind Kind => TrackingSourceKind.Vmc;
        public TrackingRegion Regions =>
            TrackingRegion.FullBody |
            TrackingRegion.Face |
            TrackingRegion.Head;

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

        public bool Process(
            IReadOnlyList<OscMessage> messages,
            long arrivalTimestampUs)
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_started)
                {
                    return false;
                }
            }

            if (!_accumulator.Process(
                messages,
                arrivalTimestampUs,
                out var frame))
            {
                lock (_sync)
                {
                    _health = new TrackingSourceHealth(
                        TrackingSourceHealthState.Healthy,
                        arrivalTimestampUs,
                        _health.Confidence,
                        null);
                }
                return false;
            }

            _latest.Publish(frame);

            lock (_sync)
            {
                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Healthy,
                    arrivalTimestampUs,
                    frame.Confidence,
                    null);
            }

            return true;
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            frame = _latest.TakeLatest();
            return frame != null;
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

        public void Stop()
        {
            lock (_sync)
            {
                _started = false;
                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Stopped,
                    _health.LastUpdateTimestampUs,
                    _health.Confidence,
                    null);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VmcTrackingSource));
            }
        }
    }
}
