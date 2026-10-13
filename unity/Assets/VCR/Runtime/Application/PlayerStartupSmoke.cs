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
        private const float StartupTimeoutSeconds = 25f;

        [Serializable]
        private sealed class SmokeReport
        {
            public string suite = "player-startup-shutdown";
            public string unityVersion;
            public string platform;
            public string timestampUtc;
            public bool started;
            public bool sceneReady;
            public bool noCharacter;
            public bool stopped;
            public bool passed;
            public long quitManagedMilliseconds;
            public string error;
        }

        private string _reportPath;
        private SmokeReport _evidence;
        private ApplicationRuntimeBootstrap _bootstrap;
        private System.Diagnostics.Stopwatch _quitTimer;
        private bool _quitRequested;

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
                timestampUtc = DateTime.UtcNow.ToString("o"),
                error = string.Empty
            };

            _reportPath = Path.GetFullPath(reportPath);
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
            if (!_quitRequested || _evidence == null)
                return;

            // Must execute after the runtime's OnApplicationQuit callbacks.
            // The report is intentionally missing if normal shutdown never
            // reaches this callback; CI treats missing evidence as failure.
            var scene = _bootstrap != null ? _bootstrap.SceneRuntime : null;
            _evidence.stopped = _bootstrap != null &&
                !_bootstrap.IsStarted &&
                scene != null &&
                scene.State == SceneRuntimeState.Stopped &&
                string.IsNullOrWhiteSpace(scene.Status.LastError);
            if (!_evidence.stopped && string.IsNullOrWhiteSpace(_evidence.error))
                _evidence.error = "Normal OnApplicationQuit did not stop runtime: " +
                    (scene?.Status.LastError ?? "runtime still active");

            _evidence.quitManagedMilliseconds =
                _quitTimer?.ElapsedMilliseconds ?? -1;
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
                ", normal quit cleanup " +
                _evidence.quitManagedMilliseconds + " ms: " + _evidence.error);
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
