using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VCR.Runtime.Core;

namespace VCR.Runtime.Camera
{
    [Flags]
    public enum CameraCaptureConsumer
    {
        None = 0,
        Tracking = 1 << 0,
        Preview = 1 << 1
    }

    public enum CameraCaptureState
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Suspended = 3,
        Faulted = 4
    }

    /// <summary>
    /// Owns the physical webcam independently from tracking and UI.
    ///
    /// Tracking and preview acquire independent consumer flags. The physical
    /// camera is opened once while at least one consumer is active, allowing a
    /// privacy-controlled preview to be toggled without starting/stopping
    /// MediaPipe and allowing MediaPipe to run while the preview remains hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraCaptureRuntime :
        MonoBehaviour,
        IRuntimeMetricsSource
    {
        [Header("Camera")]
        [SerializeField] private string deviceName = "";
        [SerializeField, Min(160)] private int requestedWidth = 640;
        [SerializeField, Min(120)] private int requestedHeight = 480;
        [SerializeField, Range(1, 120)] private int requestedFps = 30;

        [Header("Lifecycle")]
        [SerializeField] private bool suspendOnApplicationPause = true;
        [SerializeField, Min(0.5f)] private float startupTimeoutSeconds = 5f;

        private WebCamTexture _webcam;
        private Coroutine _startupCoroutine;
        private CameraCaptureConsumer _consumers;
        private CameraCaptureState _state =
            CameraCaptureState.Stopped;
        private string _selectedDeviceName;
        private string _lastError;
        private bool _applicationSuspended;
        private bool _destroying;

        public CameraCaptureState State =>
            _state;
        public CameraCaptureConsumer Consumers =>
            _consumers;
        public string SelectedDeviceName =>
            _selectedDeviceName;
        public string LastError =>
            _lastError;
        public bool IsRunning =>
            _state ==
                CameraCaptureState.Running &&
            _webcam != null &&
            _webcam.isPlaying;
        public WebCamTexture CaptureTexture =>
            IsRunning
                ? _webcam
                : null;
        public Texture PreviewTexture =>
            CaptureTexture;
        public int Width =>
            _webcam != null
                ? _webcam.width
                : 0;
        public int Height =>
            _webcam != null
                ? _webcam.height
                : 0;
        public int RequestedFps =>
            requestedFps;

        public bool TryConfigure(
            string requestedDeviceName,
            int width,
            int height,
            int fps,
            out string error)
        {
            error = null;

            if (width < 160 ||
                width > 7680 ||
                height < 120 ||
                height > 4320 ||
                fps < 1 ||
                fps > 120)
            {
                error =
                    "카메라 해상도/FPS 범위를 확인하세요.";
                return false;
            }

            var nextDevice =
                requestedDeviceName?.Trim() ??
                string.Empty;

            var changed =
                !string.Equals(
                    deviceName,
                    nextDevice,
                    StringComparison.Ordinal) ||
                requestedWidth != width ||
                requestedHeight != height ||
                requestedFps != fps;

            deviceName =
                nextDevice;
            requestedWidth =
                width;
            requestedHeight =
                height;
            requestedFps =
                fps;

            if (changed &&
                Application.isPlaying &&
                _consumers !=
                    CameraCaptureConsumer.None &&
                !_applicationSuspended)
            {
                RestartCapture();
            }

            return true;
        }

        public bool TrySetConsumerActive(
            CameraCaptureConsumer consumer,
            bool active,
            out string error)
        {
            error = null;

            if (consumer ==
                    CameraCaptureConsumer.None ||
                (consumer &
                 ~(CameraCaptureConsumer.Tracking |
                   CameraCaptureConsumer.Preview)) != 0)
            {
                error =
                    "지원하지 않는 카메라 소비자입니다.";
                return false;
            }

            if (_destroying)
            {
                error =
                    "카메라 캡처가 종료 중입니다.";
                return false;
            }

            var before =
                _consumers;

            if (active)
            {
                _consumers |=
                    consumer;
            }
            else
            {
                _consumers &=
                    ~consumer;
            }

            if (!Application.isPlaying)
            {
                return true;
            }

            if (_consumers ==
                CameraCaptureConsumer.None)
            {
                StopCapture(
                    CameraCaptureState.Stopped);
                return true;
            }

            if (before ==
                    CameraCaptureConsumer.None &&
                !_applicationSuspended)
            {
                BeginStart();
            }
            else if (_state ==
                         CameraCaptureState.Faulted &&
                     active &&
                     !_applicationSuspended)
            {
                BeginStart();
            }

            return true;
        }

        public void CollectMetrics(
            List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            output.Add(
                new RuntimeMetric(
                    "camera.capture.state",
                    (int)_state,
                    "enum"));
            output.Add(
                new RuntimeMetric(
                    "camera.capture.consumers",
                    (int)_consumers,
                    "flags"));
            output.Add(
                new RuntimeMetric(
                    "camera.capture.width",
                    Width,
                    "px"));
            output.Add(
                new RuntimeMetric(
                    "camera.capture.height",
                    Height,
                    "px"));
        }

        private void BeginStart()
        {
            if (_destroying ||
                _applicationSuspended ||
                _consumers ==
                    CameraCaptureConsumer.None ||
                _state ==
                    CameraCaptureState.Starting ||
                IsRunning)
            {
                return;
            }

            StopPhysicalCamera(
                stopStartupCoroutine: true);

            _lastError = null;
            _state =
                CameraCaptureState.Starting;
            _startupCoroutine =
                StartCoroutine(
                    StartCaptureRoutine());
        }

        private IEnumerator StartCaptureRoutine()
        {
            yield return
                Application.RequestUserAuthorization(
                    UserAuthorization.WebCam);

            if (_destroying ||
                _consumers ==
                    CameraCaptureConsumer.None)
            {
                _startupCoroutine = null;
                StopCapture(
                    CameraCaptureState.Stopped);
                yield break;
            }

            if (!Application.HasUserAuthorization(
                    UserAuthorization.WebCam))
            {
                Fail(
                    "Webcam permission was not granted.");
                yield break;
            }

            var devices =
                WebCamTexture.devices;

            if (devices == null ||
                devices.Length == 0)
            {
                Fail(
                    "No webcam devices were found.");
                yield break;
            }

            _selectedDeviceName =
                SelectDeviceName(
                    devices);

            _webcam =
                new WebCamTexture(
                    _selectedDeviceName,
                    requestedWidth,
                    requestedHeight,
                    requestedFps);
            _webcam.Play();

            var deadline =
                Time.realtimeSinceStartup +
                Math.Max(
                    0.5f,
                    startupTimeoutSeconds);

            while (_webcam != null &&
                   _webcam.isPlaying &&
                   (_webcam.width <= 16 ||
                    _webcam.height <= 16) &&
                   Time.realtimeSinceStartup <
                       deadline)
            {
                yield return null;
            }

            if (_webcam == null ||
                !_webcam.isPlaying ||
                _webcam.width <= 16 ||
                _webcam.height <= 16)
            {
                Fail(
                    "Webcam did not produce a valid frame within the startup timeout.");
                yield break;
            }

            _startupCoroutine =
                null;
            _state =
                CameraCaptureState.Running;
        }

        private string SelectDeviceName(
            WebCamDevice[] devices)
        {
            if (!string.IsNullOrWhiteSpace(
                    deviceName))
            {
                for (var i = 0;
                     i < devices.Length;
                     i++)
                {
                    if (string.Equals(
                            devices[i].name,
                            deviceName,
                            StringComparison.Ordinal))
                    {
                        return devices[i].name;
                    }
                }

                Debug.LogWarning(
                    $"VCR Camera: requested webcam '{deviceName}' was not found; using the first device.",
                    this);
            }

            return devices[0].name;
        }

        private void RestartCapture()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            StopPhysicalCamera(
                stopStartupCoroutine: true);

            if (_consumers !=
                    CameraCaptureConsumer.None &&
                !_applicationSuspended)
            {
                BeginStart();
            }
        }

        private void Fail(
            string error)
        {
            _lastError =
                string.IsNullOrWhiteSpace(
                    error)
                    ? "Camera capture failed."
                    : error;

            Debug.LogError(
                "VCR Camera: " +
                _lastError,
                this);

            StopPhysicalCamera(
                stopStartupCoroutine: false);
            _startupCoroutine =
                null;
            _state =
                CameraCaptureState.Faulted;
        }

        private void StopCapture(
            CameraCaptureState state)
        {
            StopPhysicalCamera(
                stopStartupCoroutine: true);
            _state =
                state;

            if (state ==
                CameraCaptureState.Stopped)
            {
                _lastError = null;
            }
        }

        private void StopPhysicalCamera(
            bool stopStartupCoroutine)
        {
            if (stopStartupCoroutine &&
                _startupCoroutine != null)
            {
                StopCoroutine(
                    _startupCoroutine);
            }

            _startupCoroutine = null;

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

            _selectedDeviceName = null;
        }

        private void OnApplicationPause(
            bool paused)
        {
            if (!suspendOnApplicationPause)
            {
                return;
            }

            _applicationSuspended =
                paused;

            if (paused)
            {
                if (_consumers !=
                    CameraCaptureConsumer.None)
                {
                    StopCapture(
                        CameraCaptureState.Suspended);
                }

                return;
            }

            if (_consumers !=
                CameraCaptureConsumer.None)
            {
                BeginStart();
            }
        }

        private void OnDisable()
        {
            if (_destroying)
            {
                return;
            }

            StopPhysicalCamera(
                stopStartupCoroutine: true);
            _state =
                _applicationSuspended
                    ? CameraCaptureState.Suspended
                    : CameraCaptureState.Stopped;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying ||
                _applicationSuspended ||
                _consumers ==
                    CameraCaptureConsumer.None)
            {
                return;
            }

            BeginStart();
        }

        private void OnDestroy()
        {
            _destroying = true;
            _consumers =
                CameraCaptureConsumer.None;
            StopPhysicalCamera(
                stopStartupCoroutine: true);
            _state =
                CameraCaptureState.Stopped;
        }
    }
}
