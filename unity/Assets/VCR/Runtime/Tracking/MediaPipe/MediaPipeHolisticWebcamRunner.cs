using System.Collections;
using System.Diagnostics;
using System.IO;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.Rendering;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// P0 desktop webcam runner.
    ///
    /// Camera capture resolution/FPS are deliberately configurable and are not
    /// tied to the 720p/1080p render-output targets.
    /// </summary>
    public sealed class MediaPipeHolisticWebcamRunner : MonoBehaviour
    {
        private const string ModelRelativePath = "VCR/Models/holistic_landmarker.bytes";

        [Header("Camera")]
        [SerializeField] private string deviceName = "";
        [SerializeField, Min(160)] private int requestedWidth = 640;
        [SerializeField, Min(120)] private int requestedHeight = 480;
        [SerializeField, Range(1, 120)] private int requestedFps = 30;
        [SerializeField] private bool flipHorizontally = false;
        [SerializeField] private bool flipVertically = true;

        [Header("P0")]
        [SerializeField, Range(1, 4)] private int textureFramePoolSize = 2;
        [SerializeField] private bool logTrackingRate = true;

        private readonly Stopwatch _clock = new();

        private WebCamTexture _webcam;
        private TextureFramePool _textureFramePool;
        private MediaPipeHolisticSource _source;
        private Coroutine _runCoroutine;

        private long _lastObservedSequence;
        private int _observedFrames;
        private float _nextRateLogTime;

        public ITrackingSource TrackingSource => _source;

        private IEnumerator Start()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("VCR P0: webcam permission was not granted.");
                yield break;
            }

            if (WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("VCR P0: no webcam devices were found.");
                yield break;
            }

            var selectedDevice = SelectDeviceName();
            _webcam = new WebCamTexture(
                selectedDevice,
                requestedWidth,
                requestedHeight,
                requestedFps);
            _webcam.Play();

            // macOS may report a placeholder size until the first frames arrive.
            yield return new WaitUntil(() =>
                _webcam != null &&
                _webcam.isPlaying &&
                _webcam.width > 16 &&
                _webcam.height > 16);

            var modelPath = Path.Combine(
                Application.streamingAssetsPath,
                ModelRelativePath);

            if (!File.Exists(modelPath))
            {
                Debug.LogError(
                    "VCR P0: MediaPipe model is missing. Run tools/bootstrap-mediapipe before opening Unity. " +
                    modelPath);
                yield break;
            }

            byte[] modelBytes;
            try
            {
                modelBytes = File.ReadAllBytes(modelPath);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                yield break;
            }

            _source = new MediaPipeHolisticSource(modelBytes);
            try
            {
                _source.Start();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("VCR P0: failed to start MediaPipe Holistic.");
                Debug.LogException(exception);
                yield break;
            }

            _textureFramePool = new TextureFramePool(
                _webcam.width,
                _webcam.height,
                TextureFormat.RGBA32,
                textureFramePoolSize);

            _clock.Restart();
            _nextRateLogTime = Time.unscaledTime + 5f;
            _runCoroutine = StartCoroutine(RunTracking());

            Debug.Log(
                $"VCR P0 MediaPipe started: device='{selectedDevice}', " +
                $"actual={_webcam.width}x{_webcam.height}, requestedFps={requestedFps}");
        }

        private IEnumerator RunTracking()
        {
            AsyncGPUReadbackRequest readback = default;
            var waitForReadback = new WaitUntil(() => readback.done);

            while (enabled && _source != null)
            {
                if (_webcam == null || !_webcam.isPlaying || !_webcam.didUpdateThisFrame)
                {
                    yield return null;
                    continue;
                }

                if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    // No backlog: skip this webcam frame instead of allocating.
                    yield return null;
                    continue;
                }

                readback = textureFrame.ReadTextureAsync(
                    _webcam,
                    flipHorizontally,
                    flipVertically);

                yield return waitForReadback;

                if (readback.hasError)
                {
                    textureFrame.Release();
                    yield return null;
                    continue;
                }

                var image = textureFrame.BuildCPUImage();
                textureFrame.Release();

                // Packet.CreateImageAt inside DetectAsync moves/disposes Image ownership.
                _source.SubmitImage(image, _clock.ElapsedMilliseconds);

                ObserveLatestFrameForDiagnostics();
            }
        }

        private void Update()
        {
            if (!logTrackingRate || _source == null)
            {
                return;
            }

            if (Time.unscaledTime < _nextRateLogTime)
            {
                return;
            }

            var health = _source.Health;
            Debug.Log(
                $"VCR P0 tracking: callbacks={_observedFrames}/5s, " +
                $"state={health.State}, lastUpdateUs={health.LastUpdateTimestampUs}");

            _observedFrames = 0;
            _nextRateLogTime = Time.unscaledTime + 5f;
        }

        private void ObserveLatestFrameForDiagnostics()
        {
            if (!_source.TryTakeLatest(out var frame))
            {
                return;
            }

            if (frame.Sequence == _lastObservedSequence)
            {
                return;
            }

            _lastObservedSequence = frame.Sequence;
            _observedFrames++;
        }

        private string SelectDeviceName()
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (device.name == deviceName)
                    {
                        return device.name;
                    }
                }

                Debug.LogWarning(
                    $"VCR P0: requested webcam '{deviceName}' not found; using the first device.");
            }

            return WebCamTexture.devices[0].name;
        }

        private void OnDestroy()
        {
            if (_runCoroutine != null)
            {
                StopCoroutine(_runCoroutine);
                _runCoroutine = null;
            }

            _source?.Dispose();
            _source = null;

            _textureFramePool?.Dispose();
            _textureFramePool = null;

            if (_webcam != null)
            {
                if (_webcam.isPlaying)
                {
                    _webcam.Stop();
                }

                Destroy(_webcam);
                _webcam = null;
            }
        }
    }
}
