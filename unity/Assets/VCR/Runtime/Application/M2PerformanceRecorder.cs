using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using VCR.Runtime.Character;
using VCR.Runtime.Scene;
using VCR.Runtime.Tracking;

namespace VCR.Runtime.Application
{
    /// <summary>
    /// M2 real-desktop measurement only. No work, recorder, or disk writes
    /// unless --vcr-m2-perf-csv=ABSOLUTE_PATH is expressly supplied.
    /// Does not assert that OBS capture, alpha or visual quality passed.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(31000)]
    public sealed class M2PerformanceRecorder : MonoBehaviour
    {
        private const string Option = "--vcr-m2-perf-csv=";
        private const float IntervalSeconds = 5f;
        private const double BinWidthMs = 0.25;
        private const int HistogramBins = 1025;
        private const double FrameBudgetMs = 1000.0 / 60.0;
        private long[] _frameHistogram;
        private StringBuilder _line;
        private ApplicationRuntimeBootstrap _bootstrap;
        private string _path;
        private double _startedAt;
        private double _nextSampleAt;
        private double _sumFrameMs;
        private long _frameCount;
        private long _overBudgetCount;
        private long _saturatedFrameCount;
        private ProfilerRecorder _gcRecorder;
        private bool _gcRecorderStarted;
        private bool _gcRecorderOwned;
        private bool _writeFailed;
        private const int MaxTransitionEvents = 100000;
        private string _eventPath;
        private Vrm10CharacterLoader _characterLoader;
        private ITrackingPresenceProvider _trackingPresence;
        private MonoBehaviour _trackingPresenceBehaviour;
        private long _lastPresenceSequence = -1;
        private long _eventSequence;

        [Serializable]
        private sealed class Session
        {
            public int version = 1;
            public string suite = "m2-real-desktop-performance-v1";
            public string assurance = "runtime-measurements-only; OBS and device acceptance are external";
            public string startedUtc;
            public string platform;
            public string operatingSystem;
            public string architecture;
            public string cpu;
            public string gpu;
            public string graphicsApi;
            public string graphicsDriver;
            public int graphicsMemoryCapacityMb;
            public string unityVersion;
            public bool developmentBuild;
            public float histogramBinWidthMs = (float)BinWidthMs;
            public float histogramOverflowAtMs = (HistogramBins - 1) * (float)BinWidthMs;
            public string percentileMethod = "cumulative-per-session 0.25ms histogram; overflow is lower bound";
            public string frameBudgetMethod = "frames with unscaledDeltaTime greater than 1000/60ms (NOT OBS dropped frames)";
            public string gcAllocationMethod = "ProfilerRecorder GC Allocated In Frame, latest frame only; optional";
        }

        private void Start()
        {
            if (UnityEngine.Application.isEditor)
            {
                return;
            }

            var requested = GetRequestedPath();
            if (requested == null)
            {
                return;
            }

            // Headless CI must never promote itself to physical/GUI evidence.
            if (UnityEngine.Application.isBatchMode || !Path.IsPathRooted(requested))
            {
                Debug.LogError("VCR M2: evidence requires a visible standalone Player and an absolute file path.");
                return;
            }

            try
            {
                _path = Path.GetFullPath(requested);
                var parent = Path.GetDirectoryName(_path);
                if (string.IsNullOrWhiteSpace(parent))
                    throw new InvalidOperationException("Evidence folder is missing.");
                Directory.CreateDirectory(parent);

                // Fail closed: never append a second session to old evidence
                // or overwrite files left behind by a crash.
                var metadata = _path + ".session.json";
                var transitions = _path + ".events.csv";
                if (File.Exists(_path) || File.Exists(metadata) || File.Exists(transitions))
                    throw new IOException("An M2 evidence session already exists at this path.");
                var session = new Session
                {
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    platform = UnityEngine.Application.platform.ToString(),
                    operatingSystem = SystemInfo.operatingSystem,
                    architecture = System.Runtime.InteropServices
                        .RuntimeInformation.ProcessArchitecture.ToString(),
                    cpu = SystemInfo.processorType,
                    gpu = SystemInfo.graphicsDeviceName,
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    graphicsDriver = SystemInfo.graphicsDeviceVersion,
                    graphicsMemoryCapacityMb = SystemInfo.graphicsMemorySize,
                    unityVersion = UnityEngine.Application.unityVersion,
                    developmentBuild = Debug.isDebugBuild
                };
                using (var file = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(session, true));
                using (var file = new FileStream(_path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    writer.WriteLine(
                        "utc,elapsed_seconds,total_frames,slow_frames,overflow_frames," +
                        "frame_average_ms,frame_p95_ms,frame_p99_ms," +
                        "gc_alloc_last_frame_bytes,gc_alloc_available," +
                        "mono_used_bytes,total_allocated_bytes,gc_gen0_collections," +
                        "gc_gen1_collections,gc_gen2_collections,render_width,render_height," +
                        "target_fps,character_loaded,scene_state,overlay_capture_ready,quit_observed");
                // Defer heap storage until an explicitly requested
                // measuring session; normal launches incur no profiler
                // histogram allocation or repeated Update work.
                using (var file = new FileStream(transitions, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                    writer.WriteLine("utc,elapsed_seconds,sequence,event,origin");
                _eventPath = transitions;
                _frameHistogram = new long[HistogramBins];
                _line = new StringBuilder(384);
                _bootstrap = GetComponent<ApplicationRuntimeBootstrap>();
                _startedAt = Time.realtimeSinceStartupAsDouble;
                _nextSampleAt = _startedAt + IntervalSeconds;
                WriteTransition("session_started", "recorder");
                AttachTransitionSources();
                try
                {
                    _gcRecorder = ProfilerRecorder.StartNew(
                        ProfilerCategory.Memory, "GC Allocated In Frame", 1);
                    _gcRecorderOwned = true;
                    _gcRecorderStarted = _gcRecorder.Valid;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("VCR M2: frame-GC recorder unavailable: " +
                        exception.Message, this);
                }
                Debug.Log("VCR M2: opt-in local evidence recording started.", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR M2: evidence was not started: " +
                    exception.Message, this);
                _path = null;
                _writeFailed = true;
            }
        }

        private void Update()
        {
            if (_path == null || _writeFailed)
                return;

            ObserveTrackingPresence();

            var deltaMs = Time.unscaledDeltaTime * 1000.0;
            if (deltaMs > 0 && !double.IsInfinity(deltaMs) && !double.IsNaN(deltaMs))
            {
                _frameCount++;
                _sumFrameMs += deltaMs;
                if (deltaMs > FrameBudgetMs)
                    _overBudgetCount++;
                var index = (int)Math.Min(
                    HistogramBins - 1, Math.Floor(deltaMs / BinWidthMs));
                _frameHistogram[index]++;
                if (index == HistogramBins - 1)
                    _saturatedFrameCount++;
            }

            var now = Time.realtimeSinceStartupAsDouble;
            if (now >= _nextSampleAt)
            {
                Record(now, false);
                _nextSampleAt = now + IntervalSeconds;
            }
        }

        private void OnApplicationQuit()
        {
            if (_path == null || _writeFailed)
                return;
            Record(Time.realtimeSinceStartupAsDouble, true);
            WriteTransition("session_end", "recorder");
            DetachTransitionSources();
            if (_gcRecorderOwned)
                _gcRecorder.Dispose();
            _gcRecorderOwned = false;
            _gcRecorderStarted = false;
        }

        private void OnDestroy()
        {
            DetachTransitionSources();
            if (_gcRecorderOwned)
                _gcRecorder.Dispose();
            _gcRecorderOwned = false;
            _gcRecorderStarted = false;
        }

        // The scene uses one active character. Hook the loader's actual
        // successful lifecycle events rather than guessing from five-second
        // samples. Model replacement fires CharacterLoaded but NOT Unloaded.
        private void AttachTransitionSources()
        {
            _characterLoader = GetComponentInChildren<Vrm10CharacterLoader>(true);
            if (_characterLoader != null)
            {
                _characterLoader.CharacterLoaded += OnCharacterLoaded;
                _characterLoader.CharacterUnloaded += OnCharacterUnloaded;
                _characterLoader.LoadFailed += OnCharacterLoadFailed;
                if (_characterLoader.Current != null)
                    WriteTransition("character_initially_loaded", "character.loader");
            }

            // Only resolves during opt-in evidence recording; the normal
            // desktop Player performs no extra discovery or tracking polling.
            var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                if (behaviour is ITrackingRouteProvider route)
                {
                    _trackingPresence = route;
                    _trackingPresenceBehaviour = behaviour;
                    break;
                }
            }

            if (_trackingPresence == null)
            {
                foreach (var behaviour in behaviours)
                {
                    if (behaviour is ITrackingPresenceProvider presence)
                    {
                        _trackingPresence = presence;
                        _trackingPresenceBehaviour = behaviour;
                        break;
                    }
                }
            }
        }

        private void DetachTransitionSources()
        {
            if (_characterLoader != null)
            {
                _characterLoader.CharacterLoaded -= OnCharacterLoaded;
                _characterLoader.CharacterUnloaded -= OnCharacterUnloaded;
                _characterLoader.LoadFailed -= OnCharacterLoadFailed;
                _characterLoader = null;
            }
            _trackingPresence = null;
            _trackingPresenceBehaviour = null;
        }

        private void OnCharacterLoaded(UniVRM10.Vrm10Instance character)
        {
            WriteTransition("character_loaded", "character.loader");
        }

        private void OnCharacterUnloaded()
        {
            // ApplicationRuntimeBootstrap runs earlier in Unity's execution
            // order and shuts down the scene on normal process exit. Its
            // automatic cleanup must NOT turn a loaded character into an
            // operator-verified load/unload cycle.
            var scene = _bootstrap != null ? _bootstrap.SceneRuntime : null;
            var shuttingDown = scene != null &&
                (scene.State == SceneRuntimeState.ShuttingDown ||
                 scene.State == SceneRuntimeState.Stopped);
            WriteTransition(shuttingDown
                ? "character_unloaded_shutdown"
                : "character_unloaded", "character.loader");
        }

        private void OnCharacterLoadFailed(Exception error)
        {
            WriteTransition("character_load_failed", "character.loader");
        }

        private void ObserveTrackingPresence()
        {
            // Tracking presence is a normalized router state, NOT a physical
            // device connection. The external operator must independently
            // demonstrate real disconnect/reconnect or protocol handshakes.
            if (_trackingPresence == null || _trackingPresenceBehaviour == null)
                return;

            var snapshot = _trackingPresence.Presence;
            if (snapshot.Sequence == _lastPresenceSequence)
                return;
            _lastPresenceSequence = snapshot.Sequence;
            if (snapshot.HasEvent(TrackingPresenceEvents.TrackingSourceLost))
                WriteTransition("tracking_source_lost", "tracking.presence");
            if (snapshot.HasEvent(TrackingPresenceEvents.TrackingSourceRestored))
                WriteTransition("tracking_source_restored", "tracking.presence");
            if (snapshot.HasEvent(TrackingPresenceEvents.SubjectLost))
                WriteTransition("tracking_subject_lost", "tracking.presence");
            if (snapshot.HasEvent(TrackingPresenceEvents.SubjectRestored))
                WriteTransition("tracking_subject_restored", "tracking.presence");
        }

        private void WriteTransition(string eventName, string origin)
        {
            if (_eventPath == null || _writeFailed)
                return;
            if (_eventSequence >= MaxTransitionEvents)
            {
                _writeFailed = true;
                Debug.LogError("VCR M2: transition event safety limit reached; recording invalid.", this);
                return;
            }

            try
            {
                var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                var elapsed = Math.Max(0, Time.realtimeSinceStartupAsDouble - _startedAt)
                    .ToString("F3", CultureInfo.InvariantCulture);
                var line = now + "," + elapsed + "," + (++_eventSequence) +
                    "," + eventName + "," + origin + "\n";
                File.AppendAllText(_eventPath, line, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                _writeFailed = true;
                Debug.LogError("VCR M2: transition recording failed: " +
                    exception.Message, this);
            }
        }

        private void Record(double now, bool quit)
        {
            try
            {
                RecordCore(now, quit);
            }
            catch (Exception exception)
            {
                // Opt-in diagnostics must never abort a normal Unity quit
                // or interfere with the rendering/runtime lifecycle.
                _writeFailed = true;
                Debug.LogError("VCR M2: performance sampling stopped: " +
                    exception.Message, this);
            }
        }

        private void RecordCore(double now, bool quit)
        {
            if (_frameCount == 0)
                return;

            // Quit may have already disposed the overlay/window adapter.
            // An evidence footer must not rediscover native services or
            // re-enter a shutdown scene to read capture status.
            var scene = !quit && _bootstrap != null
                ? _bootstrap.SceneRuntime
                : null;
            var width = 0;
            var height = 0;
            var targetFps = 0;
            var characterLoaded = scene != null && scene.CurrentCharacter != null;
            var sceneState = scene != null ? scene.State.ToString() : "Unavailable";
            var overlayReady = false;
            if (scene != null)
            {
                if (scene.TryCaptureRenderSettings(out var settings))
                {
                    width = settings.Width;
                    height = settings.Height;
                    targetFps = settings.TargetFrameRate;
                }
                overlayReady = scene.OverlayCaptureReadiness.Ready;
            }

            // Current-frame allocation marker is optional: unsupported retail
            // recorders must report unavailable, never invent a zero reading.
            var gcAvailable = _gcRecorderStarted &&
                _gcRecorder.Valid && _gcRecorder.Count > 0;
            var gcBytes = gcAvailable ? _gcRecorder.LastValue : -1L;
            gcAvailable &= gcBytes >= 0;
            _line.Clear();
            _line.Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Append(_line, Math.Max(0, now - _startedAt).ToString("F3", CultureInfo.InvariantCulture));
            Append(_line, _frameCount);
            Append(_line, _overBudgetCount);
            Append(_line, _saturatedFrameCount);
            Append(_line, (_sumFrameMs / _frameCount).ToString("F4", CultureInfo.InvariantCulture));
            Append(_line, Quantile(0.95).ToString("F3", CultureInfo.InvariantCulture));
            Append(_line, Quantile(0.99).ToString("F3", CultureInfo.InvariantCulture));
            Append(_line, gcAvailable ? gcBytes : -1L);
            Append(_line, gcAvailable ? 1 : 0);
            Append(_line, Profiler.GetMonoUsedSizeLong());
            Append(_line, Profiler.GetTotalAllocatedMemoryLong());
            Append(_line, GC.CollectionCount(0));
            Append(_line, GC.CollectionCount(1));
            Append(_line, GC.CollectionCount(2));
            Append(_line, width);
            Append(_line, height);
            Append(_line, targetFps);
            Append(_line, characterLoaded ? 1 : 0);
            Append(_line, sceneState);
            Append(_line, overlayReady ? 1 : 0);
            Append(_line, quit ? 1 : 0);
            _line.Append('\n');

            try
            {
                File.AppendAllText(_path, _line.ToString(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                _writeFailed = true;
                Debug.LogError("VCR M2: local evidence recording failed: " +
                    exception.Message, this);
            }
        }

        private double Quantile(double fraction)
        {
            long threshold = (long)Math.Ceiling(_frameCount * fraction);
            long cumulative = 0;
            for (var i = 0; i < _frameHistogram.Length; i++)
            {
                cumulative += _frameHistogram[i];
                if (cumulative >= threshold)
                    return (i + 0.5) * BinWidthMs;
            }
            return (HistogramBins - 1) * BinWidthMs;
        }

        private static void Append(StringBuilder builder, long value)
        {
            builder.Append(',');
            builder.Append(value);
        }

        private static void Append(StringBuilder builder, string value)
        {
            builder.Append(',');
            builder.Append(value);
        }

        private static string GetRequestedPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument != null && argument.StartsWith(Option, StringComparison.Ordinal))
                {
                    var value = argument.Substring(Option.Length);
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            return null;
        }
    }
}
