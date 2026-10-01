using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Diagnostics
{
    /// <summary>
    /// Low-overhead P0 runtime diagnostics.
    ///
    /// Per-frame work is limited to a frame-time sample and sequence checks.
    /// Sorting/scene metric collection occurs only at report cadence.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30000)]
    public sealed class P0RuntimeDiagnostics : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool autoFindTrackingProvider = true;

        [Header("Reporting")]
        [SerializeField, Min(1f)] private float reportIntervalSeconds = 5f;
        [SerializeField, Range(120, 3600)] private int frameWindowFrames = 600;
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private bool writeCsvEvidence = false;
        [SerializeField] private string csvFileName = "vcr-p0-diagnostics.csv";

        private ITrackingFrameProvider _provider;
        private ITrackingPresenceProvider _presenceProvider;

        private float[] _frameMs;
        private float[] _sortScratch;
        private int _frameIndex;
        private int _frameCount;

        private double _lastReportTime;
        private float _nextProviderSearchTime;

        private long _lastFaceSequence = -1;
        private long _lastBodySequence = -1;
        private long _lastPoseSequence = -1;
        private long _lastExpressionSequence = -1;

        private int _faceUpdates;
        private int _bodyUpdates;
        private int _poseUpdates;
        private int _expressionUpdates;

        private double _faceAgeMs = double.NaN;
        private double _bodyAgeMs = double.NaN;
        private double _poseAgeMs = double.NaN;
        private double _expressionAgeMs = double.NaN;

        private readonly List<RuntimeMetric> _metrics = new(64);
        private bool _csvHeaderWritten;

        private void Awake()
        {
            _frameMs = new float[frameWindowFrames];
            _sortScratch = new float[frameWindowFrames];
            ResolveProvider();
        }

        private void Start()
        {
            _lastReportTime =
                Time.realtimeSinceStartupAsDouble;

            if (logToConsole)
            {
                Debug.Log(
                    "VCR P0 diagnostics platform: " +
                    $"OS='{SystemInfo.operatingSystem}', " +
                    $"CPU='{SystemInfo.processorType}', " +
                    $"GPU='{SystemInfo.graphicsDeviceName}', " +
                    $"RAM={SystemInfo.systemMemorySize}MB, " +
                    $"Unity={Application.unityVersion}",
                    this);
            }
        }

        private void Update()
        {
            SampleFrameTime();

            if (_provider == null &&
                Time.unscaledTime >= _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime + 1f;
                ResolveProvider();
            }

            if (_provider != null)
            {
                ObserveTracking();
            }

            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now - _lastReportTime >=
                reportIntervalSeconds)
            {
                Report(now);
            }
        }

        public void SetTrackingProvider(
            ITrackingFrameProvider provider)
        {
            _provider = provider;
            _presenceProvider =
                provider as ITrackingPresenceProvider;
            trackingProviderBehaviour =
                provider as MonoBehaviour;

            _lastFaceSequence = -1;
            _lastBodySequence = -1;
            _lastPoseSequence = -1;
            _lastExpressionSequence = -1;
        }

        private void SampleFrameTime()
        {
            if (_frameMs == null ||
                _frameMs.Length == 0)
            {
                return;
            }

            _frameMs[_frameIndex] =
                Time.unscaledDeltaTime * 1000f;

            _frameIndex =
                (_frameIndex + 1) %
                _frameMs.Length;

            if (_frameCount < _frameMs.Length)
            {
                _frameCount++;
            }
        }

        private void ObserveTracking()
        {
            var nowUs = MonotonicClock.NowMicroseconds();

            if (_provider.TryGetLatestFace(out var face) &&
                face != null)
            {
                Observe(
                    face,
                    ref _lastFaceSequence,
                    ref _faceUpdates,
                    ref _faceAgeMs,
                    nowUs);
            }

            if (_provider.TryGetLatestBodyHands(out var body) &&
                body != null)
            {
                Observe(
                    body,
                    ref _lastBodySequence,
                    ref _bodyUpdates,
                    ref _bodyAgeMs,
                    nowUs);
            }

            if (_provider.TryGetLatestHumanoidPose(out var pose) &&
                pose != null)
            {
                Observe(
                    pose,
                    ref _lastPoseSequence,
                    ref _poseUpdates,
                    ref _poseAgeMs,
                    nowUs);
            }

            if (_provider.TryGetLatestExpressions(
                    out var expressions) &&
                expressions != null)
            {
                Observe(
                    expressions,
                    ref _lastExpressionSequence,
                    ref _expressionUpdates,
                    ref _expressionAgeMs,
                    nowUs);
            }
        }

        private static void Observe(
            TrackingFrame frame,
            ref long lastSequence,
            ref int updates,
            ref double ageMs,
            long nowUs)
        {
            if (frame.Sequence != lastSequence)
            {
                lastSequence = frame.Sequence;
                updates++;
            }

            ageMs =
                frame.RuntimeTimestampUs > 0
                    ? Math.Max(
                        0.0,
                        (nowUs -
                         frame.RuntimeTimestampUs) /
                        1000.0)
                    : double.NaN;
        }

        private void Report(double now)
        {
            var elapsed =
                Math.Max(
                    0.001,
                    now - _lastReportTime);

            _lastReportTime = now;

            ComputeFrameStats(
                out var averageMs,
                out var p95Ms,
                out var p99Ms);

            var faceHz =
                _faceUpdates / elapsed;
            var bodyHz =
                _bodyUpdates / elapsed;
            var poseHz =
                _poseUpdates / elapsed;
            var expressionHz =
                _expressionUpdates / elapsed;

            _metrics.Clear();
            CollectSubsystemMetrics(_metrics);

            var presence =
                _presenceProvider?.Presence;

            var builder = new StringBuilder(512);
            builder.Append(
                "VCR P0 diagnostics: ");
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "frame avg={0:F2}ms p95={1:F2}ms p99={2:F2}ms",
                averageMs,
                p95Ms,
                p99Ms);

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                " | Hz face={0:F1} body={1:F1} full={2:F1} expr={3:F1}",
                faceHz,
                bodyHz,
                poseHz,
                expressionHz);

            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                " | age-ms face={0} body={1} full={2} expr={3}",
                FormatAge(_faceAgeMs),
                FormatAge(_bodyAgeMs),
                FormatAge(_poseAgeMs),
                FormatAge(_expressionAgeMs));

            if (presence.HasValue)
            {
                builder.Append(
                    " | presence=");
                builder.Append(
                    presence.Value.SubjectState);
                builder.Append(
                    " sources=");
                builder.Append(
                    presence.Value.AnySourceAvailable
                        ? "up"
                        : "down");
            }

            foreach (var metric in _metrics)
            {
                builder.Append(" | ");
                builder.Append(metric.Name);
                builder.Append('=');
                builder.Append(
                    metric.Value.ToString(
                        "F2",
                        CultureInfo.InvariantCulture));
                builder.Append(metric.Unit);
            }

            if (logToConsole)
            {
                if (p95Ms > 16.67f ||
                    p99Ms > 25f)
                {
                    Debug.LogWarning(
                        builder.ToString(),
                        this);
                }
                else
                {
                    Debug.Log(
                        builder.ToString(),
                        this);
                }
            }

            if (writeCsvEvidence)
            {
                AppendCsv(
                    averageMs,
                    p95Ms,
                    p99Ms,
                    faceHz,
                    bodyHz,
                    poseHz,
                    expressionHz,
                    presence);
            }

            _faceUpdates = 0;
            _bodyUpdates = 0;
            _poseUpdates = 0;
            _expressionUpdates = 0;
        }

        private void ComputeFrameStats(
            out float averageMs,
            out float p95Ms,
            out float p99Ms)
        {
            averageMs = 0f;
            p95Ms = 0f;
            p99Ms = 0f;

            if (_frameCount <= 0)
            {
                return;
            }

            var count =
                Math.Min(
                    _frameCount,
                    _frameMs.Length);

            for (var i = 0; i < count; i++)
            {
                _sortScratch[i] =
                    _frameMs[i];
                averageMs +=
                    _sortScratch[i];
            }

            averageMs /= count;

            Array.Sort(
                _sortScratch,
                0,
                count);

            p95Ms =
                _sortScratch[
                    PercentileIndex(count, 0.95)];
            p99Ms =
                _sortScratch[
                    PercentileIndex(count, 0.99)];
        }

        private void CollectSubsystemMetrics(
            List<RuntimeMetric> output)
        {
            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                    IRuntimeMetricsSource source)
                {
                    source.CollectMetrics(output);
                }
            }
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour is
                ITrackingFrameProvider configured)
            {
                SetTrackingProvider(configured);
                return;
            }

            if (!autoFindTrackingProvider)
            {
                return;
            }

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            ITrackingFrameProvider direct = null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour is ITrackingRouteProvider route)
                {
                    SetTrackingProvider(route);
                    trackingProviderBehaviour = behaviour;
                    return;
                }

                if (direct == null &&
                    behaviour is ITrackingFrameProvider provider)
                {
                    direct = provider;
                    trackingProviderBehaviour = behaviour;
                }
            }

            if (direct != null)
            {
                SetTrackingProvider(direct);
            }
        }

        private void AppendCsv(
            float averageMs,
            float p95Ms,
            float p99Ms,
            double faceHz,
            double bodyHz,
            double poseHz,
            double expressionHz,
            TrackingPresenceSnapshot? presence)
        {
            try
            {
                var path = Path.Combine(
                    Application.persistentDataPath,
                    string.IsNullOrWhiteSpace(csvFileName)
                        ? "vcr-p0-diagnostics.csv"
                        : csvFileName);

                if (!_csvHeaderWritten &&
                    !File.Exists(path))
                {
                    File.AppendAllText(
                        path,
                        "utc,frame_avg_ms,frame_p95_ms,frame_p99_ms,face_hz,body_hz,fullbody_hz,expression_hz,face_age_ms,body_age_ms,fullbody_age_ms,expression_age_ms,presence,source_available,metrics\n");
                }

                _csvHeaderWritten = true;

                var metricsText = new StringBuilder();
                foreach (var metric in _metrics)
                {
                    if (metricsText.Length > 0)
                    {
                        metricsText.Append(';');
                    }

                    metricsText.Append(metric.Name);
                    metricsText.Append('=');
                    metricsText.Append(
                        metric.Value.ToString(
                            "F3",
                            CultureInfo.InvariantCulture));
                    metricsText.Append(metric.Unit);
                }

                var line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8},{9},{10},{11},{12},{13},\"{14}\"\n",
                    DateTime.UtcNow.ToString("O"),
                    averageMs,
                    p95Ms,
                    p99Ms,
                    faceHz,
                    bodyHz,
                    poseHz,
                    expressionHz,
                    CsvAge(_faceAgeMs),
                    CsvAge(_bodyAgeMs),
                    CsvAge(_poseAgeMs),
                    CsvAge(_expressionAgeMs),
                    presence?.SubjectState.ToString() ?? "n/a",
                    presence.HasValue &&
                    presence.Value.AnySourceAvailable
                        ? "1"
                        : "0",
                    metricsText.ToString().Replace("\"", "'"));

                File.AppendAllText(path, line);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"VCR P0 diagnostics CSV: {exception.Message}",
                    this);
                writeCsvEvidence = false;
            }
        }

        private static int PercentileIndex(
            int count,
            double percentile)
        {
            return Math.Min(
                count - 1,
                Math.Max(
                    0,
                    (int)Math.Ceiling(
                        percentile * count) - 1));
        }

        private static string FormatAge(double value)
        {
            return double.IsNaN(value)
                ? "n/a"
                : value.ToString(
                    "F1",
                    CultureInfo.InvariantCulture);
        }

        private static string CsvAge(double value)
        {
            return double.IsNaN(value)
                ? string.Empty
                : value.ToString(
                    "F3",
                    CultureInfo.InvariantCulture);
        }
    }
}
