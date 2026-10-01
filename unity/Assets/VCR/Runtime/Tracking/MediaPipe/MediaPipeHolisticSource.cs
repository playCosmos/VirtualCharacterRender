using System;
using System.Collections.Generic;
using System.Threading;
using VCR.Runtime.Core;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HolisticLandmarker;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// MediaPipe Holistic source used only for hands and upper body.
    ///
    /// Face/head ownership belongs to FaceLandmarker or ARKit (ADR-0025).
    /// </summary>
    public sealed class MediaPipeHolisticSource : ITrackingSource
    {
        private readonly object _sync = new();
        private readonly byte[] _modelBytes;
        private readonly MediaPipeHolisticCallbackBridge _bridge;
        private readonly Dictionary<long, long> _submittedAtUs = new();

        private HolisticLandmarker _landmarker;
        private TrackingSourceHealth _health;
        private int _acceptCallbacks;
        private long _resultCount;
        private long _lastProcessingLatencyUs;
        private bool _disposed;

        public MediaPipeHolisticSource(byte[] modelBytes, string sourceId = "mediapipe-holistic-webcam")
        {
            _modelBytes = modelBytes ?? throw new ArgumentNullException(nameof(modelBytes));
            if (_modelBytes.Length == 0)
            {
                throw new ArgumentException("Model buffer is empty.", nameof(modelBytes));
            }

            SourceId = sourceId;
            _bridge = new MediaPipeHolisticCallbackBridge(SourceId);
            _health = new TrackingSourceHealth(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        }

        public string SourceId { get; }

        public TrackingSourceKind Kind => TrackingSourceKind.MediaPipeHolisticWebcam;

        public TrackingRegion Regions => TrackingRegion.Hands | TrackingRegion.UpperBody;

        public long ResultCount => Interlocked.Read(ref _resultCount);
        public long LastProcessingLatencyUs =>
            Interlocked.Read(ref _lastProcessingLatencyUs);

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

                if (_landmarker != null)
                {
                    return;
                }

                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Starting,
                    0,
                    float.NaN,
                    null);

                try
                {
                    var options = new HolisticLandmarkerOptions(
                        new BaseOptions(
                            BaseOptions.Delegate.CPU,
                            modelAssetBuffer: _modelBytes),
                        runningMode: RunningMode.LIVE_STREAM,
                        outputFaceBlendshapes: false,
                        outputSegmentationMask: false,
                        resultCallback: OnResult);

                    _landmarker = HolisticLandmarker.CreateFromOptions(options);
                    Volatile.Write(ref _acceptCallbacks, 1);

                    _health = new TrackingSourceHealth(
                        TrackingSourceHealthState.Healthy,
                        0,
                        float.NaN,
                        null);
                }
                catch (Exception exception)
                {
                    _landmarker = null;
                    Volatile.Write(ref _acceptCallbacks, 0);
                    _health = new TrackingSourceHealth(
                        TrackingSourceHealthState.Faulted,
                        0,
                        float.NaN,
                        exception.Message);
                    throw;
                }
            }
        }

        public void SubmitImage(Image image, long timestampMillisec)
        {
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            HolisticLandmarker landmarker;
            lock (_sync)
            {
                ThrowIfDisposed();
                landmarker = _landmarker ??
                    throw new InvalidOperationException("Tracking source is not started.");

                if (_submittedAtUs.Count > 64)
                {
                    _submittedAtUs.Clear();
                }

                _submittedAtUs[timestampMillisec] =
                    MonotonicClock.NowMicroseconds();
            }

            // Do not hold _sync while calling native code: LIVE_STREAM callbacks
            // may arrive on another thread and also need to update source health.
            landmarker.DetectAsync(image, timestampMillisec);
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            return _bridge.TryTakeLatest(out frame);
        }

        public void Stop()
        {
            HolisticLandmarker landmarker;

            lock (_sync)
            {
                Volatile.Write(ref _acceptCallbacks, 0);

                landmarker = _landmarker;
                _landmarker = null;
                _submittedAtUs.Clear();

                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Stopped,
                    _health.LastUpdateTimestampUs,
                    _health.Confidence,
                    null);
            }

            if (landmarker != null)
            {
                ((IDisposable)landmarker).Dispose();
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

        private void OnResult(
            in HolisticLandmarkerResult result,
            Image image,
            long timestampMillisec)
        {
            if (Volatile.Read(ref _acceptCallbacks) == 0)
            {
                return;
            }

            long submittedAtUs = 0;
            lock (_sync)
            {
                if (_submittedAtUs.TryGetValue(
                    timestampMillisec,
                    out submittedAtUs))
                {
                    _submittedAtUs.Remove(timestampMillisec);
                }
            }

            if (submittedAtUs > 0)
            {
                Interlocked.Exchange(
                    ref _lastProcessingLatencyUs,
                    MonotonicClock.NowMicroseconds() -
                    submittedAtUs);
            }

            long submittedAtUs = 0;
            lock (_sync)
            {
                if (_submittedAtUs.TryGetValue(
                    timestampMillisec,
                    out submittedAtUs))
                {
                    _submittedAtUs.Remove(timestampMillisec);
                }
            }

            if (submittedAtUs > 0)
            {
                Interlocked.Exchange(
                    ref _lastProcessingLatencyUs,
                    MonotonicClock.NowMicroseconds() -
                    submittedAtUs);
            }

            _bridge.OnResult(in result, image, timestampMillisec);
            Interlocked.Increment(ref _resultCount);

            lock (_sync)
            {
                if (_landmarker == null)
                {
                    return;
                }

                _health = new TrackingSourceHealth(
                    TrackingSourceHealthState.Healthy,
                    timestampMillisec * 1000L,
                    float.NaN,
                    null);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MediaPipeHolisticSource));
            }
        }
    }
}
