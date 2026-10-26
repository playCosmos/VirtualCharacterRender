# U0 — OBS-style workspace functional specification

Status: implementation specification, not completed UI. Parent: #41. Baseline: PR #39 startup inventory, PR #42 comparison.

## 1. Main workspace

```text
┌ File  View  Profile  Scene  Tools  Help ─────────────────────────────────────┐
│ Profile [Default]   Scene [Main]  Model [name/status]  Tracking [status]    │
├───────────────┬────────────────────────────────────┬──────────────────────┤
│ Scenes        │ Character preview                   │ Inspector            │
│ - Main        │ (actual runtime viewport)           │ Selected source      │
│ - ...         │                                     │ Properties/actions   │
│               │                                     │                      │
├───────────────┤                                     ├──────────────────────┤
│ Sources       │                                     │ Materials/Shaders    │
│ - Model       │                                     │ (optional dock)      │
│ - Trackers    │                                     │                      │
│ - Overlays    │                                     │                      │
├───────────────┴────────────────────────────────────┴──────────────────────┤
│ Tracking / Actions   |   Log & Events   |   Output status                  │
└───────────────────────────────────────────────────────────────────────────┘
```

Default Live workspace: left Scenes and Sources; center preview; right Inspector; bottom Tracking/Actions and Log. Advanced workspace additionally exposes shaders/materials, capabilities and optional future event editor. Never present an unsupported feature as functional.

## 2. Main commands and state

- File: Open model (supported VRM), Unload model, Open model library, Exit. FBX selection must report unsupported until a runtime importer exists.
- View: show/hide docks, switch Live/Advanced workspace, reset current layout, open independent Settings.
- Profile/Scene: select existing runtime-bound entities only; do not invent project persistence.
- Preview: zoom, frame character, transparency preview toggle if backed by runtime, and current output status. Do not imply a separate program/capture transport.
- Sources: list actual model/tracking/overlay providers; source selection drives Inspector. Distinguish available, remembered, loading, loaded, restored, failed and unsupported.
- Tracking: actual device connectivity, enable/disable only when supported, recoverable error detail.
- Log: chronological runtime-owned entries with severity/category/time, search/filter, bounded in-memory viewport; local Player.log remains the underlying persistent source.

## 3. Settings (independent OS window)

Native Win32/AppKit title bar, draggable/resizable, ordinary Alt+Tab/Command+Tab behavior; not a Unity overlay. Sidebar pages:
1. General — startup behavior, language/theme where implemented, update of remembered-model policy.
2. Output & Window — output mode, transparency, size/framerate options only where supported.
3. Camera & Scene — scene camera, lighting, background/environment.
4. Models — user-selected library, last-successful VRM restore, missing-path recovery, supported formats.
5. Tracking & Devices — available providers, device status and diagnostics.
6. Motion & Expressions — current model-compatible controls.
7. Rendering & Shaders — registered shaders vs loaded packages vs active material binding.
8. Extensions & Modules — capability metadata and status; executable third-party plugins explicitly unsupported.
9. Diagnostics — logging levels, startup inventory, privacy warning for exports.

Each page reads/writes existing runtime/configuration APIs. Disabled controls explain why; no fake settings. No hover auto-hide or configurable Always on top.

## 4. Dock/layout contract

- Panel identity is stable, independent of translated titles; layout records schema version, workspace/profile key, panel IDs, split orientation/ratios, tab order, visibility and floating bounds.
- Save atomically with recoverable backup; reject invalid/corrupt layout without destroying previous valid data. Reset only layout, never model or device preferences.
- Clamp restored floating bounds to connected monitor work areas; handle unplugged monitors, DPI/Retina changes and minimized windows.
- Drag/drop affordances, keyboard focus and escape/cancel, minimum panel sizes and sensible snap targets. No new heavyweight UI framework by default.
- Closing/hiding/minimizing Settings or workspace must not terminate the renderer or tracking services.

## 5. Session and model ownership (future U2)

- Runtime/application session manager owns active model and last successful VRM path; UI only issues commands and observes state.
- Migrate `VCR.Character.LastSuccessfulPath.v1` without changing its semantics. Manual successful load updates; explicit unload clears. CLI `--vcr-vrm` takes precedence.
- Failed/missing/corrupt model produces actionable state without blocking startup or recording a false success.
- Opt-in bounded library only; never scan entire disks. FBX stays unsupported until actual load/unload adapter validation.

## 6. Delivery and acceptance gates

- **U0:** Review these panel/page/command contracts and platform behavior; record user approval before wholesale replacement.
- **U1:** Implement native shell and Settings with real existing bindings; verify native title bars, dock persistence, DPI/focus and independent rendering lifecycle.
- **U2:** Move session persistence out of UI; implement bounded library and migration/rollback tests.
- **U3:** Runtime-owned log dock and real module/model events, no per-frame logging.
- **U4:** Windows IL2CPP/macOS interactive acceptance, OBS transparency at 720p60 and 1080p60, physical device recovery, four-hour soak.

For each implementation package: focused branch and PR, CI plus native build checks, no tag/release/develop changes. Headless CI is not desktop UI acceptance.
