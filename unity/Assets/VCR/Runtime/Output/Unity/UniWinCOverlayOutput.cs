using System;
using System.Collections.Generic;
using Kirurobo;
using UnityEngine;
using UnityEngine.Rendering;
using VCR.Runtime.Core;

namespace VCR.Runtime.Output.Unity
{
    /// <summary>
    /// Cross-platform transparent desktop-window adapter backed by UniWinC.
    ///
    /// The shared runtime sees only IOverlayOutputAdapter. Kirurobo/native
    /// window objects remain inside this Unity/platform boundary.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UniWindowController))]
    [DefaultExecutionOrder(100)]
    public sealed class UniWinCOverlayOutput :
        MonoBehaviour,
        IOverlayOutputAdapter,
        IRuntimeMetricsSource
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        [Header("Overlay")]
        [SerializeField] private bool transparent = true;
        [SerializeField] private bool topmost = true;
        [SerializeField] private bool clickThrough = false;

        private UniWindowController _controller;
        private string _lastError;
        private bool _pendingNativeApply;
        private bool _nativeApplied;

        private CameraClearFlags _originalClearFlags;
        private Color _originalBackground;
        private bool _originalAllowHdr;
        private bool _cameraStateCaptured;

        public OverlayOutputStatus Status
        {
            get
            {
                var size =
                    _controller != null
                        ? _controller.clientSize
                        : Vector2.zero;

                return new OverlayOutputStatus(
                    IsPlatformSupported(),
                    !Application.isEditor &&
                    IsPlatformSupported() &&
                    _nativeApplied &&
                    string.IsNullOrEmpty(_lastError),
                    "uniwinc-0.9.8",
                    _lastError,
                    Mathf.RoundToInt(size.x),
                    Mathf.RoundToInt(size.y));
            }
        }

        private void Awake()
        {
            _controller = GetComponent<UniWindowController>();

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            CaptureCameraState();
            ConfigureCameraForAlpha();

            if (_controller != null)
            {
                _controller.isHitTestEnabled = false;
                _controller.hitTestType =
                    UniWindowController.HitTestType.None;
                _controller.autoSwitchCameraBackground = false;
                _controller.transparentType =
                    UniWindowController.TransparentType.Alpha;

                if (targetCamera != null)
                {
                    _controller.SetCamera(targetCamera);
                }
            }
        }

        private void Start()
        {
            Apply(CurrentSettings());
        }

        private void Update()
        {
            if (Application.isEditor ||
                !_pendingNativeApply ||
                _controller == null ||
                !string.IsNullOrEmpty(_lastError))
            {
                return;
            }

            ApplyNativeSettings();
        }

        public void ConfigureForP0(Camera camera)
        {
            targetCamera = camera;
            transparent = true;
            topmost = true;
            clickThrough = false;
        }

        public void Apply(OverlayOutputSettings settings)
        {
            transparent = settings.Transparent;
            topmost = settings.Topmost;
            clickThrough = settings.ClickThrough;

            if (transparent)
            {
                ConfigureCameraForAlpha();
            }
            else
            {
                RestoreCameraState();
            }

            _lastError = ValidateRuntime();

            if (_controller == null)
            {
                _lastError =
                    "UniWindowController component is missing.";
                return;
            }

            _controller.isHitTestEnabled = false;
            _controller.hitTestType =
                UniWindowController.HitTestType.None;
            _controller.autoSwitchCameraBackground = false;
            _controller.transparentType =
                UniWindowController.TransparentType.Alpha;

            if (targetCamera != null)
            {
                _controller.SetCamera(targetCamera);
            }

            // Native transparency is available only in standalone players.
            // Editor still receives camera/window configuration for setup.
            _nativeApplied = false;
            _pendingNativeApply =
                !Application.isEditor &&
                string.IsNullOrEmpty(_lastError);

            if (_pendingNativeApply)
            {
                ApplyNativeSettings();
            }
        }

        private void ApplyNativeSettings()
        {
            // UniWindowController may not have attached the native window yet.
            // Transparent/click-through retain requested state before attach,
            // while topmost requires the native core. Reapply until topmost
            // reports the requested value.
            _controller.transparentType =
                UniWindowController.TransparentType.Alpha;
            _controller.isTransparent = transparent;
            _controller.isClickThrough = clickThrough;
            _controller.isTopmost = topmost;

            var topmostApplied =
                _controller.isTopmost == topmost;

            if (!topmostApplied)
            {
                return;
            }

            _nativeApplied = true;
            _pendingNativeApply = false;
        }

        public void CollectMetrics(List<RuntimeMetric> output)
        {
            if (output == null)
            {
                return;
            }

            var status = Status;

            output.Add(new RuntimeMetric(
                "output.overlay.supported",
                status.Supported ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "output.overlay.client_width",
                status.ClientWidth,
                "px"));

            output.Add(new RuntimeMetric(
                "output.overlay.client_height",
                status.ClientHeight,
                "px"));

            output.Add(new RuntimeMetric(
                "output.overlay.transparent",
                transparent ? 1 : 0,
                "bool"));
        }

        private OverlayOutputSettings CurrentSettings()
        {
            return new OverlayOutputSettings(
                transparent,
                topmost,
                clickThrough);
        }

        private void CaptureCameraState()
        {
            if (targetCamera == null ||
                _cameraStateCaptured)
            {
                return;
            }

            _originalClearFlags =
                targetCamera.clearFlags;
            _originalBackground =
                targetCamera.backgroundColor;
            _originalAllowHdr =
                targetCamera.allowHDR;
            _cameraStateCaptured = true;
        }

        private void ConfigureCameraForAlpha()
        {
            if (targetCamera == null)
            {
                return;
            }

            CaptureCameraState();

            targetCamera.clearFlags =
                CameraClearFlags.SolidColor;
            targetCamera.backgroundColor =
                new Color(0f, 0f, 0f, 0f);

            // SDR RGBA8 is the lowest-complexity P0 alpha path.
            // HDR alpha remains a later measured option.
            targetCamera.allowHDR = false;
        }

        private string ValidateRuntime()
        {
            if (Application.isEditor)
            {
                return null;
            }

            if (!IsPlatformSupported())
            {
                return
                    "Transparent overlay output is supported only on Windows/macOS standalone.";
            }

#if UNITY_STANDALONE_WIN
            if (SystemInfo.graphicsDeviceType ==
                GraphicsDeviceType.Direct3D12)
            {
                return
                    "Windows alpha transparency requires the P0 D3D11 path; D3D12 is not supported by UniWinC transparency.";
            }
#endif

            return null;
        }

        private static bool IsPlatformSupported()
        {
            return
                Application.platform ==
                    RuntimePlatform.WindowsPlayer ||
                Application.platform ==
                    RuntimePlatform.OSXPlayer;
        }

        private void OnDisable()
        {
            _pendingNativeApply = false;
            _nativeApplied = false;

            if (_controller != null &&
                !Application.isEditor)
            {
                _controller.isClickThrough = false;
                _controller.isTopmost = false;
                _controller.isTransparent = false;
            }

            RestoreCameraState();
        }

        private void RestoreCameraState()
        {
            if (targetCamera == null ||
                !_cameraStateCaptured)
            {
                return;
            }

            targetCamera.clearFlags =
                _originalClearFlags;
            targetCamera.backgroundColor =
                _originalBackground;
            targetCamera.allowHDR =
                _originalAllowHdr;
        }
    }
}
