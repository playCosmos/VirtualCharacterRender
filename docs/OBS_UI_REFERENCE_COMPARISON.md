# Comparative UI research — OBS + avatar renderer workspaces

Date: 2026-10-26
Status: **research and proposed interaction patterns**, not implemented UI.
Parent design: [OBS workspace and startup inventory](OBS_UI_REDESIGN_AND_STARTUP_INVENTORY.md) (PR #39) and [UI rebuild issue #41](https://github.com/playCosmos/VirtualCharacterRender/issues/41).
This document analyzes publicly documented interfaces, not proprietary source/layout assets. Do not copy vendors' artwork, icons, screenshots or distinctive branding into VCR.

## Reference software and sourced UI behavior

| Reference | Observed interaction, per product docs | Reuse in VirtualCharacterRender | Avoid / trade-off |
| --- | --- | --- | --- |
| **OBS Studio** | Dockable Scenes, Sources, audio mixer, Controls; central preview and optional Preview/Program Studio Mode; scene/source visibility and grouping | Main shell, dock/undock/reset; visually separate selected scene/source state from live output state; persistent Status/Log | Do **not** imply full OBS scene mixer, recording or source capture is currently implemented; do not add Studio Mode until dual preview/program transport actually exists |
| **Warudo** | Dedicated editor and runtime windows; editor's Assets tab lists scene assets with per-selection properties; Blueprints tab has graphical nodes, node palette/properties; onboarding assistant | Asset tree + context inspector for character, camera, tracker, light, environment, shaders; advanced graph editor as optional workspace; setup wizard | Avoid making a complex blueprint editor the first-run default; avoid introducing a WebSocket editor/runtime architecture solely for UX; avoid reimplementing an unrestricted multi-character system |
| **VSeeFace** | Avatar choice uses recognizable thumbnail/grid selection; live app offers tracking, expressions, lighting and camera controls | Quick model switcher/gallery with loaded/missing badges; sensible default camera and tracking actions; dedicated per-model presets | Avoid a full-screen multi-step selection gate every launch; do not clutter preview with all adjustments simultaneously |
| **VTube Studio** | Model settings distinguish per-model parameter mappings, hotkeys, expressions and tracking; model switching and background/items are accessible from on-screen menus | Model-local expression/motion/hotkey profiles; quick actions without visiting global Settings; grouped tracking input/output mapping | Product focuses on Live2D, so do not copy 2D-specific parameter editors as a universal 3D/FBX contract |
| **VNyan** | Graph-based event/callback/actions, configurable resource/prop tools; optional Spout2/NDI output and camera render-layer options in product releases | Reserve a separate node/event workspace with search/palette/validation; leave output backend selection extensible; list resource owners and plugin/package status | Do not present VNyan's transport options as VCR capabilities; Spout2/NDI are **future candidates**, not working VCR outputs; avoid node canvas during basic live operation |
| **VMagicMirror** | Separate control/settings windows and rendered avatar view; settings grouped into Window, Face, Motion, Layout, Effects, Devices, Expressions, VMC and hotkeys; transparent-view and free-layout modes have intentionally different controls | Separate standard OS Settings window; operational quick controls distinct from editing tools; explicit preview/output mode indication and camera presets | Avoid switching off live-output transparency without clear UI feedback; don't reproduce Windows-specific controls on macOS without matching semantics |
| **Blender/Unity Editor** (secondary reference only) | Hierarchy/outliner, component inspector, asset browser, editor view and tabs | Property inspector for expert users, searchable hierarchy/assets, consistent selection | Too dense for default VTuber broadcasting; do not expose Unity internals or create a full DCC application as a prerequisite |

### Primary source links / visual references

- [OBS Studio Overview Guide](https://obsproject.com/kb/obs-studio-overview) — scenes, sources, mixer, Studio Mode.
- [Warudo Getting Started](https://docs.warudo.app/docs/tutorials/getting-started) — editor/runtime separation, onboarding, Assets; [Assets UI](https://docs.warudo.app/docs/assets/overview); [Blueprints UI](https://docs.warudo.app/docs/blueprints/overview).
- [VSeeFace official documentation](https://www.vseeface.icu/) — avatars, live expression and tracking controls.
- [VTube Studio official wiki](https://github.com/DenchiSoft/VTubeStudio/wiki) — models, hotkeys, tracker settings; [model settings and hotkeys](https://github.com/DenchiSoft/VTubeStudio/wiki/VTS-Model-Settings).
- [VNyan official page](https://suvidriel.itch.io/vnyan) — graph editor, model/props/tracking, available outputs; [1.6.8 release notes](https://suvidriel.itch.io/vnyan/devlog/1417469/vnyan-168-released) — independent camera/Spout2 output improvements; [VNyan wiki](https://github.com/Suvidriel/VNyanDoc).
- [VMagicMirror Getting Started](https://akiya64.github.io/VMagicMirror/en/get_started/); [settings tab documentation](https://akiya64.github.io/VMagicMirror/en/docs/); [layout mode](https://akiya64.github.io/VMagicMirror/en/docs/layout/).

Screenshots in the linked guides are **references to inspect**, not assets to bundle, emulate pixel-for-pixel, or redistribute.

## Synthesized VCR UX — two tasks, one product

**Mode A — Live / Broadcast (default):** allow a first-time streamer to launch the last real VRM without manually opening an advanced editor.

- Main title/status strip: scene/profile, exact active-model status (loaded/remembered/missing), active tracker with calibration/fault state, frame/renderer status, output readiness (never label OBS-ready without measured evidence).
- Left dock tabs: **Scenes / Models / Sources** (models are not equivalent to scene source *instances*). Model library thumbnails, load/unload/reload, last active marker, file status.
- Central resizable viewport: avatar preview with orbit/reset/camera preset; switch between preview and live-output controls, without suggesting OBS Studio Mode's dual-bus semantics until implemented.
- Right dock: selected **Properties** (model, camera, tracker, light, material/shader) with contextual controls, validation and recovery. No monolithic general-purpose settings wall.
- Bottom dock tabs: **Tracking, Expressions/Hotkeys, Status & Logs**; default show a compact tracker/output summary, with logs a tab instead of an always-expanded console.
- Stream quick controls: calibrate/reset tracking, change model, select expression, enable/disable source, transparency/output recovery, diagnostics export. Avoid default hidden/hover-only affordances.

**Mode B — Edit / Advanced (opt-in workspace preset):**

- Reuse the same runtime/camera/scene, not a second independently running renderer; expand Asset Tree, Inspector, Materials/Shaders, timeline/expression rules and Events/Nodes only when implemented.
- Node/graph panel only after matching event runtime editor capabilities are production-ready. Palette search, node validation, undo/redo, and import/export must be specified before promoting the entry point.
- Explicit **Apply / Save / Revert** semantics; warn when changed model paths, scene dependencies or shader packages are missing; never silently clear the current model on an invalid import.
- Dock layout saved per workspace/profile; **Restore default layout** always available; independent OS Settings opens via normal button/menu and can be moved to another monitor.

**Mode C — Setup / Troubleshoot (task-specific dialogs, not a third permanent app):**

- First run: quick select VRM and camera tracking, output preview; advanced options deferred. Avoid silently prompting for camera access unless enabling that device.
- Model restore: visually separate `last successful saved VRM path`, `actual runtime loaded model`, and `file in library`. No artificial "default model loaded" claim; initialize/log each separately.
- Missing/corrupt model: warn without blocking core rendering or replacing working model; show Browse, Remove remembered entry, Skip.
- Device diagnostics: registered vs enabled vs connected vs data flowing; one Recover/Recalibrate control with cause-specific result.
- Startup log: ordered events grouped by **Core, Renderer, Model, Tracking, Declarative Packages, Shaders, Output**, with severity/time, category filters and privacy-aware export; shows **unsupported** instead of phantom FBX/executable plugins.

## Concrete component and layout contract (proposed U0 baseline)

At 1600x900 desktop size, initial **proposal** (adjust through design review, not hard constraints):

```
┌────────────────────────────────────────────────────────────────────────────────┐
│ Menu / Profile / Workspace     Model: Loaded  Tracker: Ready  Output: Preview │
├─────────────────────┬──────────────────────────────────┬───────────────────────┤
│ Scenes | Models     │        CHARACTER VIEWPORT        │ Properties            │
│ Sources             │                                  │ Model / Camera /      │
│ Model gallery       │   Orbit | Reset | Camera Preset  │ Tracker / Light /     │
│ Loaded/missing      │    Preview / Output State        │ Materials / Shaders   │
├─────────────────────┴──────────────────────────────────┴───────────────────────┤
│ Tracker & Expressions | Controls | Startup Log / Diagnostics | Output Status  │
└────────────────────────────────────────────────────────────────────────────────┘
                    [Independent, ordinary OS Settings window]
```

Panel identity and resizing: tab-group dock/undock/drag/snap, keyboard activation, minimum size, split visibility, persisted layout, Reset Layout. Handle 1280×720 and 200% DPI without clipped fields or overlap; on macOS use native document/window focus and Retina metrics. Rendering and tracking must continue if main control panels are minimized/closed per existing background-first product policy; exiting the process remains an explicit operation.

## Priorities / decisions before implementation

1. **U0 reference review (this doc):** OBS dock foundation, Warudo selected-asset inspector, VSeeFace model gallery, VTube Studio per-model presets, VNyan optional graph, VMagicMirror separate settings.
2. **U0 wireframe:** live vs editing workspace; preview vs actual output labeling; model inventory with real states; Settings independent window and log panel. Review screenshot/wireframe and interaction matrix before bulk replacing native UI.
3. **U1 layout shell:** dock, saved workspace profiles, navigation and keyboard/DPI; rebind existing runtime operations without moving persistence into visual components.
4. **U2 data model:** UI-independent remembered/active model state; preserve current VRM last-successful preference; separate real FBX adapter work before eligibility for restore; resource inventory must not pretend unimplemented imports work.
5. **U3 diagnostics:** startup events in core independent of GUI and log dock. "Plugin" categories differentiate built-in services, registered declarative packages, and unsupported third-party executable plugins.
6. **U4 OBS verification:** evaluate whether OS transparent-window capture is adequate, compare optional backend transports in a separate RFC, measure alpha/FPS/latency; **do not silently adopt Spout/NDI just because a reference product supports it**.

Non-goals for this documentation PR: implementing FBX, runtime shader transpilation, Spout/NDI, browser editor, a built-in streaming encoder, a mandatory additional UI dependency, multiple concurrently loaded VRM characters, or a final visual design. No changes to renderer, CI evidence, packages, tags or releases.

## Acceptance checks informed by references

- Fresh launch with **no last model**, valid remembered VRM, deleted VRM, and explicit unload each has distinct, correct UI state and local startup log.
- Model selected in gallery displays name/thumbnail/format and only enables actions the runtime actually supports.
- Selected tracker has connection, enabled, receiving data and calibration statuses separated; device loss does not freeze GUI.
- Preview can be dragged/resized/reset; output mode and OBS readiness are independently displayed and never inferred from preview alpha alone.
- Preferences per user and per model do not overwrite each other; layout restore/reset, native Settings open/close, keyboard-only operation and OS alt-tab function at 100–200% display scaling.
- Log panel can be hidden without losing log events; starts are ordered, category-filtered and distinguish registry presence from successful load. User-supplied paths are not broadly logged or exported without review.
- Full acceptance requires screenshots on Windows/macOS plus real OBS transparent capture; simulated CI acceptance is not a substitute.
