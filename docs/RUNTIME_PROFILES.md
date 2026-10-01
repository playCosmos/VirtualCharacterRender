# Runtime Profiles

## Goal

VirtualCharacterRender uses one one-character runtime pipeline from lightweight desktop operation to advanced scene/event/environment workflows.

There are no separate Lite and Pro engines. Profiles control capabilities and budgets on top of the same character, tracking, scene, and renderer contracts.

## Product constraint

All current profiles operate on exactly one active character.

Multi-character behavior is not an Advanced-profile feature. It is a future product expansion only.

## Principle

```text
Same executable/runtime
        ↓
Capability Registry
        ↓
Selected capability preset
        ↓
Only required services initialized
        ↓
Same one-character pipeline
```

## Lightweight/default

Target: VSeeFace-class simple use with built-in capture.

Default scope:

- one active character
- one primary camera
- one environment
- webcam and/or ARKit-compatible input
- face tracking with eyes/mouth prioritized
- head tracking
- hand tracking
- upper-body tracking
- expressions and basic motion
- MToon/default material
- transparent overlay
- basic OBS workflow
- basic AFK/local-state events where configured
- minimal background services

Not active unless requested:

- full-body tracking/IK
- heavy post processing
- visual event graph editor
- optional protocol listeners
- executable plugins
- expensive environment features

## Standard

Adds optional capabilities such as:

- multiple tracking-source routing for the same performer
- richer motion/expression mixing
- custom shader presets and bindings
- VMC/OSC/WebSocket integrations
- reactive environment states
- props
- richer post processing
- broadcast chat/donation event adapters
- detailed diagnostics

## Advanced

Target: Warudo-class extensibility for one performer.

Adds:

- advanced environment/world features
- event/node runtime and later editor
- complex shader/event bindings
- multiple scene cameras where justified
- props/effects automation
- plugin extensions
- multiple protocol/event sources
- optional full-body tracking
- scripting/automation interfaces only if separately approved

## Capability model

Example capability IDs:

```text
render.vrm
render.custom-shader
render.post-process
tracking.webcam-face
tracking.arkit-face
tracking.head
tracking.hands
tracking.upper-body
tracking.full-body
environment.image
environment.video
environment.parallax
environment.3d
environment.dynamic-light
protocol.vmc
protocol.osc
protocol.websocket
events.presence
events.broadcast-chat
events.donation
events.runtime
plugins.runtime
output.transparent-window
```

## Graphics quality is separate

Runtime capability selection is not graphics quality.

A user can use advanced events with reduced visual quality or a simple character session with high visual quality.

Graphics-quality presets and capability presets are stored separately.

## Lazy initialization

- do not start camera inference if webcam tracking is disabled
- do not start a full-body solver if full tracking is disabled
- do not open protocol/event connections if disabled
- do not allocate heavy post-processing/environment resources if unused
- do not load executable plugins unless enabled

## Performance budgets

Track at minimum CPU/GPU frame time, memory, render FPS, tracking latency/update rate, mixer cost, shader/post-processing cost, environment cost, and protocol/event cost.

See `PERFORMANCE.md`.

## Compatibility rule

Increasing or reducing capability selection must not invalidate the active character/profile. Unsupported optional features degrade explicitly rather than corrupting the scene.
