# Roadmap

## P0 — Architecture and feasibility
Deliverables:
- architecture/subsystem documentation
- ADR process
- Unity/URP feasibility prototype
- UniVRM VRM 0.x and 1.0 load validation
- transparent-window and OBS validation
- MToon validation
- custom shader/material override prototype
- VMC receive/send spike
- normalized-state prototype

Exit criteria:
- one VRM loads and renders correctly
- transform, camera, and light work
- alpha output is correct
- OBS captures the intended output path
- one material can be overridden without corrupting the source model
- an invalid custom shader does not terminate the app
- one normalized tracking value reaches runtime state through an adapter
- P0 ADRs are accepted, rejected, or explicitly deferred

## P1 — Renderer Core
Scene/character lifecycle, camera/light abstraction, frame timing, model hot reload, resolution/render scale, diagnostics, safe shutdown.

## P2 — Material and Shader Runtime
Material-slot abstraction, MToon preservation, runtime overrides, generic shader parameters, presets, fallback material, error reporting.

## P3 — Shader Package / Plugin Layer
Manifest, resources, compatibility metadata, validation, hot reload where supported, failure containment.

## P4 — Tracking Abstraction
Timestamped source adapters, normalized tracking model, confidence/health, smoothing hooks, source priority, body-region routing.

## P5 — Motion and Expression Mixer
Base pose, tracking pose, additive motion, expressions, procedural motion, weighting, masks, deadzones, smoothing, fallback.

## P6 — Protocols
VMC, OSC, and WebSocket first. MIDI is an extension target.

## P7 — Event Runtime
Implement runtime before graph UI:
```text
Event → Condition → Transform → Action
```
Actions can target expression, motion, shader/material parameters, camera, props, and effects.

## P8 — Broadcast Output
Productionize transparent window and OBS. Promote Spout2 only if evidence justifies it. Virtual camera/NDI remain optional.

## P9 — Application UI
Character, Scene, Tracking, Motion, Expression, Material, Shader, Events, Output, Settings, Diagnostics.

## P10 — 2D Backend
Evaluate Inochi2D, Live2D integration, or another backend against the same runtime boundary.

## Scope control
Features outside the current phase are not blockers unless an ADR promotes them.
