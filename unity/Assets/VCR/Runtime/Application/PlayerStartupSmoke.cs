using System;
using System.Collections;
using System.IO;
using UnityEngine;
using VCR.Runtime.Scene;

namespace VCR.Runtime.Application
{
    /// <summary>
    /// Opt-in standalone Player startup/shutdown evidence.
    /// Never runs on a normal user launch.
    /// </summary>
    // Run after ApplicationRuntimeBootstrap (-1000) and the scene runtime.
    // A success report must be written after REAL OnApplicationQuit cleanup,
    // not by explicitly pre-shutting the runtime in Start().
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class PlayerStartupSmoke : MonoBehaviour
    {
        private const string ReportOption = "--vcr-smoke-report=";
        // Opt-in only when a smoke report is requested. A production launch
        // does not use these test assertions or change its quit behavior.
        private const string RecoveryOption = "--vcr-smoke-recovery=";
        private const float StartupTimeoutSeconds = 25f;

        [Serializable]
        private sealed class SmokeReport
        {
            public string suite = "player-startup-shutdown";
            public string unityVersion;
            public string platform;
            public bool developmentBuild;
            public string timestampUtc;
            public bool started;
            public bool sceneReady;
            public bool noCharacter;
            public string recoveryMode;
            public bool automaticSaveSuppressed;
            public bool usedBackupRecovery;
            public bool configurationLoadRejected;
            public bool quitSaveSuppressed;
            // Capture the *applied* camera field of view, not just config JSON:
            // native relaunch evidence must prove settings reached the scene.
            public float observedCameraFieldOfView;
            public bool stopped;
            public bool passed;
            // Application.Quit() invocation can wait for Unity/native dispatch.
            // Neither field claims to be the runtime's cleanup-only duration.
            public long quitCallToManagedCallbackMs;
            public long quitMarkerToManagedCallbackMs;
            public string error;
        }

        private string _reportPath;
        private SmokeReport _evidence;
        private ApplicationRuntimeBootstrap _bootstrap;
        private System.Diagnostics.Stopwatch _quitTimer;
        private bool _quitRequested;
        private string _recoveryMode;

        private void Awake()
        {
            _bootstrap = GetComponent<ApplicationRuntimeBootstrap>();
        }

        private IEnumerator Start()
        {
            var reportPath = GetReportPath();
            if (reportPath == null)
            {
                yield break;
            }

            var evidence = new SmokeReport
            {
                unityVersion = UnityEngine.Application.unityVersion,
                platform = UnityEngine.Application.platform.ToString(),
                developmentBuild = Debug.isDebugBuild,
                timestampUtc = DateTime.UtcNow.ToString("o"),
                error = string.Empty
            };

            _reportPath = Path.GetFullPath(reportPath);
            _recoveryMode = GetRecoveryMode();
            evidence.recoveryMode = _recoveryMode ?? string.Empty;
            if (_recoveryMode != null &&
                _recoveryMode != "backup" &&
                _recoveryMode != "unrecoverable")
            {
                evidence.error = "Unknown recovery smoke mode.";
            }
            _evidence = evidence;
            _bootstrap = GetComponent<ApplicationRuntimeBootstrap>();
            var bootstrap = _bootstrap;
            if (bootstrap == null)
            {
                evidence.error = "ApplicationRuntimeBootstrap is missing.";
            }
            else
            {
                var deadline = Time.realtimeSinceStartup +
                    StartupTimeoutSeconds;
                while (!bootstrap.IsStarted &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                evidence.started = bootstrap.IsStarted;
                var scene = bootstrap.SceneRuntime;
                evidence.sceneReady = scene != null &&
                    scene.State == SceneRuntimeState.Ready;
                evidence.noCharacter = scene != null &&
                    scene.CurrentCharacter == null;
                evidence.automaticSaveSuppressed =
                    bootstrap.AutomaticConfigurationSaveSuppressed;
                evidence.usedBackupRecovery =
                    !string.IsNullOrWhiteSpace(
                        bootstrap.LastConfigurationLoadWarning);
                evidence.configurationLoadRejected =
                    !string.IsNullOrWhiteSpace(
                        bootstrap.LastConfigurationLoadError);
                if (evidence.sceneReady)
                {
                    evidence.observedCameraFieldOfView =
                        scene.CaptureConfiguration().Camera.FieldOfView;
                }

                if (!evidence.started ||
                    !evidence.sceneReady ||
                    !evidence.noCharacter)
                {
                    evidence.error =
                        "Startup incomplete: started=" +
                        evidence.started + ", ready=" +
                        evidence.sceneReady + ", noCharacter=" +
                        evidence.noCharacter +
                        ", sceneError=" + (scene?.Status.LastError ?? "none");
                }
                else if (string.IsNullOrEmpty(evidence.error))
                {
                    bool guard = evidence.automaticSaveSuppressed;
                    bool recovered = evidence.usedBackupRecovery;
                    bool rejected = evidence.configurationLoadRejected;
                    bool expected = _recoveryMode == "backup"
                        ? guard && recovered && !rejected
                        : _recoveryMode == "unrecoverable"
                            ? guard && !recovered && rejected
                            : !guard && !recovered && !rejected;
                    if (!expected)
                    {
                        evidence.error =
                            "Configuration load/guard state mismatch: " +
                            "mode=" + (_recoveryMode ?? "normal") +
                            ", guard=" + guard + ", recovered=" +
                            recovered + ", rejected=" + rejected;
                    }
                }
            }

            // Exercise the actual Unity quit path, including the normal
            // application save and device/capability release. Do not call
            // bootstrap.Shutdown() in advance: it masks exit-only failures.
            _quitRequested = true;
            _quitTimer = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log("VCR PLAYER SMOKE: normal quit requested (t=0 ms)");
            UnityEngine.Application.Quit(
                string.IsNullOrWhiteSpace(evidence.error) ? 0 : 1);
        }

        private void OnApplicationQuit()
        {
            // This component exists in regular GUI Players too. Even without
            // smoke arguments, measure how long other components spent in
            // their OnApplicationQuit callbacks after the bootstrap finished.
            // A late timestamp distinguishes plugin/script callback delays
            // from the native Unity shutdown that follows this callback.
            Debug.Log("VCR exit: late managed quit callback at " +
                (_bootstrap?.ExitElapsedMilliseconds ?? -1) + " ms");

            if (!_quitRequested || _evidence == null)
                return;

            // Must execute after the runtime's OnApplicationQuit callbacks.
            // The report is intentionally missing if normal shutdown never
            // reaches this callback; CI treats missing evidence as failure.
            var scene = _bootstrap != null ? _bootstrap.SceneRuntime : null;
            var shutdownError = _bootstrap?.LastShutdownError;
            _evidence.quitSaveSuppressed =
                _bootstrap != null &&
                !_bootstrap.LastShutdownSucceeded &&
                !string.IsNullOrWhiteSpace(shutdownError) &&
                shutdownError.StartsWith(
                    "Automatic configuration save suppressed",
                    StringComparison.Ordinal) &&
                !shutdownError.Contains(" | ");
            bool expectedGuardedQuit = !string.IsNullOrEmpty(_recoveryMode);
            _evidence.stopped = _bootstrap != null &&
                !_bootstrap.IsStarted &&
                scene != null &&
                scene.State == SceneRuntimeState.Stopped &&
                string.IsNullOrWhiteSpace(scene.Status.LastError) &&
                (expectedGuardedQuit
                    ? _evidence.quitSaveSuppressed
                    : _bootstrap.LastShutdownSucceeded &&
                      !_evidence.quitSaveSuppressed);
            if (!_evidence.stopped && string.IsNullOrWhiteSpace(_evidence.error))
                _evidence.error = "Normal OnApplicationQuit did not stop runtime: " +
                    (_bootstrap?.LastShutdownError ??
                     scene?.Status.LastError ?? "runtime still active");

            _evidence.quitCallToManagedCallbackMs =
                _quitTimer?.ElapsedMilliseconds ?? -1;
            _evidence.quitMarkerToManagedCallbackMs =
                _bootstrap?.ExitElapsedMilliseconds ?? -1;
            _evidence.passed = _evidence.started &&
                _evidence.sceneReady &&
                _evidence.noCharacter &&
                _evidence.stopped &&
                string.IsNullOrWhiteSpace(_evidence.error);
            try
            {
                var directory = Path.GetDirectoryName(_reportPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_reportPath,
                    JsonUtility.ToJson(_evidence, prettyPrint: true));
            }
            catch (Exception exception)
            {
                Debug.LogError("VCR PLAYER SMOKE report write failed: " + exception);
                return;
            }

            Debug.Log("VCR PLAYER SMOKE " +
                (_evidence.passed ? "PASS" : "FAIL") +
                ", Application.Quit call to callback " +
                _evidence.quitCallToManagedCallbackMs +
                " ms, wantsToQuit to callback " +
                _evidence.quitMarkerToManagedCallbackMs +
                " ms: " + _evidence.error);
        }

        private static string GetRecoveryMode()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument != null &&
                    argument.StartsWith(
                        RecoveryOption,
                        StringComparison.Ordinal))
                {
                    return argument.Substring(RecoveryOption.Length);
                }
            }
            return null;
        }

        private static string GetReportPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument != null &&
                    argument.StartsWith(
                        ReportOption,
                        StringComparison.Ordinal))
                {
                    var path = argument.Substring(ReportOption.Length);
                    return string.IsNullOrWhiteSpace(path)
                        ? null
                        : path;
                }
            }

            return null;
        }
    }
}
