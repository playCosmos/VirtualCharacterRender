using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using VCR.Runtime.Core;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Diagnostics
{
    /// <summary>
    /// Low-overhead runtime diagnostics.
    ///
    /// Per-frame work is limited to a frame-time sample and sequence checks.
    /// Sorting/scene metric collection occurs only at report cadence.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(30000)]
    public class RuntimeDiagnostics : MonoBehaviour
    {
        [Header("Tracking")]
        [SerializeField] private MonoBehaviour trackingProviderBehaviour;
        [SerializeField] private bool autoFindTrackingProvider = true;

        [Header("Reporting")]
        [SerializeField, Min(1f)] private float reportIntervalSeconds = 5f;
        [SerializeField, Min(5f)] private float metricSourceRefreshIntervalSeconds = 30f;
        [SerializeField, Range(120, 3600)] private int frameWindowFrames = 600;
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private bool writeCsvEvidence = false;
        [Tooltip("Standalone Development builds automatically write evidence; non-development performance builds do not.")]
        [SerializeField] private bool writeCsvInDevelopmentBuild = true;
        [SerializeField] private string csvFileName = "vcr-runtime-diagnostics.csv";
        [SerializeField] private string systemEvidenceFileName = "vcr-runtime-system.txt";

        private ITrackingFrameProvider _provider;
        private ITrackingPresenceProvider _presenceProvider;

        private float[] _frameMs;
        private float[] _sortScratch;
        private int _frameIndex;
        private int _frameCount;

        private double _lastReportTime;
        private float _nextProviderSearchTime;

        private TrackingFrame _lastFaceFrame;
        private TrackingFrame _lastBodyFrame;
        private TrackingFrame _lastPoseFrame;
        private TrackingFrame _lastExpressionFrame;

        private int _faceUpdates;
        private int _bodyUpdates;
        private int _poseUpdates;
        private int _expressionUpdates;

        private double _faceAgeMs = double.NaN;
        private double _bodyAgeMs = double.NaN;
        private double _poseAgeMs = double.NaN;
        private double _expressionAgeMs = double.NaN;

        private static readonly Comparison<RuntimeMetric>
            RuntimeMetricComparison =
                CompareRuntimeMetrics;

        private readonly List<RuntimeMetric> _metrics = new(64);
        private readonly List<IRuntimeMetricsSource> _metricSources =
            new(32);
        private readonly StringBuilder _reportBuilder =
            new(512);
        private float _nextMetricSourceRefreshTime;
        private bool _csvHeaderWritten;
        private long _snapshotSequence;
        private long _metricSourceFailureCount;
        private long _snapshotSubscriberFailureCount;
        private readonly object _snapshotSubscriptionSync =
            new();
        private Action<RuntimeDiagnosticsSnapshot>[] _snapshotSubscribers =
            Array.Empty<Action<RuntimeDiagnosticsSnapshot>>();
        private RuntimeDiagnosticsSnapshot _latestSnapshot;

        public RuntimeDiagnosticsSnapshot LatestSnapshot =>
            _latestSnapshot;

        public float ReportIntervalSeconds =>
            float.IsNaN(
                reportIntervalSeconds) ||
            float.IsInfinity(
                reportIntervalSeconds)
                ? 5f
                : Mathf.Max(
                    1f,
                    reportIntervalSeconds);

        public int FrameWindowFrames =>
            Mathf.Clamp(
                frameWindowFrames,
                120,
                3600);

        public bool ConsoleLoggingEnabled =>
            logToConsole;

        public bool CsvEvidenceEnabled =>
            writeCsvEvidence;

        public string EvidenceDirectory =>
            Application.persistentDataPath;

        public event Action<RuntimeDiagnosticsSnapshot> SnapshotUpdated
        {
            add
            {
                if (value == null)
                {
                    return;
                }

                lock (_snapshotSubscriptionSync)
                {
                    var current =
                        _snapshotSubscribers;
                    var next =
                        new Action<RuntimeDiagnosticsSnapshot>[
                            current.Length + 1];

                    Array.Copy(
                        current,
                        next,
                        current.Length);
                    next[current.Length] =
                        value;

                    Volatile.Write(
                        ref _snapshotSubscribers,
                        next);
                }
            }
            remove
            {
                if (value == null)
                {
                    return;
                }

                lock (_snapshotSubscriptionSync)
                {
                    var current =
                        _snapshotSubscribers;
                    var index = -1;

                    for (var i =
                             current.Length - 1;
                         i >= 0;
                         i--)
                    {
                        if (Equals(
                                current[i],
                                value))
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0)
                    {
                        return;
                    }

                    if (current.Length == 1)
                    {
                        Volatile.Write(
                            ref _snapshotSubscribers,
                            Array.Empty<
                                Action<RuntimeDiagnosticsSnapshot>>());
                        return;
                    }

                    var next =
                        new Action<RuntimeDiagnosticsSnapshot>[
                            current.Length - 1];

                    if (index > 0)
                    {
                        Array.Copy(
                            current,
                            0,
                            next,
                            0,
                            index);
                    }

                    if (index <
                        current.Length - 1)
                    {
                        Array.Copy(
                            current,
                            index + 1,
                            next,
                            index,
                            current.Length -
                            index -
                            1);
                    }

                    Volatile.Write(
                        ref _snapshotSubscribers,
                        next);
                }
            }
        }

        protected virtual void Awake()
        {
            var frameWindow =
                FrameWindowFrames;

            _frameMs =
                new float[frameWindow];
            _sortScratch =
                new float[frameWindow];
            ResolveProvider();
            RefreshMetricSources(
                force: true);
        }

        protected virtual void Start()
        {
            _lastReportTime =
                Time.realtimeSinceStartupAsDouble;

            if (!Application.isEditor &&
                Debug.isDebugBuild &&
                writeCsvInDevelopmentBuild)
            {
                writeCsvEvidence = true;
            }

            if (writeCsvEvidence)
            {
                WriteSystemEvidence();
            }

            if (logToConsole)
            {
                Debug.Log(
                    "VCR runtime diagnostics platform: " +
                    $"OS='{SystemInfo.operatingSystem}', " +
                    $"CPU='{SystemInfo.processorType}', " +
                    $"GPU='{SystemInfo.graphicsDeviceName}', " +
                    $"RAM={SystemInfo.systemMemorySize}MB, " +
                    $"Unity={Application.unityVersion}",
                    this);
            }
        }

        protected virtual void Update()
        {
            SampleFrameTime();

            if (!IsServiceAlive(_provider))
            {
                _provider = null;
                _presenceProvider = null;

            }

            if (_provider == null &&
                Time.unscaledTime >= _nextProviderSearchTime)
            {
                _nextProviderSearchTime =
                    Time.unscaledTime + 1f;
                ResolveProvider();
            }

            if (IsServiceAlive(_provider))
            {
                ObserveTracking();
            }

            var now =
                Time.realtimeSinceStartupAsDouble;

            if (now - _lastReportTime >=
                ReportIntervalSeconds)
            {
                Report(now);
            }
        }

        public void ConfigureReporting(
            float intervalSeconds,
            bool consoleLogging,
            bool csvEvidence)
        {
            reportIntervalSeconds =
                Mathf.Max(
                    1f,
                    intervalSeconds);
            logToConsole =
                consoleLogging;
            writeCsvEvidence =
                csvEvidence;

            if (writeCsvEvidence)
            {
                WriteSystemEvidence();
            }
        }

        public bool TryCaptureNow(
            out RuntimeDiagnosticsSnapshot snapshot,
            out string error)
        {
            var now =
                Time.realtimeSinceStartupAsDouble;
            var elapsed =
                now -
                _lastReportTime;

            snapshot =
                _latestSnapshot;
            error = null;

            if (elapsed < 1.0)
            {
                error =
                    "Diagnostics capture requires at least one second since the previous report so tracking-rate evidence is not distorted.";
                return false;
            }

            Report(
                now);
            snapshot =
                _latestSnapshot;
            return true;
        }

        public void SetConsoleLogging(
            bool enabled)
        {
            logToConsole =
                enabled;
        }

        public void SetCsvEvidence(
            bool enabled)
        {
            writeCsvEvidence =
                enabled;

            if (writeCsvEvidence)
            {
                WriteSystemEvidence();
            }
        }

        public bool TryWriteLatestSnapshotJson(
            out string path,
            out string error)
        {
            path = null;
            error = null;

            if (_latestSnapshot.Sequence <= 0)
            {
                error =
                    "No diagnostics snapshot is available yet.";
                return false;
            }

            try
            {
                var document =
                    RuntimeDiagnosticsEvidenceDocument
                        .FromSnapshot(
                            _latestSnapshot);
                var json =
                    JsonUtility.ToJson(
                        document,
                        prettyPrint:
                            true);
                var fileName =
                    "vcr-runtime-snapshot-" +
                    DateTime.UtcNow.ToString(
                        "yyyyMMdd-HHmmss-fff") +
                    "Z.json";

                path =
                    Path.Combine(
                        Application.persistentDataPath,
                        fileName);

                File.WriteAllText(
                    path,
                    json);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Diagnostics snapshot JSON write failed: " +
                    exception.Message;
                path = null;
                return false;
            }
        }

        public void SetTrackingProvider(
            ITrackingFrameProvider provider)
        {
            _provider =
                IsServiceAlive(provider)
                    ? provider
                    : null;
            _presenceProvider =
                IsServiceAlive(_provider)
                    ? _provider as
                        ITrackingPresenceProvider
                    : null;
            trackingProviderBehaviour =
                _provider as MonoBehaviour;

            _lastFaceFrame = null;
            _lastBodyFrame = null;
            _lastPoseFrame = null;
            _lastExpressionFrame = null;
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
                    ref _lastFaceFrame,
                    ref _faceUpdates,
                    ref _faceAgeMs,
                    nowUs);
            }

            if (_provider.TryGetLatestBodyHands(out var body) &&
                body != null)
            {
                Observe(
                    body,
                    ref _lastBodyFrame,
                    ref _bodyUpdates,
                    ref _bodyAgeMs,
                    nowUs);
            }

            if (_provider.TryGetLatestHumanoidPose(out var pose) &&
                pose != null)
            {
                Observe(
                    pose,
                    ref _lastPoseFrame,
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
                    ref _lastExpressionFrame,
                    ref _expressionUpdates,
                    ref _expressionAgeMs,
                    nowUs);
            }
        }

        private static void Observe(
            TrackingFrame frame,
            ref TrackingFrame lastFrame,
            ref int updates,
            ref double ageMs,
            long nowUs)
        {
            if (!ReferenceEquals(
                    frame,
                    lastFrame))
            {
                lastFrame = frame;
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
            _metrics.Sort(
                RuntimeMetricComparison);

            TrackingPresenceSnapshot? presence =
                IsServiceAlive(_presenceProvider)
                    ? _presenceProvider.Presence
                    : null;

            var metricSnapshot =
                _metrics.ToArray();

            _latestSnapshot =
                new RuntimeDiagnosticsSnapshot(
                    ++_snapshotSequence,
                    now,
                    averageMs,
                    p95Ms,
                    p99Ms,
                    faceHz,
                    bodyHz,
                    poseHz,
                    expressionHz,
                    _faceAgeMs,
                    _bodyAgeMs,
                    _poseAgeMs,
                    _expressionAgeMs,
                    presence,
                    metricSnapshot);

            NotifySnapshotUpdated(
                _latestSnapshot);

            if (logToConsole)
            {
                var builder =
                    _reportBuilder;
                builder.Clear();
                builder.Append(
                    "VCR runtime diagnostics: ");
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
            FrameTimingStatistics.Compute(
                _frameMs,
                _frameCount,
                _sortScratch,
                out averageMs,
                out p95Ms,
                out p99Ms);
        }

        private static int CompareRuntimeMetrics(
            RuntimeMetric left,
            RuntimeMetric right)
        {
            return string.Compare(
                left.Name,
                right.Name,
                StringComparison.Ordinal);
        }

        private void CollectSubsystemMetrics(
            List<RuntimeMetric> output)
        {
            RefreshMetricSources();

            var staleSourceDetected =
                false;

            for (var i = 0;
                 i < _metricSources.Count;
                 i++)
            {
                var source =
                    _metricSources[i];

                if (!IsServiceAlive(source))
                {
                    staleSourceDetected =
                        true;
                    continue;
                }

                try
                {
                    source.CollectMetrics(
                        output);
                }
                catch
                {
                    _metricSourceFailureCount++;
                }
            }

            if (staleSourceDetected)
            {
                _nextMetricSourceRefreshTime =
                    0f;
            }

            output.Add(
                new RuntimeMetric(
                    "diagnostics.metric_source_failures",
                    _metricSourceFailureCount,
                    "count"));
            output.Add(
                new RuntimeMetric(
                    "diagnostics.snapshot_subscriber_failures",
                    _snapshotSubscriberFailureCount,
                    "count"));
        }

        private void RefreshMetricSources(
            bool force = false)
        {
            var now =
                Time.unscaledTime;

            if (!force &&
                now <
                    _nextMetricSourceRefreshTime)
            {
                return;
            }

            _nextMetricSourceRefreshTime =
                now +
                Mathf.Max(
                    5f,
                    metricSourceRefreshIntervalSeconds);

            var behaviours =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            _metricSources.Clear();

            foreach (var behaviour in behaviours)
            {
                if (behaviour is
                        IRuntimeMetricsSource source &&
                    !ReferenceEquals(
                        source,
                        this))
                {
                    _metricSources.Add(
                        source);
                }
            }
        }

        private void NotifySnapshotUpdated(
            RuntimeDiagnosticsSnapshot snapshot)
        {
            var subscribers =
                Volatile.Read(
                    ref _snapshotSubscribers);

            foreach (var subscriber in
                     subscribers)
            {
                try
                {
                    subscriber(
                        snapshot);
                }
                catch
                {
                    _snapshotSubscriberFailureCount++;
                }
            }
        }

        private static bool IsServiceAlive(
            object service)
        {
            if (service == null)
            {
                return false;
            }

            return service is UnityEngine.Object unityObject
                ? unityObject != null
                : true;
        }

        private void ResolveProvider()
        {
            if (trackingProviderBehaviour != null &&
                trackingProviderBehaviour is
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
                if (behaviour is ITrackingMixProvider mixer)
                {
                    SetTrackingProvider(mixer);
                    trackingProviderBehaviour = behaviour;
                    return;
                }

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

        private void WriteSystemEvidence()
        {
            try
            {
                var fileName =
                    string.IsNullOrWhiteSpace(
                        systemEvidenceFileName)
                        ? "vcr-runtime-system.txt"
                        : systemEvidenceFileName;

                var path =
                    Path.Combine(
                        Application.persistentDataPath,
                        fileName);

                var builder =
                    new StringBuilder(512);

                builder.AppendLine(
                    "VirtualCharacterRender runtime evidence");
                builder.AppendLine(
                    "utc=" +
                    DateTime.UtcNow.ToString("O"));
                builder.AppendLine(
                    "os=" +
                    SystemInfo.operatingSystem);
                builder.AppendLine(
                    "cpu=" +
                    SystemInfo.processorType);
                builder.AppendLine(
                    "cpu_count=" +
                    SystemInfo.processorCount);
                builder.AppendLine(
                    "cpu_mhz=" +
                    SystemInfo.processorFrequency);
                builder.AppendLine(
                    "gpu=" +
                    SystemInfo.graphicsDeviceName);
                builder.AppendLine(
                    "gpu_api=" +
                    SystemInfo.graphicsDeviceType);
                builder.AppendLine(
                    "gpu_memory_mb=" +
                    SystemInfo.graphicsMemorySize);
                builder.AppendLine(
                    "ram_mb=" +
                    SystemInfo.systemMemorySize);
                builder.AppendLine(
                    "unity=" +
                    Application.unityVersion);
                builder.AppendLine(
                    "development_build=" +
                    Debug.isDebugBuild);
                builder.AppendLine(
                    $"startup_screen={Screen.width}x{Screen.height}");
                builder.AppendLine(
                    "persistent_data_path=" +
                    Application.persistentDataPath);

                File.WriteAllText(
                    path,
                    builder.ToString());

                if (logToConsole)
                {
                    Debug.Log(
                        $"VCR runtime evidence: system='{path}', csv='{Path.Combine(Application.persistentDataPath, csvFileName)}'",
                        this);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"VCR runtime system evidence: {exception.Message}",
                    this);
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
                        ? "vcr-runtime-diagnostics.csv"
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
                    $"VCR runtime diagnostics CSV: {exception.Message}",
                    this);
                writeCsvEvidence = false;
            }
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
