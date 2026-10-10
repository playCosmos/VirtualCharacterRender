using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VCR.Runtime.Diagnostics;
using VCR.Runtime.Output;
using VCR.Runtime.Scene;

namespace VCR.Runtime.Application
{
    /// <summary>
    /// Opt-in interactive Player telemetry only; does not assert OBS alpha,
    /// visual fidelity, capture success, or performance PASS.
    /// Never samples or writes anything on a normal launch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractiveOutputTelemetry : MonoBehaviour
    {
        private const string ReportOption = "--vcr-interactive-evidence=";
        private const int MaxSamples = 360;
        private const float SampleIntervalSeconds = 5f;

        [Serializable]
        private sealed class Sample
        {
            public string timestampUtc;
            public float uptimeSeconds;
            public bool applicationFocused;
            public bool runtimeStarted;
            public string sceneState;
            public string sceneError;
            public bool characterLoaded;
            public int requestedWidth;
            public int requestedHeight;
            public int targetFrameRate;
            public bool runInBackground;
            public int screenWidth;
            public int screenHeight;
            public bool overlayStatusAvailable;
            public string overlayState;
            public string overlayError;
            public int overlayClientWidth;
            public int overlayClientHeight;
            public bool transparentRequested;
            public bool topmostRequested;
            public bool clickThroughRequested;
            public bool overlayCaptureReady;
            public string overlayCaptureFailure;
            public string broadcastTarget;
            public bool broadcastCaptureReady;
            public string broadcastCaptureFailure;
            public bool hasFrameMeasurements;
            public float frameAverageMs;
            public float frameP95Ms;
            public float frameP99Ms;
        }

        [Serializable]
        private sealed class Report
        {
            public string suite = "p10-interactive-runtime-telemetry-v1";
            public string assurance = "runtime-status-only; not GUI/OBS alpha proof";
            public string unityVersion;
            public string platform;
            public string osVersion;
            public string processor;
            public string graphicsDevice;
            public string graphicsApi;
            public string startedAtUtc;
            public string updatedAtUtc;
            public bool quitCallbackObserved;
            public int totalSamples;
            public List<Sample> samples = new List<Sample>();
        }

        private ApplicationRuntimeBootstrap _bootstrap;
        private RuntimeDiagnostics _diagnostics;
        private string _reportPath;
        private Report _report;
        private bool _focused;
        private bool _writeFailureLogged;

        private IEnumerator Start()
        {
            if (Application.isEditor)
            {
                yield break;
            }

            _reportPath = ReadReportPath();
            if (string.IsNullOrEmpty(_reportPath))
            {
                yield break;
            }

            // Do not create native output, start tracking or alter UI state.
            _bootstrap = GetComponent<ApplicationRuntimeBootstrap>();
            _diagnostics = GetComponent<RuntimeDiagnostics>();
            _focused = Application.isFocused;
            _report = new Report
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                osVersion = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                startedAtUtc = DateTime.UtcNow.ToString("o")
            };

            // Allow the actual interactive window and runtime to initialize.
            yield return new WaitForSecondsRealtime(2f);

            while (true)
            {
                Capture();
                Persist();
                yield return new WaitForSecondsRealtime(SampleIntervalSeconds);
            }
        }

        private void Capture()
        {
            if (_report == null)
            {
                return;
            }

            var sample = new Sample
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                uptimeSeconds = Time.realtimeSinceStartup,
                applicationFocused = _focused,
                runtimeStarted = _bootstrap != null && _bootstrap.IsStarted,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                sceneState = "Unavailable",
                sceneError = string.Empty,
                overlayState = "Unavailable",
                overlayError = string.Empty,
                overlayCaptureFailure = "Unavailable",
                broadcastTarget = "Unavailable",
                broadcastCaptureFailure = "Unavailable"
            };

            var scene = _bootstrap != null
                ? _bootstrap.SceneRuntime
                : null;

            if (scene != null)
            {
                sample.sceneState = scene.State.ToString();
                sample.sceneError = scene.Status.LastError ?? string.Empty;
                sample.characterLoaded = scene.CurrentCharacter != null;

                if (scene.TryCaptureRenderSettings(out var render))
                {
                    sample.requestedWidth = render.Width;
                    sample.requestedHeight = render.Height;
                    sample.targetFrameRate = render.TargetFrameRate;
                    sample.runInBackground = render.RunInBackground;
                }

                if (scene.TryCaptureOverlayOutput(
                        out var status,
                        out var settings,
                        out var overlayError))
                {
                    sample.overlayStatusAvailable = true;
                    sample.overlayState = status.State.ToString();
                    sample.overlayError = status.LastError ?? string.Empty;
                    sample.overlayClientWidth = status.ClientWidth;
                    sample.overlayClientHeight = status.ClientHeight;
                    sample.transparentRequested = settings.Transparent;
                    sample.topmostRequested = settings.Topmost;
                    sample.clickThroughRequested = settings.ClickThrough;
                    var readiness = OverlayCaptureReadinessEvaluator.Evaluate(
                        status, settings);
                    sample.overlayCaptureReady = readiness.Ready;
                    sample.overlayCaptureFailure = readiness.Failure.ToString();
                }
                else
                {
                    sample.overlayError = overlayError ?? "Overlay unavailable.";
                }

                var target = sample.requestedWidth == 1280 &&
                             sample.requestedHeight == 720
                    ? BroadcastCaptureTarget.Minimum720p60
                    : sample.requestedWidth == 1920 &&
                      sample.requestedHeight == 1080
                        ? BroadcastCaptureTarget.Recommended1080p60
                        : new BroadcastCaptureTarget(
                            BroadcastCaptureTargetTier.Custom,
                            sample.requestedWidth,
                            sample.requestedHeight,
                            60);
                sample.broadcastTarget = target.Tier.ToString();
                var capture = scene.EvaluateBroadcastCaptureTarget(target);
                sample.broadcastCaptureReady = capture.Ready;
                sample.broadcastCaptureFailure = capture.Failure.ToString();
            }

            if (_diagnostics != null)
            {
                var snapshot = _diagnostics.LatestSnapshot;
                if (snapshot.Sequence > 0)
                {
                    sample.hasFrameMeasurements = true;
                    sample.frameAverageMs = snapshot.FrameAverageMs;
                    sample.frameP95Ms = snapshot.FrameP95Ms;
                    sample.frameP99Ms = snapshot.FrameP99Ms;
                }
            }

            if (_report.samples.Count >= MaxSamples)
            {
                _report.samples.RemoveAt(0);
            }

            _report.samples.Add(sample);
            _report.totalSamples++;
            _report.updatedAtUtc = sample.timestampUtc;
        }

        private void Persist()
        {
            if (_report == null)
            {
                return;
            }

            try
            {
                var file = Path.GetFullPath(_reportPath);
                var directory = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Persist even before quit so a native crash leaves evidence.
                File.WriteAllText(file, JsonUtility.ToJson(_report, true));
                _writeFailureLogged = false;
            }
            catch (Exception exception)
            {
                if (!_writeFailureLogged)
                {
                    Debug.LogError("VCR interactive telemetry write failed: " +
                        exception.Message, this);
                    _writeFailureLogged = true;
                }
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
        }

        private void OnApplicationQuit()
        {
            if (_report == null)
            {
                return;
            }

            // No native calls here: another component may already have
            // destroyed the window. Last periodic sample remains intact.
            _report.quitCallbackObserved = true;
            _report.updatedAtUtc = DateTime.UtcNow.ToString("o");
            Persist();
        }

        private static string ReadReportPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (argument != null &&
                    argument.StartsWith(ReportOption, StringComparison.Ordinal))
                {
                    var value = argument.Substring(ReportOption.Length);
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }
    }
}
