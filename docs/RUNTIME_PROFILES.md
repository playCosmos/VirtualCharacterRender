# Runtime Profiles

## Goal

VirtualCharacterRender uses one runtime pipeline from lightweight avatar rendering to advanced scene/event operation.

There are no separate "lite" and "pro" runtimes. Profiles select capabilities, budgets, and services on top of the same domain model and renderer interfaces.

## Principle

```text
Same executable/runtime
        ↓
Capability Registry
        ↓
Selected Runtime Profile
        ↓
Only required services initialized
        ↓
Same Character / Tracking / Material / Output pipeline
```

A lightweight session must not initialize advanced subsystems merely because they exist in the product.

## Initial profiles

### Lightweight

Target: VSeeFace-class simple desktop use.

Default scope:

- one active character
- one primary camera
- webcam face tracking or iPhone ARKit-compatible face input
- basic head/face/eye/mouth motion
- optional simple hand/body tracking when available
- expressions
- MToon/default material plus simple overrides
- transparent overlay
- basic OBS workflow
- minimal background services

Excluded by default:

- event graph runtime
- multiple scene cameras
- heavy post effects
- multi-character scene orchestration
- advanced plugin scanning
- unused protocol listeners

### Standard

Adds:

- multiple tracking-source routing
- motion/expression mixing
- custom shader presets and bindings
- VMC/OSC/WebSocket integrations
- props
- richer post processing
- scene presets
- more detailed diagnostics

### Advanced

Target: Warudo-class extensibility and scene behavior.

Adds:

- multiple characters
- scene graph/orchestration
- event/node runtime
- complex shader/event bindings
- multiple cameras
- props/effects automation
- plugin extensions
- multiple protocol sources
- advanced tracking routing
- scripting/automation interfaces when separately approved

## Capability model

Features are exposed as capabilities rather than profile-specific code forks.

Example capability IDs:

```text
render.vrm
render.custom-shader
render.post-process
tracking.webcam-face
tracking.arkit-face
tracking.body
tracking.hands
protocol.vmc
protocol.osc
protocol.websocket
scene.multi-character
scene.props
events.runtime
plugins.runtime
output.transparent-window
```

A profile is a predefined capability set plus runtime budgets. Users may later customize a profile without changing the pipeline.

## Lazy initialization

Subsystems must initialize on demand whenever practical.

Examples:

- do not start camera capture if webcam tracking is disabled
- do not open VMC/OSC sockets if the protocol is disabled
- do not initialize event execution if no event graph is active
- do not allocate post-processing resources when unused
- do not scan/load optional plugins unnecessarily
- do not update invisible/inactive characters at full cost without a reason

## Performance budgets

Performance is measured per subsystem and per profile.

Track at minimum:

- CPU frame time
- GPU frame time
- memory
- render FPS
- tracking latency/update rate
- motion-mixer cost
- shader/post-processing cost
- protocol/event cost

The Lightweight profile is a release target, not merely an Advanced profile with UI options hidden.

## Compatibility rule

A character/profile created in Lightweight remains valid in Standard or Advanced. Advanced-only capabilities that are unavailable in a lower profile must degrade explicitly rather than corrupting the character or scene.
