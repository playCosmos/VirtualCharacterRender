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

The renderer is always a regular non-topmost OS window. Any legacy Topmost preference/configuration is ignored, and there is no Always on Top command in the native Settings, dashboard or Editor Settings. The Settings window is also a normal, independent non-topmost OS window with standard caption, focus, minimize and resize. It is intentionally not an owner/child floating tool window.\n\nClosing the Settings window only hides the window; it does **not** stop Unity rendering or close the main window. Reopening focuses the existing Settings instance. Unity owns all settings changes, so no additional IPC or second source of truth is required.

## Verification

CI checks macOS arm64 dylib compilation/exported symbols, Unity P0–P13 and both native Player build/startup/shutdown workflows. These automated gates **do not prove user interaction** with GUI/native windows.

On each native desktop, check all four Settings pages; OS titlebar drag; minimize/restore; focus and Alt+Tab/Command+Tab; resize; repeated open/close; a VRM selection; at least one tracker-source setting; normal z-order with Settings and another application in front (including existing legacy Topmost preferences), and transparency/click-through; Retina/Windows DPI; Settings closed/open when exiting the app. Capture real GUI evidence before labeling desktop acceptance PASS.

Published alpha releases are immutable. This development change must remain on a feature branch/PR until separately approved; **do not create any additional version tag, release intent or GitHub Release without an explicit new instruction.**
