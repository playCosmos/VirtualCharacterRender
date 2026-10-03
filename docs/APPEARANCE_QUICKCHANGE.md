# Appearance Quick Change

Updated: 2026-10-03

## Purpose

VirtualCharacterRender needs a one-character appearance system that can change clothing and accessories without replacing the active performer/avatar runtime.

This is separate from loading another VRM file. The active character identity, tracking bindings, motion/expression mixer, event bindings, camera/output state, and application session remain intact while appearance changes.

## Product scope

The baseline quick-change path supports:

- named appearance presets
- named outfit variants
- accessory slots
- immediate manual switching from the Character UI
- event-triggered switching through the existing event runtime
- per-character persistence
- deterministic rollback when a requested appearance cannot be applied
- optional choreographed quick-change transitions with motion, effects, audio, camera/environment cues, and an explicit appearance-swap marker
- user-authored transition presets and custom registered motion/effect assets

One active character remains the product scope.

## Baseline runtime model

Appearance state is character-owned and non-destructive.

```text
Active Character
  └─ Appearance Runtime
       ├─ Outfit Variant
       ├─ Accessory Slots
       │    ├─ Head
       │    ├─ Face
       │    ├─ Neck
       │    ├─ Back
       │    ├─ Left Hand
       │    ├─ Right Hand
       │    └─ Custom named slots
       └─ Appearance Preset
            ├─ outfit id
            └─ accessory selections
```

A preset is a named snapshot of outfit and accessory selections. Applying a preset must be atomic from the user's point of view.

## Runtime binding path

P11 supports two binding paths:

1. explicit programmatic/serialized bindings for authored scenes and future P12 tooling
2. convention discovery for dynamically loaded characters when no explicit bindings exist

The convention path is:

```text
<CharacterRoot>
  └─ VCRAppearance
       ├─ Outfits
       │    ├─ casual
       │    ├─ formal
       │    └─ ...
       └─ Accessories
            ├─ head
            │    ├─ hat
            │    └─ crown
            └─ ...
```

Each direct child of `Outfits` becomes a named outfit and baseline preset. Each direct child under `Accessories/<slot>` becomes a selectable accessory for that slot. Explicit authored bindings remain authoritative when present.

## Outfit variants

The first supported outfit path is a registered variant belonging to the active character.

Preferred implementations, in order:

1. activate/deactivate registered outfit roots already compatible with the loaded character
2. load a character-specific appearance package that declares the compatible character/skeleton
3. only later, support external skinned-garment replacement when skeleton/bind-pose compatibility can be proven

The baseline must not assume that an arbitrary skinned mesh can be attached safely to any VRM skeleton.

Changing an outfit must not:

- reload the whole VRM unless explicitly requested
- reset tracking
- reset motion/expression mixer state
- reset environment, events, or broadcast output
- destructively edit the source VRM

## Accessories

Accessories are attached through named slots or explicit humanoid-bone/character anchors.

Each accessory definition contains at minimum:

- stable accessory id
- asset/resource id
- target slot or anchor
- local position
- local rotation
- local scale
- optional visibility/default-enabled state

A slot is single-selection by default. A slot may explicitly allow multiple stacked accessories when needed.

Accessory attach/detach must use the same active character and must not create a second character runtime.

## Quick-change behavior

Default switch behavior remains Immediate, but each appearance change may optionally select a reusable transition preset.

The non-choreographed runtime sequence is:

1. validate requested outfit/accessories
2. prepare required objects/assets inactive where possible
3. verify required anchors and compatibility
4. apply the complete target appearance
5. publish the new appearance state
6. if application fails, restore the previous complete appearance state

No partially applied preset should remain after a failed switch.

## Choreographed transition presets

Appearance data and presentation data are separate.

```text
AppearancePreset
  ├─ outfit id
  └─ accessory selections

AppearanceTransitionPreset
  ├─ transition id
  ├─ duration / completion policy
  ├─ ordered or timed transition steps
  ├─ exactly one appearance.commit point
  └─ failure / interruption policy
```

This allows the same outfit preset to be used with multiple presentations and the same presentation to be reused across multiple outfits.

Example:

```text
transition: spin-confetti
0.00s  motion.play       spin
0.10s  effect.play       confetti
0.55s  appearance.commit
0.55s  effect.play       sparkle-burst
0.90s  motion.release    spin
1.20s  complete
```

The visible result is that the character starts turning in place, confetti obscures or accents the character, the outfit changes at the commit point, and the transition finishes without reloading the character.

### Transition step classes

The planned built-in step classes are:

- `motion.play` / `motion.release`
- `effect.play` / `effect.stop`
- `audio.play` / `audio.stop`
- `expression.set`
- `material.apply_preset` or transition-safe material cue
- `environment.set_state` where explicitly allowed
- `camera.set_fov` or another bounded camera cue
- `wait`
- `appearance.commit`
- custom registered action step

Transition definitions reference logical IDs, not Unity object references.

### Appearance commit marker

Every choreographed transition contains exactly one `appearance.commit` step.

Before the commit:

- the requested appearance is validated and prepared
- the current appearance remains authoritative
- failure or cancellation leaves the old appearance active

At the commit:

- the complete prepared outfit/accessory state replaces the previous appearance atomically
- the new appearance state is published only after successful application

After the commit:

- presentation steps may continue
- a non-critical particle/audio/post-motion failure does not revert a successfully committed appearance
- failures are surfaced through diagnostics and the transition result

This separates appearance integrity from cosmetic presentation failures.

## User-custom motion and effect support

Users may build custom transition presets rather than being limited to fixed built-in effects.

Custom motion support is planned around registered motion assets/presets that feed the established Motion / Expression mixer instead of directly writing character transforms.

Examples include:

- spin in place
- jump/land
- bow
- pose-and-hold
- Unity-project `AnimationClip` baked to a normalized additive cue asset through the P11 cue baker
- externally imported motion files remain a P12 import/tooling concern
- user-authored procedural/additive pose sequence (runtime cue path implemented)

Custom effect support uses registered effect presets and character/world anchors.

Examples include:

- confetti
- flower petals
- smoke
- flash
- sparkles
- magic circle
- custom particle/effect prefab package

User-authored transition steps must use application-level action contracts. Transition files must not persist raw `GameObject`, `Transform`, `ParticleSystem`, `AnimationClip`, or other Unity instance references.

The first Unity Editor timeline/sequence authoring slice is now available in P11. P12 remains responsible for richer packaged authoring/import workflows, marker-based synchronization, and non-Unity end-user tooling.

## Transition timing and synchronization

A transition step may be scheduled by:

- absolute time from transition start
- delay after the previous step
- a named motion marker/event
- completion of a previous blocking step

The appearance commit can therefore be synchronized to the exact frame/marker intended by a custom motion.

The runtime remains deterministic ordered/timed execution. The P11 editor visualizes timed markers and edits their order directly; named markers, blocking-step dependencies, and a full cinematic timeline remain later tooling.

## Concurrent quick-change requests

Only one appearance transition owns the character at a time.

Planned policies:

- `QueueLatest` — default; keep only the newest pending appearance request while the current transition finishes
- `QueueAll` — optional for authored sequences
- `IgnoreWhileBusy`
- `Interrupt` — advanced; allowed only when explicit immediate cancellation cleanup actions are authored

Cancellation cleanup uses ordinary non-`appearance.*` action steps such as `motion.release`, `effect.stop`, and `audio.stop`. Cleanup steps execute immediately in authored order. Interrupting before `appearance.commit` keeps the previous appearance. Interrupting after commit keeps the newly committed appearance and only stops remaining presentation steps.

## Transition fallback policy

Each transition preset chooses one of:

- `Fail` — do not change appearance when a required transition asset/action is unavailable
- `Immediate` — skip the presentation and perform the validated appearance change immediately
- `SkipOptionalSteps` — execute the transition while omitting unavailable non-critical presentation steps

The default for normal user presets should be `Immediate` so a missing cosmetic effect does not prevent a wardrobe change.

## UI plan

P11 Character UI gains an Appearance / Quick Change area with:

- current preset
- previous/next preset
- direct preset selection
- outfit selection
- accessory slot selection
- clear accessory action
- save current selection as a user preset
- restore character default appearance
- transition preset selection, including Immediate
- transition preview/test

The initial UI may use compact controls. Platform-native file browsing for importing external appearance assets remains behind the platform/UI adapter boundary.

## Event integration

The event runtime should target application concepts rather than GameObjects.

Planned actions:

```text
appearance.set_preset
appearance.set_outfit
appearance.set_accessory
appearance.clear_accessory
appearance.restore_default
appearance.cancel_transition
appearance.transition
appearance.set_preset_with_transition
```

This allows hotkeys, OSC/WebSocket events, donations, chat rules, or local automation to trigger the same quick-change runtime used by the UI, including cleanup-gated transition cancellation.

The transition runtime may internally reuse the same application-level action-handler concepts as P9, but it owns sequencing/timing and the `appearance.commit` boundary. It must not recursively emit arbitrary normalized events to drive its own steps.

## Persistence

Appearance presets are stored per character profile, keyed by a stable character identity/path profile rather than globally mutating the VRM.

The P11 implementation currently uses the normalized character path as the profile identity and stores the profile under a SHA-256-derived filename inside the application `appearance-profiles` directory. The source path is retained inside the versioned JSON document for diagnostics/migration, but is not exposed in the profile filename. Save/update/delete uses an atomic temporary-file replacement path, and the UI rolls back the in-memory user-preset registry when persistence fails.

Authored presets and user presets are separate namespaces at runtime. A user preset may replace an existing user preset with the same id, but it may not override an authored preset id.

Persist:

- preset id/name
- outfit id
- accessory slot selections
- user-defined accessory transforms when explicitly allowed
- preferred transition preset id
- user-authored transition definitions or references to them

Do not persist Unity object instance references.

## Compatibility and failure policy

A requested appearance is rejected when:

- required outfit/accessory asset is missing
- required anchor does not exist
- character compatibility metadata does not match
- skinned-mesh skeleton/bind-pose compatibility cannot be established
- package validation fails

Failure keeps the previous appearance active and exposes a user-facing error plus diagnostics.

## Performance policy

Quick change is not a frame-by-frame system.

- inactive presets must not keep update loops running
- unused accessories should be unloaded or pooled according to measured cost
- switching may allocate/load assets, but steady-state rendering cost should only include the active appearance
- no additional tracking inference is introduced

## Phase placement

### P11 — Application UI

Add the Character > Appearance / Quick Change controls and application-facing appearance state/actions.

### P12 — Advanced One-Character Scene Tooling

The first transition timeline editor has been pulled forward into P11. P12 extends it with broader appearance authoring/import tooling:

- register outfit roots
- create/edit accessory slots and anchors
- build appearance presets
- import validated external accessory packages
- optional compatible skinned-outfit package workflow
- preview and validate an appearance preset before making it active
- richer transition timeline authoring: named markers, blocking dependencies, reusable/importable transition packages, and non-scene workflows
- external custom motion import/registration beyond the implemented Unity AnimationClip baker
- custom particle/effect preset import/registration
- named motion-marker placement for appearance.commit synchronization
- transition interruption/fallback policy editing

## Current implementation status

The first source implementation now exists:

- `IAppearanceRuntime`, appearance status/state, preset, transition, and executor contracts
- `BasicCharacterAppearanceRuntime`
- registered outfit/accessory root bindings
- immediate transactional switching and default restore
- timed transition runner with a single commit step
- deterministic authored step order
- QueueLatest / QueueAll / IgnoreWhileBusy
- Interrupt enabled only for transitions with explicit cancellation cleanup actions
- transition status exposes elapsed time, duration, normalized progress, commit state, and cancel availability
- Character UI exposes progress and a Cancel Transition action when cleanup is executable
- generic transition-action bridge to existing application-level event handlers
- P11 Character UI previous/next/transition/default controls plus direct preset/outfit/accessory ID apply/clear controls
- P11 transition preview by replaying the selected non-Immediate transition against the current appearance without changing the requested look
- per-character user preset save/load/delete through a versioned appearance profile store
- automatic user-preset restore when the same character profile is loaded
- authored preset ids protected from user-preset overwrite
- `VCR/P11/Open Appearance Transition Timeline` editor with add/duplicate/delete transition operations
- direct editing of duration, queue/fallback policy, ordered action/commit steps, custom action type/target/name/text/value fields, and cancellation cleanup
- timeline marker visualization, step reorder/delete, sort-by-time, `Validate & Apply`, and Play Mode `Preview Current Appearance`
- versioned JSON export for the selected transition or all transitions
- transactional JSON import with duplicate-ID replacement confirmation and full rollback when runtime validation fails
- built-in `Spin + Confetti` and Interrupt cleanup starter templates
- timeline edits write directly to `BasicCharacterAppearanceRuntime.transitions`, so editor and runtime definitions cannot silently diverge
- appearance Event Runtime action handler

Implemented built-in presentation actions now also include:

- `effect.play`
- `effect.stop`
- logical effect-id registry over a root and/or ParticleSystem set
- optional restart-on-play and deactivate-on-stop behavior
- reuse from both normal Event Runtime rules and appearance transition steps

Implemented built-in audio presentation now also includes:

- `audio.play`
- `audio.stop`
- logical audio-id bindings over an `AudioSource` and optional bound `AudioClip`
- optional restart-on-play and loop behavior
- optional `Value` volume override clamped to 0..1
- reuse from both normal Event Runtime rules and appearance transition steps

Implemented built-in motion presentation now also includes:

- `motion.play`
- `motion.release`
- `ProceduralMotionCueSource` as a P5 Mixer Additive/Procedural pose layer
- `BakedMotionCueSource` for pre-baked `AnimationClip` pose data
- `VCR/P11/Open AnimationClip Cue Baker` editor workflow using a reference humanoid hierarchy, with optional direct registration to a selected `BakedMotionCueSource`
- baked cues store additive root/bone deltas and interpolate them at runtime without sampling `AnimationClip` or `Animator` per frame
- `MotionCueEventActionHandler` routes across procedural and baked runtimes by explicit runtime id or unique cue id
- root position/rotation curves and per-bone position/rotation curves
- idle sources disable their own Update callback
- default `spin` cue: 360-degree root yaw over 0.9 seconds
- user-defined procedural and baked cues through the same logical cue-id contract

Not yet implemented as built-ins:

- external motion-file import into Unity/AnimationClip assets
- richer user-preset management UI such as rename/reorder/duplicate
- named motion markers / blocking-step dependency authoring
- richer transition package management beyond the implemented versioned JSON import/export, such as package libraries/metadata/migration UI
- external appearance package import
- compatible external skinned-garment workflow

A custom `IEventActionHandler` can already provide additional logical transition actions, so user-defined action types have an extension path before the built-in authoring tools arrive. P11 runtime scene generation now places the shared appearance-transition executor, the default effect/audio handlers, procedural and baked motion cue sources, and the appearance event handler; the appearance runtime auto-discovers transition executors when a character is loaded.

## Deferred evidence

The feature has source implementation but is not yet runtime-validated.

Required evidence includes:

- real VRM outfit-root switching
- accessory bone/anchor stability during tracking and full-body motion
- material/shader preservation across switches
- no tracking/mixer reset during quick change
- failed-switch rollback
- appearance.commit timing during motion/effect transitions
- pre-commit cancellation keeps the prior appearance
- post-commit cosmetic failure keeps the committed appearance
- repeated quick-change request queue/interruption behavior
- custom motion and effect asset validation
- memory and frame-time behavior with multiple prepared presets
- Windows/macOS asset import and path handling
