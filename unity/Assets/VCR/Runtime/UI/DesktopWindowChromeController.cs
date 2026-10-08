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
        [SerializeField] private bool borderlessStandalone = false;
        [SerializeField, Min(0.1f)] private float borderlessRetrySeconds = 0.5f;

        private Vector2 _dragOffset;
        private bool _dragging;
        private bool _borderlessConfirmed;
        private bool _nativeChromeRequested;
        private float _nextBorderlessRetryAt;

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
                if (UnityEngine.Application.isEditor)
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

        public void UseNativeChrome()
        {
            borderlessStandalone = false;
            _nativeChromeRequested = true;
            _borderlessConfirmed = false;
            _dragging = false;

            if (UnityEngine.Application.isEditor)
            {
                return;
            }

            try
            {
                if (IsBorderlessNative())
                {
                    SetBorderlessNative(
                        false);
                }
            }
            catch (DllNotFoundException)
            {
                // Native window integration is optional for the main settings UI.
            }
            catch (EntryPointNotFoundException)
            {
                // Fall back to the operating system window frame.
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
            ApplyRequestedChromeMode(
                force: true);
        }

        private void Update()
        {
            ApplyRequestedChromeMode(
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
                ApplyRequestedChromeMode(
                    force: true);
            }
        }

        public void BeginDrag()
        {
            if (UnityEngine.Application.isEditor ||
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
                UnityEngine.Application.isEditor ||
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
            if (UnityEngine.Application.isEditor)
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
            UnityEngine.Application.Quit();
#endif
        }

        private void ApplyRequestedChromeMode(
            bool force)
        {
            if (UnityEngine.Application.isEditor)
            {
                return;
            }

            if (_nativeChromeRequested)
            {
                ApplyNativeChromeIfNeeded();
                return;
            }

            ApplyBorderlessIfNeeded(
                force);
        }

        private void ApplyNativeChromeIfNeeded()
        {
            try
            {
                if (IsBorderlessNative())
                {
                    SetBorderlessNative(
                        false);
                }
            }
            catch (DllNotFoundException)
            {
                // Keep the UI usable on unsupported development targets.
            }
            catch (EntryPointNotFoundException)
            {
                // Fall back to the operating system window frame.
            }
        }

        private void ApplyBorderlessIfNeeded(
            bool force)
        {
            if (!borderlessStandalone)
            {
                return;
            }

            // IMPORTANT: never keep applying SetBorderless(true) after the
            // native window already reports borderless. On Windows each style
            // mutation can trigger another non-client-area recalculation; the
            // old 0.5s unconditional call caused a slow cumulative shrink.
            if (_borderlessConfirmed)
            {
                return;
            }

            var now =
                Time.unscaledTime;

            if (!force &&
                now <
                    _nextBorderlessRetryAt)
            {
                return;
            }

            _nextBorderlessRetryAt =
                now +
                Mathf.Max(
                    0.1f,
                    borderlessRetrySeconds);

            try
            {
                if (IsBorderlessNative())
                {
                    _borderlessConfirmed = true;
                    return;
                }

                SetBorderlessNative(
                    true);

                _borderlessConfirmed =
                    IsBorderlessNative();
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
            EntryPoint = "IsBorderless",
            CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsBorderlessNative();

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
