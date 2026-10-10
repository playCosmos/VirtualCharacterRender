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
            public string error;
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
                timestampUtc = DateTime.UtcNow.ToString("o"),
                error = string.Empty
            };

            var bootstrap = GetComponent<ApplicationRuntimeBootstrap>();
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
                else
                {
                    try
                    {
                        var clean = bootstrap.Shutdown(
                            saveConfiguration: false,
                            out var shutdownError);
                        evidence.stopped = clean &&
                            scene.State == SceneRuntimeState.Stopped &&
                            string.IsNullOrWhiteSpace(shutdownError);
                        if (!evidence.stopped)
                        {
                            evidence.error =
                                "Shutdown failed: " +
                                (shutdownError ?? scene.Status.LastError ?? "unknown");
                        }
                    }
                    catch (Exception exception)
                    {
                        evidence.error =
                            "Shutdown exception: " + exception;
                    }
                }
            }

            evidence.passed = evidence.started &&
                evidence.sceneReady &&
                evidence.noCharacter &&
                evidence.stopped &&
                string.IsNullOrWhiteSpace(evidence.error);

            try
            {
                var output = Path.GetFullPath(reportPath);
                var directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Only a real Player writes this evidence; the runner must
                // check exit code, file contents and exact source SHA.
                File.WriteAllText(
                    output,
                    JsonUtility.ToJson(evidence, prettyPrint: true));
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "VCR PLAYER SMOKE evidence write failed: " +
                    exception);
                UnityEngine.Application.Quit(1);
                yield break;
            }

            Debug.Log(
                "VCR PLAYER SMOKE " +
                (evidence.passed ? "PASS" : "FAIL") +
                ": " + evidence.error);
            UnityEngine.Application.Quit(evidence.passed ? 0 : 1);
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
