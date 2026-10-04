using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using Mediapipe;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Core;

namespace VCR.Runtime.Tracking.MediaPipe
{
    /// <summary>
    /// Production webcam capture runner for the face-priority dual-task stack.
    ///
    /// One WebCamTexture feeds independent LIVE_STREAM FaceLandmarker and
    /// HolisticLandmarker tasks. Backpressure is drop-only: the runner never
    /// queues unbounded camera work. Runtime resources are restartable across
    /// enable/disable and application suspend/resume.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MediaPipeWebcamTrackingRunner :
        MonoBehaviour,
        ITrackingFrameProvider,
        ITrackingPresenceProvider,
        ITrackingSourceHealthProvider,
        ITrackingRuntimeControl,
        IFaceTrackingActivationControl,
        IRuntimeMetricsSource
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

        [Header("Presence")]
        [SerializeField, Min(0f)] private float subjectLostGraceSeconds = 0.5f;
        [SerializeField, Min(0f)] private float subjectRestoreStabilitySeconds = 0.15f;
        [SerializeField, Min(0.1f)] private float sourceStaleSeconds = 1.0f;

        [Header("Preprocessing - default off until measured")]
        [SerializeField] private WebcamPreprocessingMode preprocessingMode =
            WebcamPreprocessingMode.Disabled;
        [SerializeField, Range(0.5f, 3f)] private float lowLightExposure = 1.35f;
        [SerializeField, Range(0.5f, 2f)] private float lowLightGamma = 1.15f;

        [Header("Capture budget")]
        [SerializeField, Range(1, 4)] private int textureFramePoolSize = 2;
        [SerializeField] private bool suspendOnApplicationPause = true;
        [SerializeField] private bool logTrackingRate = true;

        private readonly Stopwatch _clock = new();

        private WebCamTexture _webcam;
        private TextureFramePool _faceFramePool;
        private TextureFramePool _holisticFramePool;

        private MediaPipeFaceSource _faceSource;
        private MediaPipeHolisticSource _holisticSource;
        private WebcamFramePreprocessor _preprocessor;

        private Coroutine _startupCoroutine;
        private Coroutine _faceCoroutine;
        private Coroutine _holisticCoroutine;

        private long _lastFaceResultCount;
        private long _lastHolisticResultCount;
        private float _nextRateLogTime;

        private long _faceSubmitted;
        private long _holisticSubmitted;
        private long _facePoolDrops;
        private long _holisticPoolDrops;
        private long _readbackErrors;
        private long _faceReadbackCount;
        private long _holisticReadbackCount;
        private long _faceLastReadbackUs;
        private long _holisticLastReadbackUs;

        private TrackingFrame _latestFaceFrame;
        private TrackingFrame _latestBodyHandsFrame;
        private TrackingPresenceResolver _presenceResolver;
        private TrackingPresenceSnapshot _presence;

        private MediaPipeWebcamLifecycleState _state =
            MediaPipeWebcamLifecycleState.Stopped;
        private string _selectedDeviceName;
        private string _lastError;
        private bool _applicationSuspended;
        private bool _destroying;

        public string ControlId => "mediapipe-webcam";
        public string DisplayName => "MediaPipe Webcam";
        public bool ControlEnabled => enabled;
        public TrackingSourceHealthState ControlHealthState =>
            _state switch
            {
                MediaPipeWebcamLifecycleState.Starting =>
                    TrackingSourceHealthState.Starting,
                MediaPipeWebcamLifecycleState.Running =>
                    TrackingSourceHealthState.Healthy,
                MediaPipeWebcamLifecycleState.Suspended =>
                    TrackingSourceHealthState.Stopped,
                MediaPipeWebcamLifecycleState.Faulted =>
                    TrackingSourceHealthState.Faulted,
                _ =>
                    TrackingSourceHealthState.Stopped
            };
        public string ControlError => _lastError;

        public ITrackingSource FaceTrackingSource => _faceSource;
        public ITrackingSource BodyHandTrackingSource => _holisticSource;
        public bool MediaPipeFaceEnabled => mediaPipeFaceEnabled;
        public bool FaceTrackingEnabled => mediaPipeFaceEnabled;
        public TrackingPresenceSnapshot Presence => _presence;

        public MediaPipeWebcamStatus Status =>
            new(
                _state,
                _selectedDeviceName,
                _webcam != null ? _webcam.width : 0,
                _webcam != null ? _webcam.height : 0,
                requestedFps,
                mediaPipeFaceEnabled,
                preprocessingMode,
                Interlocked.Read(ref _faceSubmitted),
                Interlocked.Read(ref _holisticSubmitted),
                Interlocked.Read(ref _facePoolDrops),
                Interlocked.Read(ref _holisticPoolDrops),
                Interlocked.Read(ref _readbackErrors),
                _lastError);

        public bool TryGetSourceHealth(
            TrackingRegion region,
            out TrackingSourceHealthSnapshot snapshot)
        {
            if (_faceSource != null &&
                (region &
                 (TrackingRegion.Face |
                  TrackingRegion.Head)) != 0)
            {
                snapshot =
                    new TrackingSourceHealthSnapshot(
                        _faceSource.SourceId,
                        _faceSource.Kind,
                        _faceSource.Regions,
                        _faceSource.Health,
                        _latestFaceFrame?
                            .RuntimeTimestampUs ?? 0);
                return true;
            }

            if (_holisticSource != null &&
                (region &
                 (TrackingRegion.Hands |
                  TrackingRegion.UpperBody)) != 0)
            {
                snapshot =
                    new TrackingSourceHealthSnapshot(
                        _holisticSource.SourceId,
                        _holisticSource.Kind,
                        _holisticSource.Regions,
                        _holisticSource.Health,
                        _latestBodyHandsFrame?
                            .RuntimeTimestampUs ?? 0);
                return true;
            }

            snapshot = default;
            return false;
        }

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

        private void OnEnable()
        {
            if (!Application.isPlaying ||
                _destroying ||
                _applicationSuspended)
            {
                return;
            }

            BeginStart();
        }

        private void Update()
        {
            if (_state !=
                MediaPipeWebcamLifecycleState.Running)
            {
                return;
            }

            CaptureLatestSourceFrames();
            UpdatePresence();

            if (!logTrackingRate ||
                Time.unscaledTime <
                _nextRateLogTime)
            {
                return;
            }

            var faceResults =
                _faceSource?.ResultCount ?? 0L;
            var holisticResults =
                _holisticSource?.ResultCount ?? 0L;

            var faceDelta =
                faceResults -
                _lastFaceResultCount;
            var holisticDelta =
                holisticResults -
                _lastHolisticResultCount;

            _lastFaceResultCount =
                faceResults;
            _lastHolisticResultCount =
                holisticResults;
            _nextRateLogTime =
                Time.unscaledTime + 5f;

            Debug.Log(
                "VCR tracking/5s: " +
                $"face={faceDelta}, holistic={holisticDelta}, " +
                $"faceState={_faceSource?.Health.State}, " +
                $"holisticState={_holisticSource?.Health.State}, " +
                $"faceDrops={Interlocked.Read(ref _facePoolDrops)}, " +
                $"holisticDrops={Interlocked.Read(ref _holisticPoolDrops)}, " +
                $"readbackErrors={Interlocked.Read(ref _readbackErrors)}",
                this);
        }

        public bool TrySetControlEnabled(
            bool enabledValue,
            out string error)
        {
            error = null;

            if (_destroying)
            {
                error =
                    "MediaPipe tracking is shutting down.";
                return false;
            }

            enabled = enabledValue;

            if (enabledValue &&
                !enabled)
            {
                error =
                    _lastError ??
                    "MediaPipe tracking could not be enabled.";
                return false;
            }

            return true;
        }

        public bool TryRecover(
            out string error)
        {
            error = null;

            if (_destroying)
            {
                error =
                    "MediaPipe tracking is shutting down.";
                return false;
            }

            if (!Application.isPlaying)
            {
                error =
                    "Tracking recovery requires play mode.";
                return false;
            }

            if (!enabled)
            {
                enabled = true;
                return enabled;
            }

            Restart();

            if (_state ==
                MediaPipeWebcamLifecycleState.Faulted)
            {
                error =
                    _lastError ??
                    "MediaPipe tracking recovery failed.";
                return false;
            }

            return true;
        }

        public void SetFaceTrackingEnabled(
            bool enabled)
        {
            SetMediaPipeFaceEnabled(enabled);
        }

        public void SetMediaPipeFaceEnabled(
            bool enabled)
        {
            mediaPipeFaceEnabled = enabled;

            if (_faceSource == null)
            {
                return;
            }

            if (!enabled)
            {
                if (_faceSource.Health.State !=
                    TrackingSourceHealthState.Stopped)
                {
                    _faceSource.Stop();
                }

                _latestFaceFrame = null;
                return;
            }

            if (_state !=
                    MediaPipeWebcamLifecycleState.Running ||
                _faceSource.Health.State !=
                    TrackingSourceHealthState.Stopped)
            {
                return;
            }

            try
            {
                _faceSource.Start();
            }
            catch (Exception exception)
            {
                mediaPipeFaceEnabled = false;
                _lastError =
                    "Failed to enable MediaPipe face tracking: " +
                    exception.Message;
                Debug.LogError(
                    _lastError,
                    this);
            }
        }

        public void Restart()
        {
            if (!Application.isPlaying ||
                _destroying)
            {
                return;
            }

            _applicationSuspended = false;
            StopRuntime(
                MediaPipeWebcamLifecycleState.Stopped);
            BeginStart();
        }

        public void Suspend()
        {
            if (_state ==
                    MediaPipeWebcamLifecycleState.Suspended ||
                _destroying)
            {
                return;
            }

            _applicationSuspended = true;
            StopRuntime(
                MediaPipeWebcamLifecycleState.Suspended);
        }

        public void Resume()
        {
            if (_destroying)
            {
                return;
            }

            _applicationSuspended = false;

            if (!isActiveAndEnabled ||
                !Application.isPlaying)
            {
                return;
            }

            BeginStart();
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.capture.state",
                (int)_state,
                "enum"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.capture.width",
                _webcam?.width ?? 0,
                "px"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.capture.height",
                _webcam?.height ?? 0,
                "px"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.preprocessing.mode",
                (int)preprocessingMode,
                "enum"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.latency",
                (_faceSource?.LastProcessingLatencyUs ?? 0L) /
                1000.0,
                "ms"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.latency",
                (_holisticSource?.LastProcessingLatencyUs ?? 0L) /
                1000.0,
                "ms"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.results",
                _faceSource?.ResultCount ?? 0L,
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.results",
                _holisticSource?.ResultCount ?? 0L,
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.submitted",
                Interlocked.Read(ref _faceSubmitted),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.submitted",
                Interlocked.Read(ref _holisticSubmitted),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.pool_drops",
                Interlocked.Read(ref _facePoolDrops),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.pool_drops",
                Interlocked.Read(ref _holisticPoolDrops),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.readback_errors",
                Interlocked.Read(ref _readbackErrors),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.readbacks",
                Interlocked.Read(ref _faceReadbackCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.face.readback_wait",
                Interlocked.Read(ref _faceLastReadbackUs) /
                1000.0,
                "ms"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.readbacks",
                Interlocked.Read(ref _holisticReadbackCount),
                "count"));

            output.Add(new RuntimeMetric(
                "tracking.mediapipe.holistic.readback_wait",
                Interlocked.Read(ref _holisticLastReadbackUs) /
                1000.0,
                "ms"));
        }

        private void BeginStart()
        {
            if (_state ==
                    MediaPipeWebcamLifecycleState.Running ||
                _state ==
                    MediaPipeWebcamLifecycleState.Starting ||
                _applicationSuspended ||
                _destroying)
            {
                return;
            }

            CleanupRuntimeResources(
                stopStartupCoroutine: false);

            _lastError = null;
            _state =
                MediaPipeWebcamLifecycleState.Starting;

            _startupCoroutine =
                StartCoroutine(
                    StartRuntimeRoutine());
        }

        private IEnumerator StartRuntimeRoutine()
        {
            yield return
                Application.RequestUserAuthorization(
                    UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(
                    UserAuthorization.WebCam))
            {
                FailStartup(
                    "Webcam permission was not granted.");
                yield break;
            }

            var devices =
                WebCamTexture.devices;

            if (devices == null ||
                devices.Length == 0)
            {
                FailStartup(
                    "No webcam devices were found.");
                yield break;
            }

            _selectedDeviceName =
                SelectDeviceName(devices);

            _webcam =
                new WebCamTexture(
                    _selectedDeviceName,
                    requestedWidth,
                    requestedHeight,
                    requestedFps);
            _webcam.Play();

            var startupDeadline =
                Time.realtimeSinceStartup + 5f;

            while (_webcam != null &&
                   _webcam.isPlaying &&
                   (_webcam.width <= 16 ||
                    _webcam.height <= 16) &&
                   Time.realtimeSinceStartup <
                   startupDeadline)
            {
                yield return null;
            }

            if (_webcam == null ||
                !_webcam.isPlaying ||
                _webcam.width <= 16 ||
                _webcam.height <= 16)
            {
                FailStartup(
                    "Webcam did not produce a valid frame within the startup timeout.");
                yield break;
            }

            byte[] faceModel;
            byte[] holisticModel;

            try
            {
                faceModel =
                    LoadModel(
                        FaceModelRelativePath);
                holisticModel =
                    LoadModel(
                        HolisticModelRelativePath);
            }
            catch (Exception exception)
            {
                FailStartup(
                    "MediaPipe models are missing or unreadable. " +
                    "Run tools/bootstrap-mediapipe first. " +
                    exception.Message);
                yield break;
            }

            try
            {
                _faceSource =
                    new MediaPipeFaceSource(
                        faceModel);
                _holisticSource =
                    new MediaPipeHolisticSource(
                        holisticModel);

                if (mediaPipeFaceEnabled)
                {
                    _faceSource.Start();
                }

                _holisticSource.Start();

                _faceFramePool =
                    new TextureFramePool(
                        _webcam.width,
                        _webcam.height,
                        TextureFormat.RGBA32,
                        textureFramePoolSize);

                _holisticFramePool =
                    new TextureFramePool(
                        _webcam.width,
                        _webcam.height,
                        TextureFormat.RGBA32,
                        textureFramePoolSize);
            }
            catch (Exception exception)
            {
                FailStartup(
                    "Failed to initialize MediaPipe tracking: " +
                    exception.Message);
                yield break;
            }

            _clock.Restart();

            _presenceResolver =
                new TrackingPresenceResolver(
                    SecondsToMicroseconds(
                        subjectLostGraceSeconds),
                    SecondsToMicroseconds(
                        subjectRestoreStabilitySeconds),
                    SecondsToMicroseconds(
                        sourceStaleSeconds));
            _presenceResolver.Reset(0);
            _presence =
                _presenceResolver.Snapshot;

            _lastFaceResultCount =
                _faceSource?.ResultCount ?? 0L;
            _lastHolisticResultCount =
                _holisticSource?.ResultCount ?? 0L;
            _nextRateLogTime =
                Time.unscaledTime + 5f;

            _state =
                MediaPipeWebcamLifecycleState.Running;
            _startupCoroutine = null;

            _faceCoroutine =
                StartCoroutine(
                    RunTask(
                        _faceFramePool,
                        faceTargetFps,
                        () =>
                            mediaPipeFaceEnabled,
                        SubmitFace,
                        faceTask: true));

            _holisticCoroutine =
                StartCoroutine(
                    RunTask(
                        _holisticFramePool,
                        holisticTargetFps,
                        () => true,
                        SubmitHolistic,
                        faceTask: false));

            Debug.Log(
                $"VCR MediaPipe started: device='{_selectedDeviceName}', " +
                $"actual={_webcam.width}x{_webcam.height}, " +
                $"cameraRequestedFps={requestedFps}, " +
                $"faceTargetFps={faceTargetFps}, " +
                $"holisticTargetFps={holisticTargetFps}.",
                this);
        }

        private IEnumerator RunTask(
            TextureFramePool framePool,
            int targetFps,
            Func<bool> shouldSubmit,
            Action<Image, long> submit,
            bool faceTask)
        {
            AsyncGPUReadbackRequest readback =
                default;

            var waitForReadback =
                new WaitUntil(
                    () => readback.done);

            var intervalMs =
                Math.Max(
                    1L,
                    1000L /
                    Math.Max(
                        1,
                        targetFps));

            var nextDueMs = 0L;

            while (enabled &&
                   _state ==
                   MediaPipeWebcamLifecycleState.Running)
            {
                if (!shouldSubmit())
                {
                    yield return null;
                    continue;
                }

                var nowMs =
                    _clock.ElapsedMilliseconds;

                if (nowMs < nextDueMs ||
                    _webcam == null ||
                    !_webcam.isPlaying ||
                    !_webcam.didUpdateThisFrame)
                {
                    yield return null;
                    continue;
                }

                if (!framePool.TryGetTextureFrame(
                        out var textureFrame))
                {
                    if (faceTask)
                    {
                        Interlocked.Increment(
                            ref _facePoolDrops);
                    }
                    else
                    {
                        Interlocked.Increment(
                            ref _holisticPoolDrops);
                    }

                    yield return null;
                    continue;
                }

                _preprocessor ??=
                    new WebcamFramePreprocessor();

                var inferenceTexture =
                    _preprocessor.Prepare(
                        _webcam,
                        preprocessingMode,
                        lowLightExposure,
                        lowLightGamma);

                var readbackStartedUs =
                    MonotonicClock
                        .NowMicroseconds();

                readback =
                    textureFrame.ReadTextureAsync(
                        inferenceTexture,
                        flipHorizontally,
                        flipVertically);

                yield return waitForReadback;

                var readbackElapsedUs =
                    Math.Max(
                        0L,
                        MonotonicClock
                            .NowMicroseconds() -
                        readbackStartedUs);

                if (faceTask)
                {
                    Interlocked.Increment(
                        ref _faceReadbackCount);
                    Interlocked.Exchange(
                        ref _faceLastReadbackUs,
                        readbackElapsedUs);
                }
                else
                {
                    Interlocked.Increment(
                        ref _holisticReadbackCount);
                    Interlocked.Exchange(
                        ref _holisticLastReadbackUs,
                        readbackElapsedUs);
                }

                if (readback.hasError)
                {
                    Interlocked.Increment(
                        ref _readbackErrors);
                    textureFrame.Release();
                    yield return null;
                    continue;
                }

                if (_state !=
                        MediaPipeWebcamLifecycleState.Running ||
                    !shouldSubmit())
                {
                    textureFrame.Release();
                    yield return null;
                    continue;
                }

                var image =
                    textureFrame.BuildCPUImage();

                textureFrame.Release();

                var timestampMs =
                    _clock.ElapsedMilliseconds;

                try
                {
                    submit(
                        image,
                        timestampMs);

                    if (faceTask)
                    {
                        Interlocked.Increment(
                            ref _faceSubmitted);
                    }
                    else
                    {
                        Interlocked.Increment(
                            ref _holisticSubmitted);
                    }
                }
                catch (Exception exception)
                {
                    image?.Dispose();
                    _lastError =
                        "MediaPipe frame submission failed: " +
                        exception.Message;
                    Debug.LogWarning(
                        _lastError,
                        this);
                }

                nextDueMs =
                    timestampMs +
                    intervalMs;
            }
        }

        private void SubmitFace(
            Image image,
            long timestampMs)
        {
            _faceSource.SubmitImage(
                image,
                timestampMs);
        }

        private void SubmitHolistic(
            Image image,
            long timestampMs)
        {
            _holisticSource.SubmitImage(
                image,
                timestampMs);
        }

        private void CaptureLatestSourceFrames()
        {
            if (_faceSource != null &&
                _faceSource.TryTakeLatest(
                    out var faceFrame))
            {
                _latestFaceFrame =
                    faceFrame;
            }

            if (_holisticSource != null &&
                _holisticSource.TryTakeLatest(
                    out var bodyFrame))
            {
                _latestBodyHandsFrame =
                    bodyFrame;
            }
        }

        private void UpdatePresence()
        {
            if (_presenceResolver == null)
            {
                return;
            }

            var nowUs =
                _clock.ElapsedMilliseconds *
                1000L;

            _presence =
                _presenceResolver.Update(
                    nowUs,
                    _latestFaceFrame,
                    mediaPipeFaceEnabled,
                    _latestBodyHandsFrame,
                    bodyHandsConfigured: true);

            if (_presence.Events !=
                TrackingPresenceEvents.None)
            {
                Debug.Log(
                    $"VCR tracking presence: state={_presence.SubjectState}, " +
                    $"events={_presence.Events}, " +
                    $"sourceAvailable={_presence.AnySourceAvailable}",
                    this);
            }
        }

        private void FailStartup(
            string error)
        {
            _lastError =
                string.IsNullOrWhiteSpace(error)
                    ? "MediaPipe webcam startup failed."
                    : error;

            Debug.LogError(
                "VCR MediaPipe: " +
                _lastError,
                this);

            CleanupRuntimeResources(
                stopStartupCoroutine: false);

            _startupCoroutine = null;
            _state =
                MediaPipeWebcamLifecycleState.Faulted;
        }

        private void StopRuntime(
            MediaPipeWebcamLifecycleState finalState)
        {
            CleanupRuntimeResources(
                stopStartupCoroutine: true);

            _state = finalState;

            if (finalState ==
                MediaPipeWebcamLifecycleState.Stopped)
            {
                _lastError = null;
            }
        }

        private void CleanupRuntimeResources(
            bool stopStartupCoroutine)
        {
            if (stopStartupCoroutine &&
                _startupCoroutine != null)
            {
                StopCoroutine(
                    _startupCoroutine);
            }

            _startupCoroutine = null;

            if (_faceCoroutine != null)
            {
                StopCoroutine(
                    _faceCoroutine);
                _faceCoroutine = null;
            }

            if (_holisticCoroutine != null)
            {
                StopCoroutine(
                    _holisticCoroutine);
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

                Destroy(
                    _webcam);
                _webcam = null;
            }

            _preprocessor?.Dispose();
            _preprocessor = null;

            _clock.Reset();
            _latestFaceFrame = null;
            _latestBodyHandsFrame = null;
            _presenceResolver = null;
            _presence = default;
        }

        private static byte[] LoadModel(
            string relativePath)
        {
            var path =
                Path.Combine(
                    Application.streamingAssetsPath,
                    relativePath);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "MediaPipe model not found.",
                    path);
            }

            return File.ReadAllBytes(path);
        }

        private string SelectDeviceName(
            WebCamDevice[] devices)
        {
            if (!string.IsNullOrWhiteSpace(
                    deviceName))
            {
                foreach (var device in
                         devices)
                {
                    if (device.name ==
                        deviceName)
                    {
                        return device.name;
                    }
                }

                Debug.LogWarning(
                    $"VCR: requested webcam '{deviceName}' was not found; using the first device.",
                    this);
            }

            return devices[0].name;
        }

        private static long SecondsToMicroseconds(
            float seconds)
        {
            return (long)(
                Math.Max(
                    0f,
                    seconds) *
                1_000_000.0);
        }

        private void OnApplicationPause(
            bool paused)
        {
            if (!suspendOnApplicationPause)
            {
                return;
            }

            if (paused)
            {
                Suspend();
            }
            else
            {
                Resume();
            }
        }

        private void OnDisable()
        {
            if (_destroying)
            {
                return;
            }

            StopRuntime(
                _applicationSuspended
                    ? MediaPipeWebcamLifecycleState.Suspended
                    : MediaPipeWebcamLifecycleState.Stopped);
        }

        private void OnDestroy()
        {
            _destroying = true;
            CleanupRuntimeResources(
                stopStartupCoroutine: true);
            _state =
                MediaPipeWebcamLifecycleState.Stopped;
        }
    }
}
