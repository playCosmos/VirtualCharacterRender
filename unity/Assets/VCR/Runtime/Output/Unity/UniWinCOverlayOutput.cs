using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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
        IProcessExitOverlayOutputAdapter,
        IRuntimeMetricsSource
    {
        [Header("Camera")]
        [SerializeField] private Camera targetCamera;

        [Header("Overlay")]
        [SerializeField] private bool transparent = true;
        [SerializeField] private bool clickThrough = false;
        // P1 is a standard desktop application. UniWinC's transparent mode
        // intentionally strips the native caption; restore it when opaque.
        [SerializeField] private bool preserveDesktopWindowFrame = false;
        [SerializeField, Min(0.5f)] private float nativeApplyTimeoutSeconds = 5f;

        private UniWindowController _controller;
        private string _lastError;
        private bool _pendingNativeApply;
        private bool _nativeApplied;
        private float _nativeApplyStartedAt;
        private long _applyAttempts;
        private long _applyFailures;
        private long _nativeApplySuccesses;

        private CameraClearFlags _originalClearFlags;
        private Color _originalBackground;
        private bool _originalAllowHdr;
        private bool _cameraStateCaptured;
        private bool _shutdownApplied;

        public OverlayOutputSettings Settings =>
            CurrentSettings();

        public OverlayOutputStatus Status
        {
            get
            {
                var size =
                    _controller != null
                        ? _controller.clientSize
                        : Vector2.zero;

                var supported =
                    IsPlatformSupported();

                var state =
                    !supported &&
                    !Application.isEditor
                        ? OverlayOutputState.Unsupported
                        : !string.IsNullOrEmpty(_lastError)
                            ? OverlayOutputState.Faulted
                            : _nativeApplied
                                ? OverlayOutputState.Active
                                : _pendingNativeApply
                                    ? OverlayOutputState.PendingNativeApply
                                    : transparent
                                        ? OverlayOutputState.Configured
                                        : OverlayOutputState.Inactive;

                return new OverlayOutputStatus(
                    state,
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

            if (Time.realtimeSinceStartup -
                    _nativeApplyStartedAt >=
                Mathf.Max(
                    0.5f,
                    nativeApplyTimeoutSeconds))
            {
                _pendingNativeApply = false;
                _nativeApplied = false;
                _applyFailures++;
                _lastError =
                    "Timed out while applying native overlay window settings.";
                return;
            }

            ApplyNativeSettings();
        }

        public void ConfigureForP0(Camera camera)
        {
            targetCamera = camera;
            transparent = true;
            clickThrough = false;
            preserveDesktopWindowFrame = false;
        }

        // U1/P1 is a normal interactive editor window by default. Transparent
        // desktop output remains opt-in through Settings. Do not change the
        // P0 transparent-alpha validation scene's default.
        public void ConfigureForP1Workspace(Camera camera)
        {
            targetCamera = camera;
            transparent = false;
            clickThrough = false;
            preserveDesktopWindowFrame = true;
        }

        public void Apply(OverlayOutputSettings settings)
        {
            _shutdownApplied = false;
            _applyAttempts++;

            transparent = settings.Transparent;
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
                _applyFailures++;
                return;
            }

            if (!string.IsNullOrEmpty(
                    _lastError))
            {
                _applyFailures++;
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
                _nativeApplyStartedAt =
                    Time.realtimeSinceStartup;
                ApplyNativeSettings();
            }
        }

        // UniWinC buffers requested styles before the native window exists.
        // A default false topmost value is NOT proof of successful attachment.
        [DllImport("LibUniWinC", EntryPoint = "IsActive",
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsNativeWindowAttached();

        private void ApplyNativeSettings()
        {
            // Main rendering window is always a normal OS window. Never
            // restore persisted topmost state or obscure Settings/other apps.
            _controller.transparentType =
                UniWindowController.TransparentType.Alpha;
            _controller.isTransparent = transparent;
            _controller.isClickThrough = clickThrough;
            _controller.isTopmost = false;

            // Do not signal Active until UniWinC has attached the actual
            // desktop window. Keep retrying (with the existing timeout).
            if (!IsNativeWindowAttached())
                return;

            // On Windows, an old P0 borderless window can survive a transition
            // back to opaque. Restore standard OS non-client controls only for
            // P1's opaque workspace, never while a transparent overlay is on.
            if (preserveDesktopWindowFrame && !transparent)
                RestoreWindowsDesktopFrameIfMissing();

            var topmostApplied =
                !_controller.isTopmost;

            if (!topmostApplied)
            {
                return;
            }

            _nativeApplied = true;
            _pendingNativeApply = false;
            _nativeApplySuccesses++;
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int GwlStyle = -16;
        private const uint WsPopup = 0x80000000;
        private const uint WsOverlappedWindow = 0x00CF0000;
        private const uint SwpNoMoveNoSizeNoZOrderFrameChanged =
            0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;

        private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr param);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetWindowLongW(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after,
            int x, int y, int width, int height, uint flags);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();
#endif

        private void RestoreWindowsDesktopFrameIfMissing()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var myPid = GetCurrentProcessId();
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var windowPid);
                if (windowPid != myPid) return true;
                var name = new StringBuilder(64);
                GetClassNameW(hwnd, name, name.Capacity);
                if (!string.Equals(name.ToString(), "UnityWndClass",
                    StringComparison.Ordinal))
                    return true;

                var style = (uint)GetWindowLongW(hwnd, GwlStyle);
                if ((style & WsOverlappedWindow) != WsOverlappedWindow ||
                    (style & WsPopup) != 0)
                {
                    var next = (style | WsOverlappedWindow) & ~WsPopup;
                    SetWindowLongW(hwnd, GwlStyle, unchecked((int)next));
                    var actualStyle = (uint)GetWindowLongW(hwnd, GwlStyle);
                    if ((actualStyle & WsOverlappedWindow) != WsOverlappedWindow ||
                        (actualStyle & WsPopup) != 0 ||
                        !SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                            SwpNoMoveNoSizeNoZOrderFrameChanged))
                        Debug.LogWarning("VCR P1: Windows desktop title bar " +
                            "restoration failed; inspect native window style.", this);
                    else
                        Debug.Log("VCR P1: restored native title bar and resize frame.", this);
                }
                return false;
            }, IntPtr.Zero);
#endif
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

            output.Add(new RuntimeMetric(
                "output.overlay.state",
                (int)status.State,
                "enum"));

            output.Add(new RuntimeMetric(
                "output.overlay.active",
                status.Active ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "output.overlay.pending",
                status.Pending ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "output.overlay.topmost",
                0,
                "bool"));

            output.Add(new RuntimeMetric(
                "output.overlay.click_through",
                clickThrough ? 1 : 0,
                "bool"));

            output.Add(new RuntimeMetric(
                "output.overlay.apply_attempts",
                _applyAttempts,
                "count"));

            output.Add(new RuntimeMetric(
                "output.overlay.apply_failures",
                _applyFailures,
                "count"));

            output.Add(new RuntimeMetric(
                "output.overlay.native_apply_successes",
                _nativeApplySuccesses,
                "count"));
        }

        private OverlayOutputSettings CurrentSettings()
        {
            return new OverlayOutputSettings(
                transparent,
                false,
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

        public void Shutdown()
        {
            if (_shutdownApplied)
            {
                return;
            }

            // Both scene shutdown and Unity OnDisable invoke this method.
            // Native desktop window teardown must be idempotent.
            _shutdownApplied = true;
            _pendingNativeApply = false;
            _nativeApplied = false;
            _nativeApplyStartedAt = 0f;

            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (_controller != null && !Application.isEditor)
                {
                    Debug.Log("VCR UniWinC shutdown: click-through off", this);
                    _controller.isClickThrough = false;
                    Debug.Log("VCR UniWinC shutdown: topmost off", this);
                    _controller.isTopmost = false;
                    Debug.Log("VCR UniWinC shutdown: transparency off", this);
                    _controller.isTransparent = false;
                }
            }
            finally
            {
                RestoreCameraState();
                Debug.Log("VCR UniWinC shutdown: completed (" +
                          timer.ElapsedMilliseconds + " ms)", this);
            }
        }

        public void ShutdownForProcessExit()
        {
            if (_shutdownApplied)
            {
                return;
            }

            // During actual application exit, native window styles will
            // disappear with the OS window. Setting them while the native
            // window is being destroyed can synchronously stall Win32/Cocoa.
            // Keep regular Shutdown() for suspension and explicit stop.
            _shutdownApplied = true;
            _pendingNativeApply = false;
            _nativeApplied = false;
            _nativeApplyStartedAt = 0f;
            RestoreCameraState();
            Debug.Log("VCR UniWinC shutdown: process exit, native window style reset skipped.", this);
        }

        private void OnDisable()
        {
            Shutdown();
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
            _cameraStateCaptured = false;
        }
    }
}
