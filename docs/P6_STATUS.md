# P6 Status

Updated: 2026-10-05

## Active branch

Current integrated source of truth:

```text
develop
```

The old P6 feature/checkpoint branch is a historical phase reference. P6 originally starts from the preserved P5 source checkpoint:

```text
checkpoint/p5-source-implementation
50c71f07c7170bd7c5fef4f4fd134e4f0cdff829
```

P0-P5 real-device/runtime evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## Source implementation checkpoint

The P6 source implementation now covers the planned one-character environment runtime boundaries.

### Environment state and update policy

- `EnvironmentUpdatePolicy`: Static / EventDriven / Hz10 / Hz30 / EveryFrame
- `EnvironmentUpdateScheduler`: drop-only recurring scheduling without catch-up bursts
- state-root bindings with duplicate/unsafe-root rejection
- atomic state switching without scene reload
- explicit StateChanged / Scheduled / Manual update dispatch
- lazy `EnvironmentUpdateDriver` creation only for recurring policies
- update-target registration/de-duplication/failure isolation
- cached Unity interface targets use Unity-object lifetime checks, so destroyed update/space/transition/lighting targets are never invoked through stale interface references

Static and EventDriven environments still do not add an `Update()` loop to `BasicEnvironmentRuntime`.

### Coordinate spaces

- World / Camera / Screen / Character environment spaces
- explicit `IEnvironmentSpaceTarget`
- Unity `EnvironmentSpaceAnchor`
- validation before reparenting
- space-target validation/apply exceptions are converted to explicit failure results rather than escaping the runtime
- failed space changes preserve the active mode and best-effort roll already-applied targets back to the previous mode

### State transitions

Engine-neutral transition contracts now include:

- Cut
- Fade
- Crossfade
- Dissolve capability identifier
- transition specification/status/context
- explicit `IEnvironmentTransitionTarget`

`BasicEnvironmentRuntime`:

- performs Cut immediately
- requires at least one live explicit transition target for non-Cut modes
- keeps previous and next state roots active while a non-Cut transition is running
- creates/enables `EnvironmentTransitionDriver` only while a transition is active
- completes to one active state root and disables the driver
- completes an active transition before transition-target replacement
- reports transition count/progress/failure/cost diagnostics

Unity `CanvasGroupEnvironmentTransitionTarget` provides lightweight Fade/Crossfade for screen/UI environments. It intentionally rejects Dissolve; shader-driven dissolve remains an explicit optional target/capability instead of being silently approximated.

### Lightweight 2D environments

`Environment2DLayerTarget` supports preconfigured:

- StaticImage
- Video
- Parallax

The component has no recurring `Update()` method. Static image and video lifecycle do not require environment scheduler ticks. Parallax moves only when an environment update is dispatched, so Hz10/Hz30/EveryFrame policy owns that cost.

Actual media decoding/loading remains Unity/asset responsibility rather than being duplicated inside the environment state runtime.

### Lighting influence

Environment lighting is exposed through:

- `EnvironmentLightingProfile`
- `IEnvironmentLightingTarget`
- Unity `EnvironmentLightInfluenceTarget`

The Unity target captures source Light color/intensity, applies weighted environment color and intensity multiplier, and restores the source Light when the target is removed/disabled/destroyed. It does not rewrite character materials.

### Diagnostics

Environment diagnostics now expose:

- state/update/space target counts and modes
- lighting target count/profile weight/failures
- transition target count/mode/progress/failures
- update dispatch last/total/average milliseconds
- transition dispatch last/total/average milliseconds
- recurring-driver state

## Source-free validation

Interactive:

```text
VCR > P6 > Validate Environment Runtime
```

Batch:

```text
tools/validate-p6-source-free.ps1
tools/validate-p6-source-free.sh
```

The P6 suite covers:

- Static/EventDriven no-recurring behavior
- Hz10 interval and drop-only stall behavior
- EveryFrame due behavior
- state-root application, atomic switching, duplicate/unsafe binding rejection
- manual/scheduled/state-change update dispatch
- update-target de-duplication/register/unregister/failure isolation
- World/Camera/Screen/Character anchors
- space-target apply-exception fail-closed rollback
- non-Cut transition rejection without a target
- Crossfade start/midpoint/completion root semantics
- CanvasGroup Cut/Fade/Crossfade alpha behavior and Dissolve rejection
- transition-target de-duplication and diagnostics
- parallax update math and no-self-Update contract
- static-image invalid configuration guard
- VideoPlayer-backed video-layer configuration
- weighted environment Light influence
- invalid lighting-target atomic rejection and source-Light restoration
- update/transition dispatch cost metrics
- destroyed cached target handling: update/space/lighting paths skip dead Unity objects and non-Cut transitions fail closed when no live transition target remains

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Deferred P6 evidence

- execute the P0-P6 Unity source-free batch suites
- validate image/video environments with real assets and video decode on Windows/macOS
- validate Fade/Crossfade visual quality in actual overlay/scene configurations
- implement/measure a shader-specific Dissolve target only when an environment package needs it
- validate lighting influence on real VRM + MToon scenes
- measure Hz10/Hz30/EveryFrame parallax and transition cost on target hardware
- tune defaults only from measured evidence

The source architecture is complete enough for a checkpoint, but these evidence items are not marked PASS.

`StateChanged` notification is subscriber-isolated. A throwing UI/plugin observer cannot interrupt the already-committed environment state change, update dispatch, or transition setup; failures are counted as `environment.state_subscriber_failures`.

Configured `EnvironmentStateBinding` inputs are deep-cloned at the runtime boundary. Later caller mutation of the original binding objects cannot rewrite the live environment configuration, and staged binding application must succeed before the new binding set is committed. Immediate state switches and non-Cut transition root preparation now snapshot every bound root's `activeSelf` value before mutation and restore that exact snapshot if a later `SetActive` fails; rollback failures remain explicit in the returned error instead of silently leaving partially toggled roots.

Lighting configuration now follows the same fail-closed contract. `ConfigureLightingTargets(..., out error)` and `SetLightingProfile(..., out error)` convert validation/apply exceptions into structured failures, continue isolating individual target exceptions for diagnostics, and restore the previous target set/profile when a staged apply fails after another target has already changed.

Non-Cut transition target validation now follows the same structured failure boundary: exceptions from `IEnvironmentTransitionTarget.ValidateEnvironmentTransition` are returned as `false` plus an error string before state id, root visibility, or transition status is mutated.

`EnvironmentTransitionSpec` now normalizes non-finite durations to `0s` and unsupported enum values to `Cut`; `ConfigureDefaultTransition` uses the same normalized spec before persisting defaults. Invalid serialized/API inputs therefore degrade to a safe immediate transition instead of poisoning later state changes with NaN/Infinity or an unknown mode.

`EnvironmentLightingProfile` now normalizes non-finite RGB channels to neutral `1`, non-finite intensity to `1`, and non-finite weight to `0`; finite values retain the existing clamp semantics. Invalid numeric input therefore cannot propagate NaN/Infinity into lighting targets or renderer state.
