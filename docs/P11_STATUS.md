# P11 Status

Updated: 2026-10-05

## Active branch

Current integrated source of truth:

```text
develop
```

The old P11 feature/checkpoint branches are historical phase references only. P11 originally starts from the preserved P10 source checkpoint:

```text
checkpoint/p10-source-implementation
83325fffe496c3b8c18bc82852a271e0784e7109
```

P0-P10 runtime/device/platform evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

P11 source scope is checkpointed on `checkpoint/p11-source-implementation`. The application shell and current control surfaces are implemented through existing subsystem contracts, including Character, Tracking, Motion / Expression, Environment, Material / Shader, Events, Camera / Output, Settings, Diagnostics, Appearance / Quick Change, transition authoring/import, external motion import including BVH, desktop file selection, output toggles, capability/render controls, and diagnostics evidence capture. Remaining P11 work is dominated by Unity Editor/standalone/device/OBS evidence rather than another required source feature slice.

This status does **not** mark Unity compilation, Editor execution, standalone behavior, tracker quality, overlay capture, or performance targets PASS in the current environment.

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
- repeated refreshes cache `AppearanceChanged` state snapshots instead of polling the defensive-copy `Current` accessor; current preset/outfit identifiers use the allocation-free appearance status contract
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
- Settings: application start/config path, sorted capability state/error, render scale, target FPS, VSync, and run-in-background state
- Diagnostics: frame average/P95/P99, tracking update/age/presence data, paged subsystem `RuntimeMetric` table, manual stable-interval capture, JSON snapshot evidence, and CSV/console reporting toggles

Detailed editing controls are intentionally added incrementally instead of duplicating subsystem logic inside the UI.

The next control slice is now also implemented:

- Character: manual VRM path boundary plus Load / Reload / Unload actions over `SingleCharacterSceneRuntime`
- Character keeps direct path input and now also exposes a Browse action through `ICharacterFileSelectionAdapter`; Unity Editor, Windows standalone, and macOS standalone adapters are implemented. The selected path is validated as an existing `.vrm` before it is copied into the path field.
- scene-mutating buttons are disabled while a character load is already in progress and while the scene is suspended/shutting down/stopped
- Camera / Output: Apply 720p60 and Apply 1080p60 actions use the existing broadcast-target runtime contract; Transparent / Topmost / Click-through buttons preserve the other overlay flags and apply through `SingleCharacterSceneRuntime.ApplyOverlayOutput`
- Tracking: previous/next source selection, enable/disable, and recovery actions use `ITrackingRuntimeControl`
- Motion / Expression: primary pose-layer weight plus manual expression set/clear/clear-all controls use the mixer/manual-expression contracts
- Environment: state ID, Cut/Fade/Dissolve mode, duration, and apply controls use `IEnvironmentRuntime`
- Material / Shader: slot browse/refresh, shader apply, float override, and clear actions use `MaterialOverrideController`
- Events: rule browse, enable/disable, trace toggle, max-commands setting, and atomic persistent save/reload use `EventRuntimeHost` plus `EventRuntimeConfigurationStore`
- Settings: sorted capability browse/enable/disable and Render Scale / FPS / VSync / Run in Background controls use `CapabilityRegistry` and `SingleCharacterSceneRuntime` render-setting boundaries
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
- QueueLatest, QueueAll, and IgnoreWhileBusy request policies; QueueAll is bounded by a configurable 1..256 backlog limit (default 32) with queue-depth/rejection diagnostics
- Interrupt is accepted only when explicit cancellation cleanup action steps are authored
- transition status exposes elapsed time, duration, normalized progress, commit state, and cancelability
- explicit cancel/interrupt executes cleanup actions immediately; pre-commit cancellation keeps the old appearance and post-commit cancellation keeps the committed appearance
- Immediate fallback when a required presentation executor is unavailable
- application-action transition executor that can reuse exactly one existing `IEventActionHandler`
- recursive `appearance.*` transition actions are rejected
- event actions for preset/outfit/accessory/default changes
- loaded VRM characters receive an appearance runtime component without forcing any appearance configuration
- Character UI exposes Previous Look / Next Look / transition selection / Restore Default, direct preset/outfit/accessory ID controls, transition preview, and per-character user preset save/delete/rename/duplicate/reorder
- user presets are restored automatically from a versioned per-character appearance profile
- per-character appearance profiles use atomic save plus a 16 MiB strict UTF-8 load/save bound so corrupted or oversized files fail before JSON allocation
- authored preset ids cannot be overwritten by user presets
- profile filenames use a SHA-256 key of the normalized character path rather than exposing the full source path
- persistence failure rolls the in-memory user-preset mutation back
- `VCR/P11/Open Appearance Transition Timeline` authors the serialized runtime transition array directly
- transition editor supports add/duplicate/delete, ordered action/commit steps, named marker create/edit/delete, AnimationClip/BakedMotionCue marker import, configurable nearest-marker snapping, Absolute Time or Marker + Offset scheduling, custom action payloads, Blocking + completion timeout, stable Action Step IDs, All/Any dependencies on earlier actions, a resolved-time dependency node/edge graph with missing/forward-reference warnings, cleanup actions, resolved-time sorting, runtime validation, and Play Mode preview
- selected/all transition definitions can be exported as versioned JSON packages and imported transactionally
- transition package schema is v2 for dependency metadata; v1 packages migrate to v2 defaults automatically, while unsupported newer versions fail closed
- `VCR/P11/Open Transition Package Library` indexes `Assets/VCR/TransitionPackages`, supports package/transition search, source/effective version and migration status, invalid-package diagnostics, external JSON add, asset ping/path copy, and pending-package handoff to the Timeline
- package-library handoff reuses the Timeline collision prompt plus transactional `RebuildConfiguration` rollback rather than implementing a second import path
- import confirms ID replacement and rolls the complete transition array back if runtime validation fails
- spin+confetti and Interrupt-cleanup starter templates are provided
- `VCR/P11/Open External Motion Importer` imports Unity-native `.fbx`, `.dae`, and `.anim` motion sources into project assets
- FBX/DAE embedded clips are copied to standalone `.anim` assets before marker editing
- optional versioned `.vcrmarkers.json` sidecars support wildcard/exact clip selectors and seconds/normalized marker time
- imported clips can be routed directly to the Cue Baker or Transition Timeline
- external motion import rolls back newly created assets on parse/marker/import failure

Implemented event action types:

```text
appearance.set_preset
appearance.set_outfit
appearance.set_accessory
appearance.clear_accessory
appearance.restore_default
appearance.cancel_transition
```

`appearance.set_preset` and `appearance.set_outfit` accept an optional transition id through the command Name field. Accessory set/clear currently use the immediate path. `appearance.cancel_transition` executes the same cleanup-gated cancellation path exposed by the Character UI.

Transition presentation is intentionally handler-driven. A user or later built-in module can register logical actions such as `motion.play` or `effect.play` without the appearance runtime owning Animator, ParticleSystem, material, camera, or environment objects. Blocking actions use the optional completion-probe interface; built-in motion/effect/audio handlers provide probes, while custom action handlers can opt in without changing the appearance runtime.

The generic transition sequencer/bridge, particle/effect action path, audio action path, procedural motion path, and baked AnimationClip motion path are implemented. The default `spin` cue, baked AnimationClip cues, registered confetti/flower-petal/sparkle effects, and logical audio cues can all be referenced through the same transition action system. Runtime playback of baked clips does not sample Animator/AnimationClip every frame.

Timed coroutine execution, QueueAll saturation/rejection behavior under real event bursts, visual commit timing, actual cleanup execution, baked-clip behavior on a real VRM, and real VRM appearance roots still require Unity runtime evidence and are not marked PASS.

Transition dependency discovery is hardened for the optional-handler case: missing `IAppearanceTransitionStepExecutor` instances and missing delegated `IEventActionHandler` instances are negative-cached for one monotonic second, while configured/live handlers remain immediately usable. This prevents repeated full-scene `MonoBehaviour` scans when transition presentation capabilities are intentionally absent.

The application UI dependency refresh also reuses one active-`MonoBehaviour` discovery snapshot across the optional character-file selector, appearance runtime, and tracking-presence resolvers. Their standalone/manual resolver paths still perform a live scan when explicitly invoked, while the periodic 2-second dependency pass no longer allocates duplicate scene-wide arrays for the same snapshot.

The periodic dependency pass now resolves its missing concrete services from that same snapshot as well. `ApplicationRuntimeBootstrap`, `SingleCharacterSceneRuntime`, diagnostics, Event Runtime, the motion/expression mixer, manual-expression source, and material controller no longer each issue a separate global `FindFirstObjectByType` scan when absent.

Environment availability/control/summary refresh now uses an `IEnvironmentRuntime` cached by that same bounded dependency pass rather than polling `SingleCharacterSceneRuntime.EnvironmentRuntime` every 0.5 seconds. The cached environment interface is tied to the scene-runtime instance that produced it, so a replaced/destroyed scene cannot leave the UI reading an orphaned environment runtime. An intentionally control-less tracking configuration also negative-caches its expensive `FindObjectsInactive.Include` control scan for five seconds; explicit capability enable/disable forces an immediate rescan so user-driven capability changes do not inherit that delay.

Appearance runtime notifications are now subscriber-isolated: a throwing `AppearanceChanged` or `StatusChanged` observer cannot turn an already committed wardrobe/accessory change into a failed/stuck transition or prevent healthy subscribers from receiving the same notification. Failure counters are exposed through appearance diagnostics metrics.

Renaming the currently selected user preset is atomic at the notification boundary: the runtime stages the replacement current id before registry replacement, restores the old id if replacement fails, and emits one final appearance snapshot without a transient `PresetId=null` state or redundant status notification.

Replacing user-preset definitions also validates the active identity against the actual current outfit/accessory state. If the same preset id is reloaded with different appearance content, the runtime preserves the visible appearance but clears the now-stale current preset id and publishes one invalidation snapshot.

Transition executor capability probes are also isolated. Exceptions from custom `CanExecute` or completion `CanTrackCompletion` implementations now fail closed with explicit errors instead of escaping validation/coroutine execution; probe failures are counted as `appearance.transition.executor_probe_failures`.

The delegated Event Runtime action bridge applies the same rule to `IEventActionHandler.CanHandle` and `IEventActionCompletionProbe.CanTrackCompletion`. Throwing handler probes no longer escape `AppearanceTransitionActionExecutor`; `TryExecute`/`TryIsComplete` return explicit errors and diagnostics expose separate handler/completion probe failure counters.

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
- overlay-setting and runtime-Settings action availability by scene state
- sorted capability status snapshots including enabled/disabled state
- event rule configuration JSON save/load round trip with rule enabled state/order and max commands
- capability status snapshot ordering and enabled/disabled state
- Diagnostics normal UI refresh reads existing snapshots only; explicit Capture Now forces a report only when at least one second has elapsed since the previous report, avoiding distorted tracking-rate evidence
- transactional registered outfit/accessory switching
- authored appearance preset order
- default appearance restore
- invalid appearance request leaves prior state intact
- missing required transition executor with Immediate fallback
- transition definition requires exactly one appearance commit
- Event Runtime -> appearance preset/cancel bridge
- transition action executor -> application action handler bridge
- destroyed Unity transition executors/action handlers are rejected through Unity-object lifetime checks; auto-find paths rebuild against live replacements
- recursive appearance.* transition action rejection
- built-in effect.play/effect.stop root activation/deactivation through transition actions
- procedural motion cue sampling plus motion.play/motion.release state changes through the transition bridge
- AnimationClip bake to additive pose frames, explicit VCR marker-event extraction/preservation, baked cue interpolation, and cue-owner routing across multiple motion runtimes
- user-defined custom transition action dispatch
- direct appearance UI action gating for preset/outfit/accessory set/clear
- transition preview gating rejects Immediate, busy/faulted states, and missing active outfits
- audio.play/audio.stop transition dispatch, logical audio binding, loop and optional volume override
- user preset registry save/replace/collision behavior plus rename/duplicate/reorder semantics
- active user-preset identity preservation across rename and ordered duplicate insertion
- per-character appearance profile JSON save/load round trip including managed preset order and oversized-file rejection
- saved preset UI gating requires a ready runtime, source/target ids as applicable, and active character path
- Interrupt definition rejection without cleanup and acceptance with explicit cleanup actions
- transition cancel UI gating on active/cancelable status
- transition timeline SerializedObject property contract and authored transition RebuildConfiguration round trip
- named marker resolution, AnimationClip marker extraction with non-marker filtering/duplicate rejection, baked marker preservation, unknown-marker rejection, blocking timeout validation, completion-probe bridging, StepId uniqueness, All/Any dependency validation, forward-reference rejection, and dependency timeout validation
- appearance transition queue-limit range validation plus source checks for destroyed executor/handler replacement recovery
- procedural motion completion state before/after release through the transition bridge
- transition package JSON v2 marker/blocking/dependency/action/commit/cleanup round trip, v1→v2 migration, and newer-version rejection
- transition package library add/scan behavior, v1 migration metadata, valid/invalid file indexing, and package/transition search matching
- external standalone `.anim` file copy/import with wildcard + exact sidecar marker resolution
- normalized/seconds sidecar marker conversion into `VCRMarker` AnimationEvents
- duplicate resolved marker rejection, newer-sidecar-version rejection, and failed-import asset/folder rollback
- built-in BVH adapter dispatch through the shared external-motion registry
- BVH hierarchy/channel/frame parsing into an additive humanoid baked cue
- BVH configurable position scale/handedness conversion, first-frame-relative root motion/rotation, common humanoid bone mapping, and sidecar-marker preservation
- adapter source archival as Unity-tracked `.bvh.bytes` plus baked-cue asset creation

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Next P11 work

- Character: native file-selection source path is implemented for Unity Editor, Windows standalone, and macOS standalone; remaining work is real-platform dialog/build verification
- Character > Appearance / Quick Change: saved preset save/load/delete/rename/duplicate/reorder is implemented; later work is richer browsing/search/metadata UX if needed
- Appearance transitions: timeline authoring, Unity AnimationClip/BakedCue marker extraction/import/snapping, named markers, Blocking plus StepId-based All/Any completion dependencies, resolved-time dependency graph preview, versioned JSON v2 import/export with v1 migration, and a searchable project package-library browser are implemented; remaining work is additional non-native motion adapters/marker conversion beyond BVH, interactive graph editing/grouping, richer preview choreography, and real-runtime verification of progress/cancel/Interrupt/dependency behavior
- Motion import: Unity-project AnimationClip baking plus external Unity-native `.fbx`, `.dae`, and `.anim` import are implemented. FBX/DAE embedded clips are extracted to standalone `.anim` assets before marker editing. A built-in `.bvh` adapter parses hierarchy/channels and emits an additive humanoid `BakedMotionCueAsset`; glTF-style motion remains adapter work.
- Custom transition authoring/import remains P12; the P11 runtime can already execute authored transition definitions through logical action executors; see `APPEARANCE_QUICKCHANGE.md`
- Tracking: source enable/status/recovery controls are implemented; remaining work is real tracker-device verification and any justified source-specific UI that does not leak implementation details
- Motion / Expression: pose-layer weight and manual-expression controls are implemented; remaining work is real avatar/mixer verification and richer preset browsing only if needed
- Environment: state and Cut/Fade/Dissolve transition controls are implemented; remaining work is real scene-transition evidence and richer state browsing if justified
- Material / Shader: slot browse/refresh, shader apply, float override, and clear controls are implemented; remaining work is richer property discovery/preset UX without duplicating P2/P7 internals
- Events: persisted rule browse/enable, trace, max-command, save, and reload controls are implemented; full rule filter/condition/action editing remains later tooling rather than hidden mutation in P11
- Camera / Output: output state plus transparent/topmost/click-through controls and 720p60/1080p60 broadcast-target apply actions are implemented; remaining work is real standalone/OBS verification and any justified camera-edit controls beyond the existing summary
- Settings: capability enable/disable and Render Scale / FPS / VSync / Run in Background controls are implemented; remaining work is real capability lifecycle/render verification and any additional settings justified by runtime contracts
- Diagnostics: paged subsystem metric table, guarded manual capture, JSON evidence save, and CSV/console toggles are implemented. Manual capture requires a >=1s measurement interval; optional charts remain future-only unless they prove useful enough to justify extra UI complexity
- destructive-looking actions remain gated by centralized validation/policy and explicit status messages

One active performer remains the product scope. P11 is a view/control layer over established subsystem contracts, not a new runtime orchestration layer.

Appearance quick change now has a source implementation foundation but is not runtime-validated. The baseline is same-character outfit/accessory switching with optional choreographed transitions and an atomic commit marker. User-authored motion/effect sequences are supported through registered logical action handlers. Arbitrary external skinned garments remain deferred until character/skeleton compatibility can be validated.

## Appearance quick-change implementation update

Appearance quick change now has a source implementation slice. The runtime includes same-character outfit/accessory bindings, named authored and user presets, atomic immediate switching, transition presets with exactly one appearance commit point, named marker + offset scheduling, cleanup-backed Blocking and StepId-based All/Any completion dependencies, QueueLatest/QueueAll/IgnoreWhileBusy plus cleanup-gated Interrupt, Immediate/Fail/SkipOptionalSteps fallback policy, event actions, procedural and baked AnimationClip motion cues, effect/audio handlers, direct P11 preset/outfit/accessory controls, transition preview/progress/cancel UI, per-character user-preset persistence, and a Unity Editor transition timeline authoring surface. Dynamically loaded characters can auto-discover a conventional `VCRAppearance/Outfits` and `VCRAppearance/Accessories/<slot>` hierarchy when no explicit bindings are supplied.

Transition validation now rejects missing/multiple commit points, recursive `appearance.*` steps, decreasing step times, durations that end before the final step, and Interrupt definitions without explicit cleanup. The source-free validator covers atomic outfit/accessory changes, authored/user preset separation, user-preset persistence round trips, event-driven changes, custom transition actions, procedural and baked clip cues, effect/audio play/stop, convention discovery, transition timing guards, direct appearance control gating, preview gating, and cancel gating.

This is not a runtime/device validation PASS. Unity Editor execution, real VRM hierarchy switching, custom effect visuals, baked-clip retarget quality, motion/particle/audio synchronization, cancellation cleanup execution, memory/frame-time behavior, and external user-authored asset import remain evidence/tooling work. Arbitrary external skinned garments are still deferred until character/skeleton compatibility can be validated.
