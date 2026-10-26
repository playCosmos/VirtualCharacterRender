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
        private const uint ButtonStyle = 0x0000000Bu; // BS_OWNERDRAW
        private const uint NormalTextStyle = 0u;
        private const uint StaticColor = 0x0138u;
        private const uint EditColor = 0x0133u;
        private const uint DrawItemMessage = 0x002Bu;
        private const uint EraseBackgroundMessage = 0x0014u;
        private const uint SizeMessage = 0x0005u;
        private const uint VScrollMessage = 0x0115u;
        private const uint MouseWheelMessage = 0x020Au;
        private const uint GetMinMaxInfoMessage = 0x0024u;
        private const uint WindowVerticalScroll = 0x00200000u;
        private const uint ScrollRange = 0x0001u | 0x0002u | 0x0004u | 0x0010u;
        private const int ScrollBarVertical = 1;
        private const uint SetFontMessage = 0x0030u;
        private const uint NoBackground = 1u;
        private const uint TextSingleLine = 0x00000020u | 0x00000004u | 0x00008000u;
        private const uint TextLeft = 0x00000000u;
        // Main Unity UI: charcoal surfaces, light text and saturated blue selection.
        private const uint BackgroundColor = 0x00251F1Bu;
        private const uint SidebarColor = 0x00312A24u;
        private const uint ButtonColor = 0x003B302Au;
        private const uint SelectedColor = 0x00AA591Bu;
        private const uint ForegroundColor = 0x00EFE8E2u;
        private const uint MutedColor = 0x00B8ABA0u;
        private const uint EditStyle = 0x00800080u;
        private const int ShowNormal = 5;
        private const int Hide = 0;
        private const int CloseMessage = 0x10;
        private const int CommandMessage = 0x111;
        private const int RemoveMessage = 1;
        private const int ClassAlreadyExists = 1410;
        private const uint SwpNoSize = 0x0001u;
        private const uint SwpNoMove = 0x0002u;
        private const uint SwpNoActivate = 0x0010u;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
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
        private struct Rect
        {
            public int left, top, right, bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ScrollInfo
        {
            public uint cbSize, fMask;
            public int nMin, nMax;
            public uint nPage;
            public int nPos, nTrackPos;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point reserved, maxSize, maxPosition, minTrackSize, maxTrackSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DrawItem
        {
            public uint controlType, controlId, itemId, itemAction, itemState;
            public IntPtr hwndItem, hdc;
            public Rect rect;
            public IntPtr itemData;
        }
        private struct ControlLayout
        {
            public int Left, Top, Width, Height;
            public bool Expand;
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
        [DllImport("user32.dll")] private static extern bool SetWindowPos(
            IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height,
            uint flags);
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
        [DllImport("user32.dll")] private static extern int SetScrollInfo(
            IntPtr hwnd, int scrollBar, ref ScrollInfo info, bool redraw);
        [DllImport("user32.dll")] private static extern bool GetScrollInfo(
            IntPtr hwnd, int scrollBar, ref ScrollInfo info);
        [DllImport("user32.dll")] private static extern bool GetClientRect(
            IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern int FillRect(
            IntPtr hdc, ref Rect rect, IntPtr brush);
        [DllImport("user32.dll")] private static extern bool MoveWindow(
            IntPtr hwnd, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(
            IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int DrawTextW(IntPtr hdc, string text, int length,
            ref Rect rect, uint format);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageW(
            IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(int height, int width, int escapement,
            int orientation, int weight, uint italic, uint underline, uint strikeout,
            uint charset, uint outputPrecision, uint clipPrecision, uint quality,
            uint pitchFamily, string family);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClassW(string name, IntPtr instance);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        private static readonly WindowProcedure Procedure = WindowProc;
        private static DesktopNativeSettingsWindow current;
        private readonly Dictionary<int, IntPtr> editHandles = new Dictionary<int, IntPtr>();
        private readonly Dictionary<int, IntPtr> textHandles = new Dictionary<int, IntPtr>();
        private readonly Dictionary<int, List<IntPtr>> categoryControls =
            new Dictionary<int, List<IntPtr>>();
        private readonly int[] rows = new int[4];
        private readonly int[] scrollOffsets = new int[4];
        private readonly Dictionary<IntPtr, int> controlCategories =
            new Dictionary<IntPtr, int>();
        private readonly Queue<int> events = new Queue<int>();
        private readonly Dictionary<IntPtr, ControlLayout> controlLayouts =
            new Dictionary<IntPtr, ControlLayout>();
        private readonly IntPtr[] navigation = new IntPtr[4];
        private readonly IntPtr baseBrush = CreateSolidBrush(BackgroundColor);
        private readonly IntPtr sidebarBrush = CreateSolidBrush(SidebarColor);
        private readonly IntPtr buttonBrush = CreateSolidBrush(ButtonColor);
        private readonly IntPtr activeBrush = CreateSolidBrush(SelectedColor);
        private readonly IntPtr editBrush = CreateSolidBrush(ButtonColor);
        private IntPtr uiFont;
        private IntPtr hwnd;
        private readonly IntPtr instance;

        // Required for reverse P/Invoke of the native Win32 window procedure in IL2CPP.
        [AOT.MonoPInvokeCallback(typeof(WindowProcedure))]
        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var owner = current;
            if (owner != null && owner.hwnd == hwnd)
            {
                if (msg == CloseMessage)
                {
                    ShowWindow(hwnd, Hide);
                    return IntPtr.Zero;
                }
                if (msg == CommandMessage)
                {
                    var id = unchecked((int)((long)wParam & 0xffff));
                    var notification = unchecked((int)(((long)wParam >> 16) & 0xffff));
                    // BN_CLICKED only: tracker Edit fields have IDs >= 6000,
                    // and their EN_CHANGE must not enqueue spurious actions.
                    if (id >= 100 && notification == 0 && lParam != IntPtr.Zero)
                    {
                        owner.events.Enqueue(id);
                        return IntPtr.Zero;
                    }
                }
                if (msg == SizeMessage)
                {
                    owner.ResizeControls();
                }
                if (msg == GetMinMaxInfoMessage && lParam != IntPtr.Zero)
                {
                    var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    limits.minTrackSize = new Point { x = 800, y = 560 };
                    Marshal.StructureToPtr(limits, lParam, false);
                    return IntPtr.Zero;
                }
                if (msg == VScrollMessage || msg == MouseWheelMessage)
                {
                    owner.ScrollDetail(msg, wParam);
                    return IntPtr.Zero;
                }
                if (msg == EraseBackgroundMessage)
                {
                    if (GetClientRect(hwnd, out var background))
                    {
                        FillRect(wParam, ref background, owner.baseBrush);
                        background.right = Math.Min(background.right, 244);
                        FillRect(wParam, ref background, owner.sidebarBrush);
                        return new IntPtr(1);
                    }
                }
                if (msg == StaticColor || msg == EditColor)
                {
                    SetBkMode(wParam, (int)NoBackground);
                    SetTextColor(wParam, ForegroundColor);
                    if (msg == EditColor)
                    {
                        SetBkColor(wParam, ButtonColor);
                        return owner.editBrush;
                    }
                    SetBkColor(wParam, BackgroundColor);
                    return owner.baseBrush;
                }
                if (msg == DrawItemMessage && lParam != IntPtr.Zero)
                {
                    owner.DrawButton(Marshal.PtrToStructure<DrawItem>(lParam));
                    return new IntPtr(1);
                }
            }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private void DrawButton(DrawItem item)
        {
            var id = (int)item.controlId;
            var nav = id >= 100 && id < 104;
            var active = nav && selectedCategory == id - 100;
            var brush = active ? activeBrush : nav ? sidebarBrush : buttonBrush;
            var rect = item.rect;
            FillRect(item.hdc, ref rect, brush);
            SetBkMode(item.hdc, (int)NoBackground);
            SetTextColor(item.hdc, active ? ForegroundColor : nav ? MutedColor : ForegroundColor);
            var prior = uiFont != IntPtr.Zero ? SelectObject(item.hdc, uiFont) : IntPtr.Zero;
            var text = new StringBuilder(Math.Max(1, GetWindowTextLengthW(item.hwndItem) + 1));
            GetWindowTextW(item.hwndItem, text, text.Capacity);
            rect.left += 14;
            rect.right -= 8;
            DrawTextW(item.hdc, text.ToString(), -1, ref rect, TextSingleLine | TextLeft);
            if (prior != IntPtr.Zero) SelectObject(item.hdc, prior);
        }

        private IntPtr NewControl(string type, string label, uint style, int id,
            int x, int y, int width, int height, bool expandable = false)
        {
            var control = CreateWindowExW(0, type, label ?? "", ChildVisible | style,
                x, y, width, height, hwnd, id == 0 ? IntPtr.Zero : new IntPtr(id),
                instance, IntPtr.Zero);
            if (control == IntPtr.Zero)
                throw new InvalidOperationException("Cannot create Settings control.");
            if (uiFont != IntPtr.Zero)
                SendMessageW(control, SetFontMessage, uiFont, new IntPtr(1));
            controlLayouts[control] = new ControlLayout
            {
                Left = x, Top = y, Width = width, Height = height, Expand = expandable
            };
            return control;
        }

        private void ScrollDetail(uint message, IntPtr value)
        {
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var area))
                return;
            var page = selectedCategory;
            var max = Math.Max(0, rows[page] * 48 + 120 - area.bottom);
            var position = scrollOffsets[page];
            if (message == MouseWheelMessage)
            {
                var delta = unchecked((short)(((long)value >> 16) & 0xffff));
                position -= Math.Sign(delta) * 48;
            }
            else
            {
                var code = (int)((long)value & 0xffff);
                switch (code)
                {
                    case 0: position -= 48; break; // SB_LINEUP
                    case 1: position += 48; break; // SB_LINEDOWN
                    case 2: position -= Math.Max(48, area.bottom - 120); break;
                    case 3: position += Math.Max(48, area.bottom - 120); break;
                    case 4:
                    case 5:
                        var info = new ScrollInfo
                        {
                            cbSize = (uint)Marshal.SizeOf<ScrollInfo>(),
                            fMask = ScrollRange
                        };
                        if (GetScrollInfo(hwnd, ScrollBarVertical, ref info))
                            position = info.nTrackPos;
                        break;
                    default: return;
                }
            }
            scrollOffsets[page] = Math.Max(0, Math.Min(max, position));
            ResizeControls();
        }

        private void ResizeControls()
        {
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var area))
                return;
            var contentWidth = Math.Max(200, area.right - 300);
            var max = Math.Max(0, rows[selectedCategory] * 48 + 120 - area.bottom);
            scrollOffsets[selectedCategory] = Math.Min(scrollOffsets[selectedCategory], max);
            var scroll = new ScrollInfo
            {
                cbSize = (uint)Marshal.SizeOf<ScrollInfo>(),
                fMask = ScrollRange,
                nMin = 0, nMax = rows[selectedCategory] * 48 + 120,
                nPage = (uint)Math.Max(1, area.bottom),
                nPos = scrollOffsets[selectedCategory]
            };
            SetScrollInfo(hwnd, ScrollBarVertical, ref scroll, true);
            foreach (var entry in controlLayouts)
            {
                var layout = entry.Value;
                if (IsWindow(entry.Key))
                    MoveWindow(entry.Key, layout.Left,
                        layout.Top - (controlCategories.TryGetValue(entry.Key, out var category)
                            && category == selectedCategory ? scrollOffsets[category] : 0),
                        layout.Expand ? Math.Max(120, contentWidth - (layout.Left - 270)) : layout.Width,
                        layout.Height, true);
            }
        }

        public DesktopNativeSettingsWindow()
        {
            instance = GetModuleHandleW(null);
            var spec = new WindowClass
            {
                callback = Procedure,
                instance = instance,
                className = ClassName,
                brush = baseBrush,
            };
            if (RegisterClassW(ref spec) == 0 &&
                Marshal.GetLastWin32Error() != ClassAlreadyExists)
            {
                throw new InvalidOperationException("Win32 Settings window class registration failed.");
            }
            current = this;
            hwnd = CreateWindowExW(0, ClassName, "VirtualCharacterRender - 환경설정",
                Overlapped | WindowVerticalScroll, 160, 120, 940, 720, IntPtr.Zero, IntPtr.Zero,
                instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                current = null;
                throw new InvalidOperationException("Cannot create native Win32 Settings window.");
            }
            uiFont = CreateFontW(-18, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
            try
            {
                var dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            var labels = new[] { "일반", "캐릭터 / 모델", "트래킹", "출력" };
            for (var i = 0; i < labels.Length; i++)
            {
                navigation[i] = NewControl("BUTTON", labels[i], ButtonStyle,
                    100 + i, 18, 84 + i * 49, 208, 40);
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
            var top = 82 + 48 * rows[category]++;
            if (kind == ControlKind.Edit)
            {
                handles.Add(NewControl("STATIC", label, NormalTextStyle,
                    0, 270, top + 6, 206, 26));
                var input = NewControl("EDIT", value ?? "", EditStyle, id,
                    490, top, 380, 32, expandable: true);
                handles.Add(input);
                editHandles[id] = input;
            }
            else
            {
                var textHandle = NewControl(
                    kind == ControlKind.Button ? "BUTTON" : "STATIC",
                    label ?? "", kind == ControlKind.Button ? ButtonStyle : NormalTextStyle,
                    id,
                    270, top, 600, 36, expandable: true);
                handles.Add(textHandle);
                if (kind == ControlKind.Label && id != 0)
                    textHandles[id] = textHandle;
            }
            foreach (var control in handles)
                controlCategories[control] = category;
            ResizeControls();
            if (category != selectedCategory)
                foreach (var handle in handles) ShowWindow(handle, Hide);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacAddControl(category, id, (int)kind, label ?? "", value ?? "");
#endif
        }

        private int selectedCategory;
        public int SelectedCategory => selectedCategory;
        public void SelectCategory(int category)
        {
            if (category < 0 || category > 3) return;
            selectedCategory = category;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            foreach (var handle in navigation)
                if (handle != IntPtr.Zero) InvalidateRect(handle, IntPtr.Zero, true);
            ResizeControls();
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
                    foreach (var entry in new List<KeyValuePair<int, IntPtr>>(textHandles))
                        if (entry.Value == control)
                            textHandles.Remove(entry.Key);
                    if (control != IntPtr.Zero && IsWindow(control))
                        DestroyWindow(control);
                    controlLayouts.Remove(control);
                    controlCategories.Remove(control);
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
            if ((editHandles.TryGetValue(id, out var handle) ||
                 textHandles.TryGetValue(id, out handle)) && handle != IntPtr.Zero)
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
                // The rendering overlay can be topmost; the Settings window must
                // remain above it while open so native inputs receive real clicks.
                SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                SetForegroundWindow(hwnd);
                UpdateWindow(hwnd);
            }
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacShow();
#endif
        }

        public void PumpMessages()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (hwnd == IntPtr.Zero)
                return;

            // Pump the top-level window and child HWNDs without stealing
            // Unity's own thread messages. Tabs are separate from page controls.
            PumpWindowMessages(hwnd);
            foreach (var tab in navigation)
            {
                if (tab != IntPtr.Zero && IsWindow(tab))
                    PumpWindowMessages(tab);
            }
            foreach (var entry in categoryControls)
            {
                foreach (var control in entry.Value)
                {
                    if (control != IntPtr.Zero && IsWindow(control))
                        PumpWindowMessages(control);
                }
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static void PumpWindowMessages(IntPtr target)
        {
            var count = 0;
            while (count++ < 64 &&
                   PeekMessageW(out var msg, target, 0, 0, RemoveMessage))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
#endif

        public int PollAction()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
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
            textHandles.Clear();
            categoryControls.Clear();
            controlLayouts.Clear();
            controlCategories.Clear();
            events.Clear();
            UnregisterClassW(ClassName, instance);
            if (uiFont != IntPtr.Zero) DeleteObject(uiFont);
            DeleteObject(editBrush);
            DeleteObject(activeBrush);
            DeleteObject(buttonBrush);
            DeleteObject(sidebarBrush);
            DeleteObject(baseBrush);
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
            MacDestroy();
#endif
        }
    }
}
