# Roadmap

## P0 — Architecture and feasibility

Deliverables:

- architecture/subsystem documentation and ADR process
- Windows and macOS validation matrix
- Unity/URP feasibility prototype on both platforms
- UniVRM VRM 0.x and 1.0 load validation
- transparent-window and OBS-compatible capture validation on both platforms
- MToon validation
- custom shader/material override prototype
- webcam face/head tracking spike
- Apple ARKit-compatible mobile face-tracking input spike
- VMC receive/send spike
- normalized-state prototype
- capability/profile bootstrap

Exit criteria:

- one VRM loads/renders on both Windows and macOS
- transform, camera, and light work
- alpha output is correct on both target platforms
- OBS-compatible capture path is demonstrated on both target platforms
- one material can be overridden without corrupting the source model
- an invalid custom shader does not terminate the application
- webcam tracking produces normalized head/face state
- mobile ARKit-compatible face data reaches the desktop normalized state
- switching tracking source does not require character reload
- a minimal Lightweight session can run with advanced services disabled
- P0 ADRs are accepted, rejected, or explicitly deferred

## P1 — Cross-platform Renderer Core

Scene/character lifecycle, camera/light abstraction, frame timing, model hot reload, resolution/render scale, diagnostics, safe shutdown, platform adapters, and capability lifecycle.

The same runtime core is used on Windows and macOS.

## P2 — Material and Shader Runtime

Material-slot abstraction, MToon preservation, runtime overrides, generic shader parameters, presets, fallback material, cross-platform shader compatibility reporting, and error handling.

## P3 — Built-in Basic Motion Capture

Productionize:

- webcam face/head tracking
- eye/mouth/expression tracking as source quality permits
- Apple ARKit-compatible face input
- audio-driven fallback motion
- optional webcam hand/body tracking after performance/quality gates

All sources feed the normalized tracking model.

## P4 — Tracking Abstraction and Routing

Source health, timestamps, confidence, smoothing hooks, source priority, body-region routing, and source hot switching.

## P5 — Motion and Expression Mixer

Base pose, tracking pose, additive motion, expressions, procedural motion, weighting, masks, deadzones, smoothing, and fallback.

## P6 — Material/Shader Package and Plugin Layer

Manifest, resources, compatibility metadata, validation, hot reload where supported, failure containment, and capability registration.

## P7 — Protocols

VMC, OSC, and WebSocket first. MIDI remains an extension target.

## P8 — Scene and Event Runtime

Implement runtime before graph UI:

```text
Event → Condition → Transform → Action
```

Actions can target expressions, motion, shader/material parameters, cameras, props, effects, and scene state.

This phase expands the same runtime used by Lightweight sessions rather than introducing a second engine.

## P9 — Broadcast Output

Productionize transparent overlay output and OBS workflow on both platforms. Platform-specific high-performance transports may be added as optional adapters.

## P10 — Application UI

UI scales with profile/capabilities:

- Simple/Lightweight view
- Character
- Scene
- Tracking
- Motion
- Expression
- Material
- Shader
- Events
- Output
- Settings
- Diagnostics

Advanced panels are not required to be active in Lightweight sessions.

## P11 — Advanced Scene Tooling

- visual event/node editor
- richer multi-character scene orchestration
- props/effect automation
- multiple cameras
- advanced plugin workflows

## P12 — 2D Backend

Evaluate Inochi2D, Live2D integration, or another backend against the same tracking/runtime/event contracts.

## Scope control

Features outside the active phase are not blockers unless an ADR promotes them. Lightweight performance is a release criterion throughout development, not a cleanup task after advanced features are complete.
