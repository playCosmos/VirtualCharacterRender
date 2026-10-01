# Environment Runtime

## Purpose

Environment is a first-class scene subsystem rather than a passive background image.

A lightweight session may use a single static image, while the same interface can represent parallax, video, 3D spaces, dynamic lighting, shader-driven scenes, and event-reactive states.

## P0 implementation

The P0 environment boundary is now represented by:

- `IEnvironmentRuntime`
- `EnvironmentRuntimeStatus`
- `EnvironmentStateChange`
- `EnvironmentUpdatePolicy`
- `EnvironmentSpaceMode`
- Unity `BasicEnvironmentRuntime`

The generated P0 runtime scene owns its directional light under an explicit `Environment` root.

`BasicEnvironmentRuntime` intentionally has no `Update()` method. Static and event-driven environments therefore pay no controller-side per-frame update cost.

A state change such as `day -> night` updates the environment state without requiring a scene reload. Visual state binding/transitions are later Environment Runtime work.

## Environment classes

```text
Environment
├─ 2D Layer
│  ├─ image
│  ├─ video
│  ├─ animated texture
│  └─ parallax
├─ 2.5D Space
│  ├─ billboards
│  ├─ depth layers
│  └─ particles
├─ 3D Space
│  ├─ mesh
│  ├─ light
│  ├─ reflection
│  ├─ effects
│  └─ optional physics
└─ Procedural
   ├─ shader-driven
   ├─ audio-reactive
   └─ event-reactive
```

These are environment capabilities, not separate product modes.

## Scene relationship

```text
Scene Runtime
├─ Character (one active character in the product scope)
├─ Environment
├─ Props
├─ Camera
├─ Lights
└─ Effects
```

Multi-character scene operation is not an initial product feature.

## Environment state

A loaded environment can expose state without requiring a full scene reload.

Examples:

- day / night
- weather
- stream starting / live / break / ending
- calm / alert / celebration
- alternate lighting
- event-specific variants

## Transition

Supported transition concepts may include:

- cut
- fade
- crossfade
- dissolve
- camera movement
- shader transition

Expensive transitions are optional capabilities and must participate in performance diagnostics.

## Coordinate spaces

Environment/render elements may explicitly use:

- world space
- camera-relative space
- screen space
- character-relative space

This avoids forcing broadcast decorations and character-follow effects into the same transform semantics as room geometry.

## Update classes

Environment objects may declare update policy:

- every frame
- fixed lower rate
- event-driven
- static

The runtime should not update static or event-driven objects every render frame.

## Lighting integration

Environment lighting may influence character rendering through an explicit weight/profile rather than destructively changing character materials.

## Packaging

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

Exact schema is deferred until the runtime reader and validator exist.
