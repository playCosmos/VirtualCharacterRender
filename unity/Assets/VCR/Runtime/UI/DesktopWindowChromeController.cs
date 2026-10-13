using System;
using System.Runtime.InteropServices;
using System.Text;
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
    [DefaultExecutionOrder(-2000)]
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
        private bool _quitWindowHidden;

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

        private void OnApplicationQuit()
        {
            // Runs before the runtime bootstrap (-1000) at execution order -2000.
            // Unlike wantsToQuit this event cannot be vetoed by a subscriber.
            HideWindowForShutdown();
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
            HideWindowForShutdown();
            UnityEngine.Application.Quit();
#endif
        }


        private void HideWindowForShutdown()
        {
#if UNITY_EDITOR
            return;
#else
            if (_quitWindowHidden)
                return;

            // Hidden Unity windows must keep processing the remaining quit
            // callbacks even when the persisted preference was background-off.
            UnityEngine.Application.runInBackground = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
#if UNITY_STANDALONE_WIN
                var window = FindUnityMainWindow();
                if (window != IntPtr.Zero)
                {
                    ShowWindow(window, ShowWindowHide);
                    _quitWindowHidden = true;
                }
                // Settings is an independent top-level window. Hide it as
                // well so no visible VCR window survives lengthy cleanup.
                var settings = FindNativeSettingsWindow();
                if (settings != IntPtr.Zero)
                {
                    ShowWindow(settings, ShowWindowHide);
                    _quitWindowHidden = true;
                }
#elif UNITY_STANDALONE_OSX
                HideMacAppWindows();
                _quitWindowHidden = true;
#endif
                Debug.Log("VCR exit: native window hidden=" + _quitWindowHidden +
                          " (" + timer.ElapsedMilliseconds + " ms)", this);
            }
            catch (DllNotFoundException exception)
            {
                Debug.LogWarning("VCR exit: native window hide unavailable: " +
                                 exception.Message, this);
            }
            catch (EntryPointNotFoundException exception)
            {
                Debug.LogWarning("VCR exit: native window hide unavailable: " +
                                 exception.Message, this);
            }
            catch (Exception exception)
            {
                // Visibility optimization must never prevent normal termination.
                Debug.LogWarning("VCR exit: native window hide failed: " +
                                 exception.Message, this);
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int ShowWindowHide = 0;
        private delegate bool EnumerateWindows(IntPtr hwnd, IntPtr param);

        private static IntPtr FindNativeSettingsWindow()
        {
            var pid = GetCurrentProcessId();
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, param) =>
            {
                if (!IsWindowVisible(window))
                    return true;
                GetWindowThreadProcessId(window, out var owner);
                if (owner != pid)
                    return true;
                var className = new StringBuilder(256);
                GetClassNameW(window, className, className.Capacity);
                if (string.Equals(className.ToString(), "VCR.NativeSettings.Window.1",
                    StringComparison.Ordinal))
                {
                    result = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static IntPtr FindUnityMainWindow()
        {
            var pid = GetCurrentProcessId();
            IntPtr matchingWindow = IntPtr.Zero;
            IntPtr fallback = IntPtr.Zero;
            // A separately focused Settings top-level window must never
            // accidentally be selected as the renderer window.
            EnumWindows((window, param) =>
            {
                if (!IsWindowVisible(window))
                    return true;
                GetWindowThreadProcessId(window, out var owner);
                if (owner != pid)
                    return true;
                var buffer = new StringBuilder(256);
                GetClassNameW(window, buffer, buffer.Capacity);
                var type = buffer.ToString();
                if (type == "VCR.NativeSettings.Window.1")
                    return true;
                if (type.IndexOf("UnityWndClass", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchingWindow = window;
                    return false;
                }

                // Fall back only to a matching application title; do not
                // hide arbitrary windows created by other native libraries.
                buffer.Clear();
                GetWindowTextW(window, buffer, buffer.Capacity);
                var title = buffer.ToString();
                if (title.IndexOf("VirtualCharacterRender", StringComparison.OrdinalIgnoreCase) >= 0
                    && title.IndexOf("환경설정", StringComparison.Ordinal) < 0)
                    fallback = window;
                return true;
            }, IntPtr.Zero);
            return matchingWindow != IntPtr.Zero ? matchingWindow : fallback;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(
            EnumerateWindows callback, IntPtr param);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(
            IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(
            IntPtr hwnd, StringBuilder name, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(
            IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsHideAppWindowsForQuit")]
        private static extern void HideMacAppWindows();
#endif

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
