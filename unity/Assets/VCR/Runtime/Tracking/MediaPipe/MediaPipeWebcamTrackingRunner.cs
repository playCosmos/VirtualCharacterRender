using System;
using System.Collections;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using Mediapipe;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// P0 desktop webcam runner for ADR-0025.
    ///
    /// One WebCamTexture feeds two independent LIVE_STREAM tasks:
    /// FaceLandmarker at a higher priority/rate, and HolisticLandmarker at a
    /// lower configurable rate for hands/upper body.
    /// </summary>
    public sealed class MediaPipeWebcamTrackingRunner : MonoBehaviour, ITrackingFrameProvider, ITrackingPresenceProvider, IFaceTrackingActivationControl, IRuntimeMetricsSource
    {
        private const string FaceModelRelativePath =
            "VCR/Models/face_landmarker_v2_with_blendshapes.bytes";
        private const string HolisticModelRelativePath =
            "VCR/Models/holistic_landmarker.bytes";

        [Header("Camera")]
        [SerializeField] private string deviceName = "";
        [SerializeField, Min(160)] private int requestedWidth = 640;
        [SerializeField, Min(120)] private int requestedHeight = 480;
        [SerializeField, Range(1, 120)] private int requestedFps = 30;
        [SerializeField] private bool flipHorizontally = false;
        [SerializeField] private bool flipVertically = true;

        [Header("Tracking rates")]
        [SerializeField, Range(1, 120)] private int faceTargetFps = 30;
        [SerializeField, Range(1, 120)] private int holisticTargetFps = 15;
        [SerializeField] private bool mediaPipeFaceEnabled = true;

        [Header("Presence - provisional P0 defaults")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;
        [SerializeField, Min(0.1f)] private float sourceStaleSeconds = 1.0f;

        [Header("P0")]
        [SerializeField, Range(1, 4)] private int textureFramePoolSize = 2;
        [SerializeField] private bool logTrackingRate = true;

        private readonly Stopwatch _clock = new();

        private WebCamTexture _webcam;
        private TextureFramePool _faceFramePool;
        private TextureFramePool _holisticFramePool;

        private MediaPipeFaceSource _faceSource;
        private MediaPipeHolisticSource _holisticSource;

        private Coroutine _faceCoroutine;
        private Coroutine _holisticCoroutine;

        private long _lastFaceResultCount;
        private long _lastHolisticResultCount;
        private float _nextRateLogTime;

        private TrackingFrame _latestFaceFrame;
        private TrackingFrame _latestBodyHandsFrame;
        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        public ITrackingSource FaceTrackingSource => _faceSource;
        public ITrackingSource BodyHandTrackingSource => _holisticSource;
        public bool MediaPipeFaceEnabled => mediaPipeFaceEnabled;
        public bool FaceTrackingEnabled => mediaPipeFaceEnabled;
        public TrackingPresenceSnapshot Presence => _presence;

        public bool TryGetLatestFace(out TrackingFrame frame)
        {
            frame = _latestFaceFrame;
            return frame != null;
        }

        public bool TryGetLatestBodyHands(out TrackingFrame frame)
        {
            frame = _latestBodyHandsFrame;
            return frame != null;
        }

        public bool TryGetLatestHumanoidPose(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }

        public bool TryGetLatestExpressions(out TrackingFrame frame)
        {
            frame = null;
            return false;
        }


        private IEnumerator Start()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                Debug.LogError("VCR P0: webcam permission was not granted.");
                yield break;
            }

            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogError("VCR P0: no webcam devices were found.");
                yield break;
            }

            var selectedDevice = SelectDeviceName(devices);
            _webcam = new WebCamTexture(
                selectedDevice,
                requestedWidth,
                requestedHeight,
                requestedFps);
            _webcam.Play();

            // macOS can report a placeholder size until camera frames arrive.
            yield return new WaitUntil(() =>
                _webcam != null &&
                _webcam.isPlaying &&
                _webcam.width > 16 &&
                _webcam.height > 16);

            byte[] faceModel;
            byte[] holisticModel;
            try
            {
                faceModel = LoadModel(FaceModelRelativePath);
                holisticModel = LoadModel(HolisticModelRelativePath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR P0: MediaPipe models are missing or unreadable. " +
                    "Run tools/bootstrap-mediapipe before opening Unity.");
                Debug.LogException(exception);
                yield break;
            }

            _faceSource = new MediaPipeFaceSource(faceModel);
            _holisticSource = new MediaPipeHolisticSource(holisticModel);

            try
            {
                if (mediaPipeFaceEnabled)
                {
                    _faceSource.Start();
                }

                _holisticSource.Start();
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR P0: failed to start MediaPipe tracking.");
                Debug.LogException(exception);
                yield break;
            }

            _faceFramePool = new TextureFramePool(
                _webcam.width,
                _webcam.height,
                TextureFormat.RGBA32,
                textureFramePoolSize);

            _holisticFramePool = new TextureFramePool(
                _webcam.width,
                _webcam.height,
                TextureFormat.RGBA32,
                textureFramePoolSize);

            _clock.Restart();
            _presenceResolver = new TrackingPresenceResolver(
                SecondsToMicroseconds(subjectLostGraceSeconds),
                SecondsToMicroseconds(subjectRestoreStabilitySeconds),
                SecondsToMicroseconds(sourceStaleSeconds));
            _presenceResolver.Reset(0);
            _presence = _presenceResolver.Snapshot;
            _nextRateLogTime = Time.unscaledTime + 5f;

            _faceCoroutine = StartCoroutine(RunTask(
                _faceFramePool,
                faceTargetFps,
                () => mediaPipeFaceEnabled,
                SubmitFace));

            _holisticCoroutine = StartCoroutine(RunTask(
                _holisticFramePool,
                holisticTargetFps,
                () => true,
                SubmitHolistic));

            Debug.Log(
                $"VCR P0 MediaPipe started: device='{selectedDevice}', " +
                $"actual={_webcam.width}x{_webcam.height}, " +
                $"cameraRequestedFps={requestedFps}, faceTargetFps={faceTargetFps}, " +
                $"holisticTargetFps={holisticTargetFps}");
        }

        /// <summary>
        /// ARKit routing can disable webcam face inference without stopping the
        /// shared webcam or body/hand task. Re-enabling lazily starts FaceLandmarker
        /// if it was disabled at startup.
        /// </summary>
        public void SetFaceTrackingEnabled(bool enabled)
        {
            SetMediaPipeFaceEnabled(enabled);
        }

        public void SetMediaPipeFaceEnabled(bool enabled)
        {
            mediaPipeFaceEnabled = enabled;

            if (_faceSource == null)
            {
                return;
            }

            if (!enabled)
            {
                if (_faceSource.Health.State != TrackingSourceHealthState.Stopped)
                {
                    _faceSource.Stop();
                }

                _latestFaceFrame = null;
                return;
            }

            if (_faceSource.Health.State == TrackingSourceHealthState.Stopped)
            {
                try
                {
                    _faceSource.Start();
                }
                catch (Exception exception)
                {
                    Debug.LogError("VCR P0: failed to enable MediaPipe face tracking.");
                    Debug.LogException(exception);
                    mediaPipeFaceEnabled = false;
                }
            }
        }

        private IEnumerator RunTask(
            TextureFramePool framePool,
            int targetFps,
            Func<bool> shouldSubmit,
            Action<Image, long> submit)
        {
            AsyncGPUReadbackRequest readback = default;
            var waitForReadback = new WaitUntil(() => readback.done);
            var intervalMs = Math.Max(1L, 1000L / Math.Max(1, targetFps));
            var nextDueMs = 0L;

            while (enabled)
            {
                if (!shouldSubmit())
                {
                    yield return null;
                    continue;
                }

                var nowMs = _clock.ElapsedMilliseconds;
                if (nowMs < nextDueMs ||
                    _webcam == null ||
                    !_webcam.isPlaying ||
                    !_webcam.didUpdateThisFrame)
                {
                    yield return null;
                    continue;
                }

                if (!framePool.TryGetTextureFrame(out var textureFrame))
                {
                    // Keep latency bounded: skip instead of allocating/queuing.
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

                // The source may have been disabled while readback was pending.
                if (!shouldSubmit())
                {
                    textureFrame.Release();
                    yield return null;
                    continue;
                }

                var image = textureFrame.BuildCPUImage();
                textureFrame.Release();

                // Packet.CreateImageAt inside DetectAsync takes Image ownership.
                var timestampMs = _clock.ElapsedMilliseconds;
                submit(image, timestampMs);
                nextDueMs = timestampMs + intervalMs;
            }
        }

        private void SubmitFace(Image image, long timestampMs)
        {
            _faceSource.SubmitImage(image, timestampMs);
        }

        private void SubmitHolistic(Image image, long timestampMs)
        {
            _holisticSource.SubmitImage(image, timestampMs);
        }

        private void Update()
        {
            CaptureLatestSourceFrames();
            UpdatePresence();

            if (!logTrackingRate || Time.unscaledTime < _nextRateLogTime)
            {
                return;
            }

            var faceResults = _faceSource?.ResultCount ?? 0L;
            var holisticResults = _holisticSource?.ResultCount ?? 0L;

            var faceDelta = faceResults - _lastFaceResultCount;
            var holisticDelta = holisticResults - _lastHolisticResultCount;

            _lastFaceResultCount = faceResults;
            _lastHolisticResultCount = holisticResults;
            _nextRateLogTime = Time.unscaledTime + 5f;

            Debug.Log(
                "VCR P0 tracking/5s: " +
                $"face={faceDelta}, holistic={holisticDelta}, " +
                $"faceState={_faceSource?.Health.State}, " +
                $"holisticState={_holisticSource?.Health.State}");
        }

        private void CaptureLatestSourceFrames()
        {
            if (_faceSource != null && _faceSource.TryTakeLatest(out var faceFrame))
            {
                _latestFaceFrame = faceFrame;
            }

            if (_holisticSource != null && _holisticSource.TryTakeLatest(out var bodyFrame))
            {
                _latestBodyHandsFrame = bodyFrame;
            }
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var nowUs = _clock.ElapsedMilliseconds * 1000L;
            _presence = _presenceResolver.Update(
                nowUs,
                _latestFaceFrame,
                mediaPipeFaceEnabled,
                _latestBodyHandsFrame,
                bodyHandsConfigured: true);

            if (_presence.Events != TrackingPresenceEvents.None)
            {
                Debug.Log(
                    $"VCR tracking presence: state={_presence.SubjectState}, " +
                    $"events={_presence.Events}, sourceAvailable={_presence.AnySourceAvailable}");
            }
        }

        private static long SecondsToMicroseconds(float seconds)
        {
            return (long)(Math.Max(0f, seconds) * 1_000_000.0);
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.latency",
                (_faceSource?.LastProcessingLatencyUs ?? 0L) / 1000.0,
                "ms"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.latency",
                (_holisticSource?.LastProcessingLatencyUs ?? 0L) / 1000.0,
                "ms"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.results",
                _faceSource?.ResultCount ?? 0L,
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.results",
                _holisticSource?.ResultCount ?? 0L,
                "count"));
        }

        private static byte[] LoadModel(string relativePath)
        {
            var path = Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("MediaPipe model not found.", path);
            }

            return File.ReadAllBytes(path);
        }

        private string SelectDeviceName(WebCamDevice[] devices)
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                foreach (var device in devices)
                {
                    if (device.name == deviceName)
                    {
                        return device.name;
                    }
                }

                Debug.LogWarning(
                    $"VCR P0: requested webcam '{deviceName}' not found; using the first device.");
            }

            return devices[0].name;
        }

        private void OnDestroy()
        {
            if (_faceCoroutine != null)
            {
                StopCoroutine(_faceCoroutine);
                _faceCoroutine = null;
            }

            if (_holisticCoroutine != null)
            {
                StopCoroutine(_holisticCoroutine);
                _holisticCoroutine = null;
            }

            _faceSource?.Dispose();
            _faceSource = null;

            _holisticSource?.Dispose();
            _holisticSource = null;

            _faceFramePool?.Dispose();
            _faceFramePool = null;

            _holisticFramePool?.Dispose();
            _holisticFramePool = null;

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
