# Roadmap

## P0 — Architecture and feasibility

Deliverables:

- architecture/subsystem documentation and ADR process
- Windows and macOS validation matrix
- Unity/URP implementation baseline on both platforms
- UniVRM VRM 0.x and 1.0 load validation
- transparent-window and OBS-compatible capture validation
- 720p60 minimum and 1080p60 recommended baseline measurements
- MToon validation
- custom shader/material override prototype
- MediaPipeUnityPlugin dual-task LIVE_STREAM spike:
  - FaceLandmarker: face/eyes/mouth/head
  - HolisticLandmarker: hands/upper body
  - independent async frame-drop behavior
  - per-task M1 CPU inference cost
- Apple ARKit-compatible mobile face-tracking input spike
- mixed ARKit + Holistic hands/upper-body routing spike
- VMC receive/send spike
- normalized tracking-state prototype
- tracking-derived subject-presence prototype
- normalized event prototype
- dynamic-environment abstraction prototype
- capability/lazy-initialization bootstrap
- early diagnostics/per-subsystem timing

Exit criteria:

- one VRM loads/renders on Windows and macOS
- exactly one active character is the tested product path
- transform, camera, light, and basic environment work
- alpha output is correct
- OBS-compatible capture is demonstrated
- 720p60 is sustained in the defined baseline on an otherwise-unloaded validated PC
- 1080p60 behavior is measured and treated as the primary recommended target
- higher resolutions remain configurable without inheriting the same blanket guarantee
- one material can be overridden without corrupting the source model
- an invalid custom shader does not terminate the application
- FaceLandmarker produces usable normalized eye/mouth/face/head state without requiring pose
- HolisticLandmarker produces usable hand/upper-body state
- Face and Holistic live-stream inference do not block the Unity render loop
- M1 tracking CPU cost and end-to-end latency are measured
- mobile ARKit-compatible face data reaches desktop normalized state
- mixed ARKit + Holistic routing works without character reload
- performer disappearance can be distinguished from source/device disconnection
- brief tracking loss does not cause rapid SubjectLost/SubjectRestored flapping
- optional full-body services remain disabled in the baseline
- disabled optional capabilities have no meaningful recurring frame cost
- exact Unity LTS / URP / UniVRM versions are pinned or explicitly blocked with evidence
- remaining P0 ADRs are accepted, rejected, or explicitly deferred

## P1 — Cross-platform Renderer Core

One-character scene/character lifecycle, camera/light abstraction, frame timing, model hot reload, dynamic-environment hook, resolution/render scale, diagnostics, safe shutdown, platform adapters, and capability lifecycle.

## P2 — Material and Shader Runtime

Material-slot abstraction, MToon preservation, runtime overrides, generic shader parameters, presets, fallback material, cross-platform compatibility reporting, and error handling.

## P3 — Built-in Basic Motion Capture

Productionize:

- MediaPipe FaceLandmarker + HolisticLandmarker live-stream webcam paths
- face tracking with eye and mouth quality prioritized
- head pose
- hands
- upper body
- low-light tracking preprocessing/refinement
- Apple ARKit-compatible face input
- optional audio-driven fallback motion
- subject-presence derivation from webcam/ARKit tracking state

Full-body tracking/IK is a separate optional capability and does not define baseline runtime cost.

Do not split Holistic into separate Hand/Pose tasks unless measured profiling or hand-quality evidence justifies the additional complexity.

## P4 — Tracking Abstraction and Routing

Source health, timestamps, confidence, subject validity, smoothing, source priority, region routing, source hot switching, and subject/source loss distinction for the one active performer.

Multi-person identity tracking is not in scope.

## P5 — Motion and Expression Mixer

Base pose, tracking pose, additive motion, expressions, procedural motion, weighting, masks, deadzones, smoothing, and fallback behavior.

## P6 — Environment Runtime

Static image/video, parallax, reactive 2D/2.5D layers, 3D environment hooks, state changes, transitions, environment lighting, update classes, and performance attribution.

## P7 — Material/Shader Package and Plugin Layer

Manifest, resources, compatibility metadata, validation, hot reload where supported, failure containment, and capability registration.

Executable plugin support remains gated by ADR-0015.

## P8 — Protocols and Event Adapters

VMC, OSC, and WebSocket plus normalized event injection.

Introduce:

- tracking-derived subject presence source
- one broadcast chat integration
- one donation/support integration

Additional platforms use adapters rather than changing event-runtime contracts.

## P9 — Event Runtime

Implement event execution before the visual graph editor.

```text
Event → Filter/Condition → Transform/State → Action
```

Targets include character, expressions, motion, environment, shader/material parameters, camera, props, effects, audio, and overlay elements.

## P10 — Broadcast Output

Productionize transparent overlay and OBS workflow on both platforms. Platform-specific high-performance output transports may be optional adapters.

## P11 — Application UI

UI areas:

- Character
- Tracking
- Motion/Expression
- Environment
- Material/Shader
- Events
- Camera/Output
- Settings
- Diagnostics

Advanced controls may remain collapsed/disabled when their capabilities are unused.

## P12 — Advanced One-Character Scene Tooling

- visual event/node editor
- richer environment/prop/effect automation
- multiple scene cameras if justified
- advanced shader bindings
- advanced plugin workflows
- full-body tracking integration as optional capability

## P13 — 2D Extension

Evaluate Inochi2D, Live2D integration, or another backend/extension against the established tracking/event/output contracts.

## Future only — Multi-character

Do not implement multi-person webcam tracking, multiple simultaneous performer bindings, multi-character UI, or multi-character performance work in the current roadmap.

Keep internal contracts extensible where inexpensive so a future ADR can introduce multi-character support without forcing it into current complexity.

## Scope control

Features outside the active phase are not blockers unless an ADR promotes them. Lightweight/default performance is a release criterion throughout development, not a cleanup task after advanced features are complete.
