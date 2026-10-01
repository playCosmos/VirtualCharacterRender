using System;
using System.Collections.Generic;
using System.Threading;
using VCR.Runtime.Core;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Independent one-face MediaPipe source for face/eyes/mouth/head.
    /// It intentionally does not depend on pose validity.
    /// </summary>
    public sealed class MediaPipeFaceSource : ITrackingSource
    {
        private readonly object _sync = new();
        private readonly byte[] _modelBytes;
        private readonly MediaPipeFaceCallbackBridge _bridge;
        private readonly Dictionary<long, long> _submittedAtUs = new();

        private FaceLandmarker _landmarker;
        private TrackingSourceHealth _health;
        private int _acceptCallbacks;
        private long _resultCount;
        private long _lastProcessingLatencyUs;
        private bool _disposed;

        public MediaPipeFaceSource(byte[] modelBytes, string sourceId = "mediapipe-face-webcam")
        {
            _modelBytes = modelBytes ?? throw new ArgumentNullException(nameof(modelBytes));
            if (_modelBytes.Length == 0)
            {
                throw new ArgumentException("Model buffer is empty.", nameof(modelBytes));
            }

            SourceId = sourceId;
            _bridge = new MediaPipeFaceCallbackBridge(SourceId);
            _health = new TrackingSourceHealth(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        }

        public string SourceId { get; }

        public TrackingSourceKind Kind => TrackingSourceKind.MediaPipeFaceWebcam;

        public TrackingRegion Regions => TrackingRegion.Face | TrackingRegion.Head;

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
                    var options = new FaceLandmarkerOptions(
                        new BaseOptions(
                            BaseOptions.Delegate.CPU,
                            modelAssetBuffer: _modelBytes),
                        runningMode: RunningMode.LIVE_STREAM,
                        numFaces: 1,
                        outputFaceBlendshapes: true,
                        outputFaceTransformationMatrixes: true,
                        resultCallback: OnResult);

                    _landmarker = FaceLandmarker.CreateFromOptions(options);
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

            FaceLandmarker landmarker;
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

            landmarker.DetectAsync(image, timestampMillisec);
        }

        public bool TryTakeLatest(out TrackingFrame frame)
        {
            return _bridge.TryTakeLatest(out frame);
        }

        public void Stop()
        {
            FaceLandmarker landmarker;

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
            FaceLandmarkerResult result,
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
                throw new ObjectDisposedException(nameof(MediaPipeFaceSource));
            }
        }
    }
}
