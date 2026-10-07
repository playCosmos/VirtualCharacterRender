using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Native bridge for the custom in-app title bar.
    ///
    /// UniWinC's managed controller remains owned by the output layer. The UI
    /// talks only to the small public native window surface that UniWinC
    /// already ships, keeping the UI assembly independent from Kirurobo's
    /// managed assembly.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class DesktopWindowChromeController :
        MonoBehaviour
    {
        [SerializeField] private bool borderlessStandalone = true;
        [SerializeField, Min(0.1f)] private float borderlessReapplySeconds = 0.5f;

        private Vector2 _dragOffset;
        private bool _dragging;
        private float _nextBorderlessApplyAt;

        public bool CanMinimize =>
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public bool IsZoomed
        {
            get
            {
                if (Application.isEditor)
                {
                    return false;
                }

                try
                {
                    return IsMaximizedNative();
                }
                catch (DllNotFoundException)
                {
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    return false;
                }
            }
        }

        private void Awake()
        {
#if !UNITY_EDITOR
            if (Screen.fullScreen)
            {
                Screen.fullScreen = false;
            }
#endif
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
            if (Application.isEditor ||
                IsZoomed)
            {
                _dragging = false;
                return;
            }

            if (!TryGetCursorPosition(
                    out var cursor) ||
                !TryGetWindowPosition(
                    out var window))
            {
                _dragging = false;
                return;
            }

            _dragOffset =
                cursor -
                window;
            _dragging = true;
        }

        public void DragToCursor()
        {
            if (!_dragging ||
                Application.isEditor ||
                IsZoomed)
            {
                return;
            }

            if (!TryGetCursorPosition(
                    out var cursor))
            {
                return;
            }

            try
            {
                SetPositionNative(
                    cursor.x -
                        _dragOffset.x,
                    cursor.y -
                        _dragOffset.y);
            }
            catch (DllNotFoundException)
            {
                _dragging = false;
            }
            catch (EntryPointNotFoundException)
            {
                _dragging = false;
            }
        }

        public void EndDrag()
        {
            _dragging = false;
        }

        public void ToggleZoom()
        {
            if (Application.isEditor)
            {
                return;
            }

            _dragging = false;

            try
            {
                SetMaximizedNative(
                    !IsMaximizedNative());
            }
            catch (DllNotFoundException)
            {
                return;
            }
            catch (EntryPointNotFoundException)
            {
                return;
            }

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
                // Keep the UI usable on unsupported development targets.
            }
            catch (EntryPointNotFoundException)
            {
                // Keep the UI usable if a future UniWinC binary changes.
            }
        }

        private static bool TryGetCursorPosition(
            out Vector2 position)
        {
            position = Vector2.zero;

            try
            {
                if (!GetCursorPositionNative(
                        out var x,
                        out var y))
                {
                    return false;
                }

                position =
                    new Vector2(
                        x,
                        y);
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static bool TryGetWindowPosition(
            out Vector2 position)
        {
            position = Vector2.zero;

            try
            {
                if (!GetPositionNative(
                        out var x,
                        out var y))
                {
                    return false;
                }

                position =
                    new Vector2(
                        x,
                        y);
                return true;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
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

        [DllImport(
            "LibUniWinC",
            EntryPoint = "IsMaximized",
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsMaximizedNative();

        [DllImport(
            "LibUniWinC",
            EntryPoint = "SetMaximized",
            CallingConvention = CallingConvention.Winapi)]
        private static extern void SetMaximizedNative(
            [MarshalAs(UnmanagedType.U1)]
            bool maximized);

        [DllImport(
            "LibUniWinC",
            EntryPoint = "GetPosition",
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPositionNative(
            out float x,
            out float y);

        [DllImport(
            "LibUniWinC",
            EntryPoint = "SetPosition",
            CallingConvention = CallingConvention.Winapi)]
        private static extern void SetPositionNative(
            float x,
            float y);

        [DllImport(
            "LibUniWinC",
            EntryPoint = "GetCursorPosition",
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPositionNative(
            out float x,
            out float y);
    }
}
