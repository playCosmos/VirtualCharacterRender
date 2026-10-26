# Native desktop Settings — Win32 / AppKit

All non-Editor Windows x64 and Apple Silicon macOS Players use a **separate OS-level Settings window**. The former Unity Canvas `Settings Modal Overlay` remains only for Unity Editor previews, never for desktop Players.

- Windows: `DesktopNativeSettingsWindow.cs` registers a Win32 window class and uses native controls with project-owned dark GDI palette, system caption, a vertical sidebar, responsive detail pane and per-page scrolling. All labels, inputs and actions are owned by the application; Win32/DWM/GDI are operating-system services, not added dependencies.
- macOS: project-owned `native/macos/VcrSettingsWindow.mm` implements an AppKit `NSWindow` with a vertical sidebar, scrollable detail pages and matching dark application palette. Compile to arm64 `VcrSettingsWindow.dylib` via `bash tools/build-native-settings-macos.sh` on a Mac with Apple's Xcode toolchain. The generated binary and its importer .meta are consumed by native Player CI. The source repository does not vendor a binary.
- No separate Unity process, external GUI framework, browser engine or runtime installer.

## Feature mapping

| Section | Control | Existing runtime operation |
| --- | --- | --- |
| General | Render scale/FPS | `CaptureRenderSettings` / `ApplySettingsRender` |
| General | VSync/background | `ToggleSettingsVsync` / `ToggleSettingsRunInBackground` |
| General | Save/reload configuration | `ApplicationRuntimeBootstrap` |
| Character | VRM path, select/load/reload/unload | Existing character file adapter and load commands |
| Tracking | Refresh source list, open per-source setting form | `ITrackingRuntimeConfigurable.GetConfigurationSettings` |
| Tracking | Apply per-source fields | `TryApplyConfiguration`, then existing PlayerPrefs storage |
| Output | Transparency, click-through, recover | Existing overlay-output controls |

The renderer is always a regular non-topmost OS window. Any legacy Topmost preference/configuration is ignored, and there is no Always on Top command in the native Settings, dashboard or Editor Settings. The Settings window is an independent OS window with standard caption, focus, minimize and resize. On Windows it is raised above a potentially topmost render overlay while visible; this is not a user-configurable Always on Top feature. It is intentionally not an owner/child floating tool window.\n\nClosing the Settings window only hides the window; it does **not** stop Unity rendering or close the main window. Reopening focuses the existing Settings instance. Unity owns all settings changes, so no additional IPC or second source of truth is required.

## Verification

CI checks macOS arm64 dylib compilation/exported symbols, Unity P0–P13 and both native Player build/startup/shutdown workflows. These automated gates **do not prove user interaction** with GUI/native windows.

On each native desktop, check all four Settings pages; OS titlebar drag; minimize/restore; focus and Alt+Tab/Command+Tab; resize; repeated open/close; a VRM selection; at least one tracker-source setting; normal z-order with Settings and another application in front (including existing legacy Topmost preferences), and transparency/click-through; Retina/Windows DPI; Settings closed/open when exiting the app. Capture real GUI evidence before labeling desktop acceptance PASS.

Published alpha releases are immutable. This development change must remain on a feature branch/PR until separately approved; **do not create any additional version tag, release intent or GitHub Release without an explicit new instruction.**

## Current Windows IL2CPP input validation boundary

The Windows implementation must pump native child-control messages on Unity's main thread. Pumping only the top-level HWND leaves edit and button keyboard/mouse messages undispatched even though the controls paint and can be inspected. `DesktopNativeSettingsWindow.PumpMessages()` therefore pumps the top-level window, the four category tabs, and every category control; `PollAction()` only drains the queued commands. The category tabs are tracked separately so selecting a category does not hide them. The window procedure also carries `[AOT.MonoPInvokeCallback(typeof(WindowProcedure))]` for IL2CPP and uses `SetWindowPos(HWND_TOPMOST)` while visible so the Settings window stays above the render overlay.

A Windows Mono `0.1.0-alpha.8` diagnostic verified actual input with the private Sendagaya VRM: clicking native edit 12 and sending real Ctrl+A/Ctrl+V changed the edit, a real button 300 click loaded Sendagaya, button 302 reloaded it, output buttons 400-403 applied and recovered settings, and button 205 saved configuration. The native window was topmost and the main-window screenshot showed Sendagaya after load. The Windows IL2CPP `0.1.0-alpha.8` candidate then repeated the same actual path with no fallback: real category click, real keyboard edit, button 300 load (generation 1->2), button 302 reload (generation 2->3), output 400-403 with settings-preserving 403 (`apply_attempts` 5->6), config save 205 (1,264 bytes), and a real 301 file dialog. The `#32770` dialog was topmost and foreground above the overlay; cancelling it returned to a visible Settings window and the app exited 0. A separate actual-input run populated the dialog filename field with Sendagaya and clicked the real Open button; the dialog closed, real button 300 loaded Sendagaya (generation 2->3 after a prior Alicia load), the registry changed from Alicia to Sendagaya, and the rendered screenshot showed Sendagaya. The harness set the standard filename edit via `WM_SETTEXT` because a clipboard-paste attempt did not focus it; the Open click and application load were real. Cross-process `GetWindowText` can return a cached title, so it is not reliable edit-text evidence; use the application's load/telemetry result, registry, and screenshot. OBS, DPI/resize, and macOS remain separate acceptance work.

`Recover Output` (403) is intentionally settings-preserving. It restarts the overlay adapter with the current transparent/topmost/click-through settings; it does not reset them. To return to a visible clickable overlay after testing click-through, use the transparent/topmost/click-through controls or the explicit restore sequence. This contract is also covered by `P10BroadcastOutputValidation`.


