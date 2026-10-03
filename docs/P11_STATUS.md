# P11 Status

Updated: 2026-10-03

## Active branch

```text
feature/p11-application-ui
```

P11 starts from the preserved P10 source checkpoint:

```text
checkpoint/p10-source-implementation
83325fffe496c3b8c18bc82852a271e0784e7109
```

P0-P10 runtime/device/platform evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## First P11 source slice

P11 introduces a dedicated `VCR.Runtime.UI` assembly rather than moving UI concerns into scene, tracking, materials, events, or diagnostics.

The application navigation model exposes the roadmap sections in stable order:

```text
Character
Tracking
Motion / Expression
Environment
Material / Shader
Events
Camera / Output
Settings
Diagnostics
```

`ApplicationUiModel` owns section selection and availability only. If the selected section becomes unavailable, it deterministically moves to the first available section. Unavailable sections preserve an explicit reason rather than silently disappearing.

`ApplicationUiController` is a first runtime shell built programmatically with uGUI:

- no prefab/UXML dependency for the initial shell
- one screen-space overlay canvas
- fixed navigation section order
- unavailable sections disabled with a visible reason
- low-rate 0.5-second refresh by default rather than expensive frame-by-frame data reconstruction
- scene-status and diagnostics events also trigger refresh
- application configuration save action
- settings-preserving overlay recovery action
- no direct mutation of tracking/material/event internals outside their existing public contracts

The first bound summaries are:

- Character: scene state, model loaded/path, runtime error
- Tracking: subject/source availability and presence events
- Motion / Expression: mixer availability
- Environment: environment/state/space/active/error
- Material / Shader: slot/error counts
- Events: processed/matched/executed/failed/unhandled/ambiguous counts
- Camera / Output: output state, transparency/topmost/click-through, capture readiness, 720p60/1080p60 configuration readiness
- Settings: application start/config path and capability counts
- Diagnostics: frame average/P95/P99 and tracking update rates

Detailed editing controls are intentionally added incrementally instead of duplicating subsystem logic inside the UI.

The next control slice is now also implemented:

- Character: manual VRM path boundary plus Load / Reload / Unload actions over `SingleCharacterSceneRuntime`
- Character path input is not a platform-native file picker; desktop file browsing remains a platform/UI adapter concern
- scene-mutating buttons are disabled while a character load is already in progress and while the scene is suspended/shutting down/stopped
- Camera / Output: Apply 720p60 and Apply 1080p60 actions use the existing broadcast-target runtime contract
- Save Configuration and Recover Output remain global actions
- action availability rules are centralized in `ApplicationUiActionPolicy` rather than duplicated across button callbacks

## Runtime scene

Interactive scene generation:

```text
VCR > P11 > Create Application UI Runtime Scene
```

Output:

```text
Assets/VCR/P11/P11Runtime.unity
```

The builder reuses the established runtime-scene builder and adds `ApplicationUiController` to the application bootstrap object. It does not fork the renderer/tracking/output bootstrap logic.

## Source-free validation

Interactive:

```text
VCR > P11 > Validate Application UI
```

Batch:

```text
tools/validate-p11-source-free.ps1
tools/validate-p11-source-free.sh
```

The P11 batch entry runs P0-P10 source-free suites first and then checks:

- exactly nine roadmap UI sections
- stable Character -> Diagnostics order
- default Character selection
- selected-section fallback after capability loss
- navigation rejection for unavailable sections
- navigation to available sections
- stable user-facing section titles
- unavailable-reason preservation
- blank character-path rejection
- character load availability in operational scene states
- reload/unload gating on an active character
- scene-mutating action suppression while loading/suspended
- broadcast-target action availability by scene state

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Next P11 work

- Character: platform-native file-selection adapter on top of the implemented path-input/load/reload/unload boundary
- Character > Appearance / Quick Change: implement the planned appearance runtime and UI for named outfit variants, accessory slots, named presets, previous/next/direct preset switching, restore-default, and user preset save; see `APPEARANCE_QUICKCHANGE.md`
- Tracking: source enable/status/recovery controls without exposing tracker implementation details
- Motion / Expression: pose-layer weight and manual-expression controls
- Environment: state and transition controls
- Material / Shader: slot/preset/shader/parameter controls over existing P2/P7 contracts
- Events: persisted P9 rule document list/edit/enable controls
- Camera / Output: camera state plus transparent/topmost/click-through controls; 720p60/1080p60 broadcast-target apply actions are implemented
- Settings: capability enable/disable and graphics/runtime settings
- Diagnostics: richer metric tables and optional charts without increasing core diagnostics cadence
- define save/apply UX and validation messaging before enabling destructive-looking actions

One active performer remains the product scope. P11 is a view/control layer over established subsystem contracts, not a new runtime orchestration layer.

Appearance quick change is now explicitly in plan but is not yet implemented or validated. The baseline is same-character outfit/accessory switching; arbitrary external skinned garments are deferred until character/skeleton compatibility can be validated.
