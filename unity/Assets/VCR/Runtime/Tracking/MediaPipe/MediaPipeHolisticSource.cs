using System;
using System.Threading;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HolisticLandmarker;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// One-performer MediaPipe Holistic source using LIVE_STREAM CPU inference.
    ///
    /// Image ownership follows MediaPipe move semantics: DetectAsync transfers the
    /// submitted Image into the native packet. Callers must not reuse or dispose
    /// the Image after SubmitImage returns successfully.
    /// </summary>
    public sealed class MediaPipeHolisticSource : ITrackingSource
    {
        private readonly object _sync = new();
        private readonly byte[] _modelBytes;
        private readonly MediaPipeHolisticCallbackBridge _bridge = new();

        private HolisticLandmarker _landmarker;
        private TrackingSourceHealth _health;
        private int _acceptCallbacks;
        private bool _disposed;

        public MediaPipeHolisticSource(byte[] modelBytes, string sourceId = "mediapipe-holistic-webcam")
        {
            _modelBytes = modelBytes ?? throw new ArgumentNullException(nameof(modelBytes));
            if (_modelBytes.Length == 0)
            {
                throw new ArgumentException("Model buffer is empty.", nameof(modelBytes));
            }

            SourceId = sourceId;
            _health = new TrackingSourceHealth(
                TrackingSourceHealthState.Stopped,
                0,
                float.NaN,
                null);
        }

        public string SourceId { get; }

        public TrackingSourceKind Kind => TrackingSourceKind.MediaPipeHolisticWebcam;

        public TrackingRegion Regions => TrackingRegion.Baseline;

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
                        outputFaceBlendshapes: true,
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

            lock (_sync)
            {
                ThrowIfDisposed();

                if (_landmarker == null)
                {
                    throw new InvalidOperationException("Tracking source is not started.");
                }

                _landmarker.DetectAsync(image, timestampMillisec);
            }
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

            _bridge.OnResult(in result, image, timestampMillisec);

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
