using System;
using System.Runtime.InteropServices;
using Kirurobo;
using UnityEngine;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Owns the native desktop window chrome used by the in-app title bar.
    /// UniWinC remains the single native window attachment; this controller
    /// only keeps the attached player borderless and exposes window actions
    /// to the runtime UI.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UniWindowController))]
    [DefaultExecutionOrder(200)]
    public sealed class DesktopWindowChromeController :
        MonoBehaviour
    {
        [SerializeField] private bool borderlessStandalone = true;
        [SerializeField, Min(0.1f)] private float borderlessReapplySeconds = 0.5f;

        private UniWindowController _window;
        private Vector2 _dragOffset;
        private bool _dragging;
        private float _nextBorderlessApplyAt;

        public bool CanMinimize =>
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public bool IsZoomed =>
            _window != null &&
            _window.isZoomed;

        private void Awake()
        {
            _window =
                GetComponent<UniWindowController>();

            if (_window != null)
            {
                _window.forceWindowed = true;
            }
        }

        private void Start()
        {
            ApplyBorderlessIfNeeded(
                force: true);
        }

        private void Update()
        {
            ApplyBorderlessIfNeeded(
                force: false);

            if (_dragging)
            {
                DragToCursor();
            }
        }

        private void OnApplicationFocus(
            bool hasFocus)
        {
            if (hasFocus)
            {
                ApplyBorderlessIfNeeded(
                    force: true);
            }
        }

        public void BeginDrag()
        {
            if (_window == null ||
                _window.isZoomed)
            {
                _dragging = false;
                return;
            }

            _dragOffset =
                _window.cursorPosition -
                _window.windowPosition;
            _dragging = true;
        }

        public void DragToCursor()
        {
            if (!_dragging ||
                _window == null ||
                _window.isZoomed)
            {
                return;
            }

            _window.windowPosition =
                _window.cursorPosition -
                _dragOffset;
        }

        public void EndDrag()
        {
            _dragging = false;
        }

        public void ToggleZoom()
        {
            if (_window == null)
            {
                return;
            }

            _dragging = false;
            _window.isZoomed =
                !_window.isZoomed;

            ApplyBorderlessIfNeeded(
                force: true);
        }

        public void Minimize()
        {
            _dragging = false;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var window =
                GetActiveWindow();

            if (window != IntPtr.Zero)
            {
                ShowWindow(
                    window,
                    ShowWindowMinimize);
            }
#endif
        }

        public void CloseApplication()
        {
            _dragging = false;

#if UNITY_EDITOR
            return;
#else
            Application.Quit();
#endif
        }

        private void ApplyBorderlessIfNeeded(
            bool force)
        {
            if (!borderlessStandalone ||
                Application.isEditor)
            {
                return;
            }

            var now =
                Time.unscaledTime;

            if (!force &&
                now <
                    _nextBorderlessApplyAt)
            {
                return;
            }

            _nextBorderlessApplyAt =
                now +
                Mathf.Max(
                    0.1f,
                    borderlessReapplySeconds);

            try
            {
                SetBorderlessNative(
                    true);
            }
            catch (DllNotFoundException)
            {
                // UniWinC is optional outside supported standalone targets.
            }
            catch (EntryPointNotFoundException)
            {
                // Keep the player usable if a future UniWinC binary changes.
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int ShowWindowMinimize = 6;

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(
            IntPtr hWnd,
            int nCmdShow);
#endif

        [DllImport(
            "LibUniWinC",
            EntryPoint = "SetBorderless",
            CallingConvention = CallingConvention.Winapi)]
        private static extern void SetBorderlessNative(
            [MarshalAs(UnmanagedType.U1)]
            bool enabled);
    }
}
