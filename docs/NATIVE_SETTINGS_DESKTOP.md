# Native desktop Settings — Win32 / AppKit

All non-Editor Windows x64 and Apple Silicon macOS Players use a **separate OS-level Settings window**. The former Unity Canvas `Settings Modal Overlay` remains only for Unity Editor previews, never for desktop Players.

- Windows: `DesktopNativeSettingsWindow.cs` registers a Win32 window class and uses Windows common controls. All labels, inputs and action buttons are owned by the application; user32/kernel32 are operating-system services, not additional dependencies.
- macOS: project-owned `native/macos/VcrSettingsWindow.mm` implements `NSWindow` and AppKit fields/buttons. Compile to arm64 `VcrSettingsWindow.dylib` via `bash tools/build-native-settings-macos.sh` on a Mac with Apple's Xcode toolchain. The generated binary and its importer .meta are consumed by native Player and future release CI. The source repository does not vendor a binary.
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
| Output | Topmost, transparency, click-through, recover | Existing overlay-output controls |

Closing the Settings window only hides the window; it does **not** stop Unity rendering or close the main window. Reopening focuses the existing Settings instance. Unity owns all settings changes, so no additional IPC or second source of truth is required.

## Verification

CI checks macOS arm64 dylib compilation/exported symbols, Unity P0–P13 and both native Player build/startup/shutdown workflows. These automated gates **do not prove user interaction** with GUI/native windows.

On each native desktop, check all four Settings pages; OS titlebar drag; minimize/restore; focus and Alt+Tab/Command+Tab; resize; repeated open/close; a VRM selection; at least one tracker-source setting; click-through/topmost with Settings in front; Retina/Windows DPI; Settings closed/open when exiting the app. Capture real GUI evidence before labeling desktop acceptance PASS.

The first alpha.2 release is immutable and predates this migration; its downloads do not include this Settings change. Generate and verify a later release version after CI and actual desktop acceptance. 
