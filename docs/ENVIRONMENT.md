# Environment Runtime

## Purpose

Environment is a first-class scene subsystem rather than a passive background image.

A lightweight one-character session can use a static image with no environment-side recurring update loop. The same runtime contract can also host video, parallax, reactive 2D/2.5D content, 3D spaces, lighting influence, shader-driven effects, and event-reactive state.

## P6 architecture

The environment boundary is represented by:

- `IEnvironmentRuntime`
- `EnvironmentRuntimeStatus`
- `EnvironmentStateChange`
- `EnvironmentUpdatePolicy`
- `EnvironmentSpaceMode`
- transition contracts
- lighting-influence contracts
- Unity `BasicEnvironmentRuntime`

The runtime owns one active environment/state for the current one-character product scope.

## Environment state

Environment states change without scene reload.

Typical state IDs include:

- day / night
- weather variants
- stream starting / live / break / ending
- calm / alert / celebration
- alternate lighting
- event-specific variants

Unity state bindings map IDs to explicit roots. Binding sets are validated before replacement. Duplicate IDs, missing roots, and roots that contain the runtime itself are rejected without partially replacing the valid active binding set.

## Update classes

Supported update policies:

- Static
- EventDriven
- Hz10
- Hz30
- EveryFrame

`BasicEnvironmentRuntime` itself intentionally has no `Update()` method.

Static and EventDriven environments therefore pay no controller-side per-frame callback cost. Recurring policies lazily create/enable `EnvironmentUpdateDriver`. The scheduler is drop-only and does not burst to catch up after stalls.

Environment update targets receive explicit contexts for:

- StateChanged
- Scheduled
- Manual

Target failures are isolated and attributed in diagnostics.

## Coordinate spaces

Environment content can explicitly use:

- World
- Camera
- Screen
- Character

`IEnvironmentSpaceTarget` validates a requested mode before movement. Unity `EnvironmentSpaceAnchor` reparents content to explicit anchors and can reset local transform. A failed space change leaves both the previous parent and active mode intact.

This keeps room geometry, camera-relative decorations, screen overlays, and character-follow effects from sharing ambiguous transform semantics.

## State transitions

Transition modes are explicit:

- Cut
- Fade
- Crossfade
- Dissolve

Cut is immediate and requires no transition target or recurring transition driver.

Non-Cut transitions require an explicit `IEnvironmentTransitionTarget`. While a transition is active, the runtime keeps the previous and next state roots active, lazily enables `EnvironmentTransitionDriver`, supplies normalized progress, then leaves only the destination root active and disables the driver.

Unity `CanvasGroupEnvironmentTransitionTarget` supplies lightweight Fade and Crossfade for screen/UI environments.

Dissolve is intentionally not emulated with CanvasGroup alpha. It remains a shader-specific optional target so its shader compatibility and performance can be validated explicitly.

## Lightweight 2D targets

Unity `Environment2DLayerTarget` supports preconfigured:

- StaticImage
- Video
- Parallax

The component does not own a recurring `Update()` loop.

Static images and videos can remain Static/EventDriven. Parallax moves only when the environment runtime dispatches an update, so a scene can deliberately choose Hz10, Hz30, or EveryFrame according to visual need and performance budget.

Media loading/decoding remains the responsibility of Unity assets/VideoPlayer rather than being duplicated inside the environment state controller.

## 2.5D and 3D content

The same state/update/space contracts can contain:

- billboards
- depth layers
- particles
- meshes
- lights
- reflections
- effects
- optional world physics
- procedural/shader-driven objects

These are capabilities of one environment pipeline, not separate product modes.

## Lighting integration

Environment lighting influence is explicit rather than implemented by rewriting character materials.

`EnvironmentLightingProfile` contains:

- RGB influence color
- intensity multiplier
- influence weight

`IEnvironmentLightingTarget` consumes that profile.

Unity `EnvironmentLightInfluenceTarget` captures source Light color/intensity, applies weighted influence, and restores the source values when the target is removed, disabled through the runtime, or destroyed.

Character shader/material source state remains outside this environment-lighting mechanism.

## Failure containment

- invalid state bindings do not replace the current valid set
- invalid space targets do not partially reparent content
- non-Cut transitions without a target are rejected
- transition-target exceptions are isolated and counted
- update-target exceptions are isolated and counted
- invalid lighting target sets are rejected atomically
- removing lighting targets restores source Light values

## Diagnostics

Environment runtime metrics include:

- active/update policy
- state change/update counts
- update/space/transition/lighting target counts
- active space mode
- transition mode/progress/count/failures
- lighting weight/intensity multiplier/failures
- recurring update-driver state
- update-dispatch last/total/average milliseconds
- transition-dispatch last/total/average milliseconds

This allows environment cost to remain attributable instead of disappearing into general frame time.

## Performance posture

The lightweight path is intentionally event/static first:

- no runtime `Update()` callback for Static/EventDriven
- no transition driver except during an active non-Cut transition
- no parallax movement unless scheduled
- no forced video/parallax capability initialization
- no character material mutation for environment lighting

720p60/1080p60 guarantees still require target-hardware evidence and are not inferred from source structure alone.

## Deferred evidence / optional capability work

Still requires Unity/device evidence:

- real image/video assets on Windows/macOS
- video decode cost
- Fade/Crossfade visual quality
- shader-specific Dissolve target implementation when needed
- lighting influence on real VRM/MToon scenes
- Hz10/Hz30/EveryFrame parallax cost
- transition/update allocation and CPU cost on target hardware

## Future packaging

A future environment package may contain:

```text
environment/
├─ manifest
├─ scene
├─ meshes
├─ textures
├─ shaders
├─ audio
├─ animations
├─ presets
└─ preview
```

The exact package schema remains a later package/plugin-layer concern rather than part of the P6 state runtime.
