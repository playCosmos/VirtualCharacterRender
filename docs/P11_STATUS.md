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

## Appearance / Quick Change implementation slice

The planned wardrobe feature now has its first runtime implementation rather than documentation only.

Implemented source contracts:

- engine-independent `VCR.Runtime.Appearance` contracts and status/state snapshots
- Unity `BasicCharacterAppearanceRuntime` for one active character
- registered outfit-root switching
- single-selection registered accessory slots
- named appearance presets
- authored previous/next preset order
- default-appearance restore
- atomic same-frame root activation with rollback on apply exception
- Immediate changes
- timed transition definitions with exactly one explicit appearance commit step
- deterministic authored step order; transition step times must be non-decreasing
- QueueLatest, QueueAll, and IgnoreWhileBusy request policies
- Interrupt definitions currently fail closed until explicit transition-cancellation cleanup is implemented
- Immediate fallback when a required presentation executor is unavailable
- application-action transition executor that can reuse exactly one existing `IEventActionHandler`
- recursive `appearance.*` transition actions are rejected
- event actions for preset/outfit/accessory/default changes
- loaded VRM characters receive an appearance runtime component without forcing any appearance configuration
- Character UI exposes Previous Look / Next Look / transition selection / Restore Default, direct preset/outfit/accessory ID controls, and transition preview

Implemented event action types:

```text
appearance.set_preset
appearance.set_outfit
appearance.set_accessory
appearance.clear_accessory
appearance.restore_default
```

`appearance.set_preset` and `appearance.set_outfit` accept an optional transition id through the command Name field. Accessory set/clear currently use the immediate path.

Transition presentation is intentionally handler-driven. A user or later built-in module can register logical actions such as `motion.play` or `effect.play` without the appearance runtime owning Animator, ParticleSystem, material, camera, or environment objects.

The generic transition sequencer/bridge, particle/effect action path, and procedural motion action path are implemented. The default `spin` motion cue plus a registered confetti/flower-petal/sparkle effect can now be referenced directly by a transition definition. `audio.play` and imported AnimationClip conversion remain pending.

Timed coroutine execution, queue behavior, visual commit timing, cancellation cleanup, and real VRM appearance roots still require Unity runtime evidence and are not marked PASS.

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
- transactional registered outfit/accessory switching
- authored appearance preset order
- default appearance restore
- invalid appearance request leaves prior state intact
- missing required transition executor with Immediate fallback
- transition definition requires exactly one appearance commit
- Event Runtime -> appearance preset bridge
- transition action executor -> application action handler bridge
- recursive appearance.* transition action rejection
- built-in effect.play/effect.stop root activation/deactivation through transition actions
- procedural motion cue sampling plus motion.play/motion.release state changes through the transition bridge
- user-defined custom transition action dispatch
- direct appearance UI action gating for preset/outfit/accessory set/clear
- transition preview gating rejects Immediate, busy/faulted states, and missing active outfits

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Next P11 work

- Character: platform-native file-selection adapter on top of the implemented path-input/load/reload/unload boundary
- Character > Appearance / Quick Change: durable per-character user-preset save remains; direct preset/outfit/accessory controls are now implemented
- Appearance transitions: add audio action handler, imported AnimationClip cue conversion/registration, richer preview/test choreography, and transition progress/cancel UX
- define explicit cancellation-cleanup steps before enabling Interrupt policy
- Custom transition authoring/import remains P12; the P11 runtime can already execute authored transition definitions through logical action executors; see `APPEARANCE_QUICKCHANGE.md`
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

Appearance quick change now has a source implementation foundation but is not runtime-validated. The baseline is same-character outfit/accessory switching with optional choreographed transitions and an atomic commit marker. User-authored motion/effect sequences are supported through registered logical action handlers. Arbitrary external skinned garments remain deferred until character/skeleton compatibility can be validated.

## Appearance quick-change implementation update

Appearance quick change now has a source implementation slice. The runtime includes same-character outfit/accessory bindings, named presets, atomic immediate switching, transition presets with exactly one appearance commit point, QueueLatest/QueueAll/IgnoreWhileBusy policies, Immediate/Fail/SkipOptionalSteps fallback policy, event actions, motion/effect action bridging, procedural quick-change motion cues, effect play/stop handlers, direct P11 preset/outfit/accessory controls, and transition preview by replaying the selected transition against the current appearance. Dynamically loaded characters can auto-discover a conventional `VCRAppearance/Outfits` and `VCRAppearance/Accessories/<slot>` hierarchy when no explicit bindings are supplied.

Transition validation now rejects missing/multiple commit points, recursive `appearance.*` steps, decreasing step times, and durations that end before the final step. The source-free validator covers atomic outfit/accessory changes, event-driven changes, custom transition actions, procedural spin cues, effect play/stop, convention discovery, transition timing guards, direct appearance control gating, and transition preview gating.

This is not a runtime/device validation PASS. Unity Editor execution, real VRM hierarchy switching, custom effect visuals, motion/particle synchronization, cancellation cleanup, memory/frame-time behavior, and external user-authored asset import remain evidence/tooling work. Arbitrary external skinned garments are still deferred until character/skeleton compatibility can be validated.
