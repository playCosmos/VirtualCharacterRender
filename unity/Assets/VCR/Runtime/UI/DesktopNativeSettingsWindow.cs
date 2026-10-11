using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace VCR.Runtime.UI
{
    /// <summary>
    /// Real desktop settings top-level window. Only platform APIs are used:
    /// Win32 common controls or the project-owned AppKit bridge.
    /// Unity remains the renderer and sole owner of settings state.
    /// </summary>
    internal sealed class DesktopNativeSettingsWindow : IDisposable
    {
        public enum ControlKind { Label = 0, Button = 1, Edit = 2 }

        public static bool Supported
        {
            get
            {
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const uint Overlapped = 0x00CF0000u;
        private const uint ChildVisible = 0x50000000u;
        private const uint ButtonStyle = 0x00000000u;
        private const uint EditStyle = 0x00800080u;
        private const int ShowNormal = 5;
        private const int Hide = 0;
        private const int CloseMessage = 0x10;
        private const int CommandMessage = 0x111;
        private const int RemoveMessage = 1;
        private const int ClassAlreadyExists = 1410;
        private const string ClassName = "VCR.NativeSettings.Window.1";

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message,
            IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint style;
            public WindowProcedure callback;
            public int extraClassBytes, extraWindowBytes;
            public IntPtr instance, icon, cursor, brush;
            [MarshalAs(UnmanagedType.LPWStr)] public string menuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string className;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public Point point;
            public uint privateData;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassW(ref WindowClass spec);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint extended, string className,
            string text, uint style, int x, int y, int width, int height,
            IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(
            IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool UpdateWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetWindowTextW(IntPtr hwnd, string value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder buffer, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLengthW(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool PeekMessageW(
            out NativeMessage msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref NativeMessage msg);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        private static readonly WindowProcedure Procedure = WindowProc;
        private static DesktopNativeSettingsWindow current;
        private readonly Dictionary<int, IntPtr> editHandles = new Dictionary<int, IntPtr>();
        private readonly Dictionary<int, List<IntPtr>> categoryControls =
            new Dictionary<int, List<IntPtr>>();
        private readonly int[] rows = new int[4];
        private readonly Queue<int> events = new Queue<int>();
        private IntPtr hwnd;
        private readonly IntPtr instance;

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (current != null && current.hwnd == hwnd)
            {
                if (msg == CloseMessage)
                {
                    ShowWindow(hwnd, Hide);
                    return IntPtr.Zero;
                }
                if (msg == CommandMessage)
                {
                    var id = unchecked((int)((long)wParam & 0xffff));
                    if (id >= 100)
                    {
                        current.events.Enqueue(id);
                        return IntPtr.Zero;
                    }
                }
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public DesktopNativeSettingsWindow()
        {
            instance = GetModuleHandleW(null);
            var spec = new WindowClass
            {
                callback = Procedure,
                instance = instance,
                className = ClassName,
            };
            if (RegisterClassW(ref spec) == 0 &&
                Marshal.GetLastWin32Error() != ClassAlreadyExists)
            {
                throw new InvalidOperationException("Win32 Settings window class registration failed.");
            }
            current = this;
            hwnd = CreateWindowExW(0, ClassName, "VirtualCharacterRender - 환경설정",
                Overlapped, 160, 120, 940, 720, IntPtr.Zero, IntPtr.Zero,
                instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                current = null;
                throw new InvalidOperationException("Cannot create native Win32 Settings window.");
            }
            var labels = new[] { "일반", "캐릭터 / 모델", "트래킹", "출력" };
            for (var i = 0; i < labels.Length; i++)
            {
                CreateWindowExW(0, "BUTTON", labels[i], ChildVisible | ButtonStyle,
                    20 + i * 220, 18, 200, 36, hwnd, new IntPtr(100 + i),
                    instance, IntPtr.Zero);
            }
        }
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsCreate")]
        private static extern void MacCreate();
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsShow")]
        private static extern void MacShow();
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsIsVisible")]
        private static extern int MacIsVisible();
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsSelectCategory")]
        private static extern void MacSelectCategory(int category);
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsAddControl")]
        private static extern void MacAddControl(int category, int id, int kind,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsClearCategory")]
        private static extern void MacClearCategory(int category);
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsSetText")]
        private static extern void MacSetText(int id, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsGetText")]
        private static extern void MacGetText(int id, StringBuilder value, int capacity);
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsPollAction")]
        private static extern int MacPollAction();
        [DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsDestroy")]
        private static extern void MacDestroy();

        public DesktopNativeSettingsWindow() { MacCreate(); }
#else
        public DesktopNativeSettingsWindow() { }
#endif

        public void Add(int category, int id, ControlKind kind, string label, string value = "")
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (hwnd == IntPtr.Zero || category < 0 || category > 3)
                return;
            if (!categoryControls.TryGetValue(category, out var handles))
                categoryControls[category] = handles = new List<IntPtr>();
            var top = 84 + 48 * rows[category]++;
            if (kind == ControlKind.Edit)
            {
                handles.Add(CreateWindowExW(0, "STATIC", label, ChildVisible,
                    220, top + 7, 230, 26, hwnd, IntPtr.Zero, instance, IntPtr.Zero));
                var input = CreateWindowExW(0, "EDIT", value ?? "", ChildVisible | EditStyle,
                    460, top, 390, 32, hwnd, new IntPtr(id), instance, IntPtr.Zero);
                handles.Add(input);
                editHandles[id] = input;
            }
            else
            {
                handles.Add(CreateWindowExW(0,
                    kind == ControlKind.Button ? "BUTTON" : "STATIC",
                    label ?? "", ChildVisible,
                    220, top, 630, 34, hwnd,
                    kind == ControlKind.Button ? new IntPtr(id) : IntPtr.Zero,
                    instance, IntPtr.Zero));
            }
            if (category != selectedCategory)
                foreach (var handle in handles) ShowWindow(handle, Hide);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacAddControl(category, id, (int)kind, label ?? "", value ?? "");
#endif
        }

        private int selectedCategory;
        public void SelectCategory(int category)
        {
            if (category < 0 || category > 3) return;
            selectedCategory = category;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            foreach (var entry in categoryControls)
                foreach (var handle in entry.Value)
                    if (handle != IntPtr.Zero)
                        ShowWindow(handle, entry.Key == category ? ShowNormal : Hide);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacSelectCategory(category);
#endif
        }

        public void ClearCategory(int category)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (categoryControls.TryGetValue(category, out var handles))
            {
                foreach (var control in handles)
                {
                    foreach (var entry in new List<KeyValuePair<int, IntPtr>>(editHandles))
                        if (entry.Value == control)
                            editHandles.Remove(entry.Key);
                    if (control != IntPtr.Zero && IsWindow(control))
                        DestroyWindow(control);
                }
                handles.Clear();
            }
            rows[category] = 0;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacClearCategory(category);
#endif
        }

        public string GetText(int id)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (!editHandles.TryGetValue(id, out var handle) || handle == IntPtr.Zero)
                return "";
            var size = Math.Min(32767, GetWindowTextLengthW(handle)) + 1;
            var value = new StringBuilder(size);
            GetWindowTextW(handle, value, size);
            return value.ToString();
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            var value = new StringBuilder(32768);
            MacGetText(id, value, value.Capacity);
            return value.ToString();
#else
            return "";
#endif
        }

        public void SetText(int id, string value)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (editHandles.TryGetValue(id, out var handle) && handle != IntPtr.Zero)
                SetWindowTextW(handle, value ?? "");
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacSetText(id, value ?? "");
#endif
        }

        public bool IsVisible
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
                return MacIsVisible() != 0;
#else
                return false;
#endif
            }
        }

        public void Open()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            {
                ShowWindow(hwnd, ShowNormal);
                SetForegroundWindow(hwnd);
                UpdateWindow(hwnd);
            }
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacShow();
#endif
        }

        public int PollAction()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Do not steal Unity messages. Only dispatch messages addressed
            // to our own native top-level window on Unity's main thread.
            if (hwnd != IntPtr.Zero)
            {
                var count = 0;
                while (count++ < 64 &&
                       PeekMessageW(out var msg, hwnd, 0, 0, RemoveMessage))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            return events.Count > 0 ? events.Dequeue() : 0;
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            return MacPollAction();
#else
            return 0;
#endif
        }

        public void Dispose()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (hwnd != IntPtr.Zero && IsWindow(hwnd)) DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
            if (current == this) current = null;
            editHandles.Clear();
            categoryControls.Clear();
            events.Clear();
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacDestroy();
#endif
        }
    }
}
