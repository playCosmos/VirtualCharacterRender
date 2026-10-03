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
- custom shader/material override prototype:
  - non-destructive material-slot discovery
  - runtime material clones
  - generic parameter setters
  - safe source-material fallback
  - precompiled shader ID registry
  - platform-specific AssetBundle shader registration
- MediaPipeUnityPlugin dual-task LIVE_STREAM spike:
  - FaceLandmarker: face/eyes/mouth/head
  - HolisticLandmarker: hands/upper body
  - independent async frame-drop behavior
  - per-task M1 CPU inference cost
- Apple ARKit-compatible mobile face-tracking input spike via iFacialMocap/FaceMotion3D UDP v2 compatibility adapter
- ARKit-priority / MediaPipe-face-fallback router with Holistic hands/upper-body kept active
- VMC receive/send spike:
  - bounded OSC message/bundle codec
  - VMC UDP receiver with loopback-by-default sender filter
  - source-neutral humanoid pose with explicit pose space and expression domains
  - optional external full-body routing
  - calibrated UniVRM full-body P0 target
  - normalized final-character snapshot provider
  - VMC UDP sender with VRM0-compatible expression names
- normalized face/upper-body/hand payload prototype
- normalized tracking -> UniVRM ControlRig mapping prototype
- tracking-derived subject-presence resolver with source-loss distinction and neutral fallback prototype
- normalized event prototype:
  - canonical tracking/chat/donation event types
  - bounded thread-safe ingress queue
  - main-thread sequence-stamped dispatch
  - tracking subject/source loss adapter
- dynamic-environment abstraction prototype:
  - environment ID/state/status
  - world/camera/screen/character space mode
  - static/event-driven/lower-rate/every-frame update policy
  - Unity basic environment root with no per-frame cost in static/event-driven mode
- capability/lazy-initialization bootstrap:
  - factory registration without service creation
  - enable=create
  - disable=dispose
  - repeatable re-enable lifecycle
- early diagnostics/per-subsystem timing:
  - frame average/P95/P99
  - normalized channel update rates and snapshot age
  - MediaPipe Face/Holistic submit-to-callback latency
  - protocol packet/error counters
  - optional CSV evidence

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
- a precompiled custom shader bundle can be registered/applied on both target platforms without source-material mutation
- FaceLandmarker produces usable normalized eye/mouth/face/head state without requiring pose
- normalized face state drives VRM head/blink/look/mouth through the character adapter
- HolisticLandmarker produces usable hand/upper-body state
- normalized upper-body state drives torso and arm motion through UniVRM ControlRig/fallback bones
- Face and Holistic live-stream inference do not block the Unity render loop
- M1 tracking CPU cost, Face/Holistic processing latency, and end-to-end behavior are measured
- iFacialMocap/FaceMotion3D ARKit-compatible face data reaches desktop normalized state
- mixed ARKit + Holistic routing works without character reload and disables redundant MediaPipe face inference while ARKit is healthy
- performer disappearance can be distinguished from source/device disconnection
- tracking.subject_lost enters the normalized event pipeline without AFK/timer semantics
- basic environment state can change without scene reload
- lazy capability self-test proves disabled services are not instantiated
- stable tracking loss returns the VRM tracking contribution toward neutral without a one-frame snap
- brief tracking loss does not cause rapid SubjectLost/SubjectRestored flapping
- VMC codec self-test passes and external receive/send interoperability is demonstrated before ADR-0006 acceptance
- P0 diagnostics records frame P95/P99 and tracking/protocol metrics on both target platforms
- optional full-body services remain disabled in the baseline
- disabled optional capabilities have no meaningful recurring frame cost
- exact Unity LTS / URP / UniVRM versions are pinned or explicitly blocked with evidence
- remaining P0 ADRs are accepted, rejected, or explicitly deferred

## P1 — Cross-platform Renderer Core

Status: Source implementation checkpointed; runtime/device evidence remains deferred. See `P1_STATUS.md`.

One-character scene/character lifecycle, camera/light abstraction, frame timing, model hot reload, dynamic-environment hook, resolution/render scale, diagnostics, safe shutdown, platform adapters, and capability lifecycle.

## P2 — Material and Shader Runtime

Status: Source implementation checkpointed; runtime/device evidence remains deferred. See `P2_STATUS.md`.

Material-slot abstraction, MToon preservation, runtime overrides, generic shader parameters, presets, fallback material, cross-platform compatibility reporting, and error handling.

## P3 — Built-in Basic Motion Capture

Status: Source implementation checkpointed; real webcam/ARKit quality and performance evidence remains deferred. See `P3_STATUS.md`.

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

Status: Source implementation complete enough for a checkpoint; Unity source-free validation and real-device transition evidence are still required. See `P4_STATUS.md`.

Implemented source scope includes common source health, timestamps/route age, subject validity, explicit source-priority policy, region routing, hot switching, route diagnostics, and VMC-to-audio expression fallback selection for the one active performer.

Multi-person identity tracking is not in scope.

## P5 — Motion and Expression Mixer

Status: Source implementation checkpoint-ready; Unity allocation/performance evidence remains deferred. See `P5_STATUS.md`.

Base pose, tracking pose, additive motion, expressions, procedural motion, weighting, masks, deadzones, smoothing, and fallback behavior.

The first implementation slice establishes the final mix-provider contract and expression-layer blending while passing routed face/body/full-body frames through unchanged.

## P6 — Environment Runtime

Status: Source implementation checkpoint-ready; Unity media/performance evidence remains deferred. See `P6_STATUS.md`.

Implemented source scope includes state-root switching, Static/EventDriven/Hz10/Hz30/EveryFrame update classes, World/Camera/Screen/Character anchors, Cut/Fade/Crossfade transition runtime with optional Dissolve target contract, lightweight image/video/parallax targets, weighted environment-light influence, failure containment, and update/transition cost attribution.

Real media decode, transition visual quality, shader-specific Dissolve, VRM lighting quality, and target-hardware performance still require evidence.

## P7 — Material/Shader Package and Plugin Layer

Status: Source implementation checkpoint-ready; cross-platform package/performance evidence remains deferred. See `P7_STATUS.md`.

Implemented source scope includes validated shader-package manifests/resources, Unity/URP/platform compatibility metadata, transactional load/unload/explicit reload, shader/texture registry rollback, unsafe/undeclared executable-source rejection, declarative capability registration, failure containment, and diagnostics.

Executable third-party plugin loading remains gated by deferred ADR-0015 and is not part of the P7 source checkpoint.

## P8 — Protocols and Event Adapters

Status: Source implementation checkpoint-ready; real network/service interoperability evidence remains deferred. See `P8_STATUS.md`.

Implemented source scope includes:

- existing VMC receive/send interoperability contracts
- generic OSC `/vcr/event` mapping and loopback-default UDP receiver
- versioned WebSocket `event.inject` schema and outbound `ClientWebSocket` bridge transport
- bounded background-network to main-thread dispatch queues
- external ingress validation and tracking-event spoof rejection
- tracking-derived subject/source event adaptation
- SOOP chat and donation bridge mapping with event-id duplicate suppression
- adapter/transport diagnostics

A public application-control WebSocket listener/server is not implied by the P8 bridge client and remains a separate future versioned surface if P11/P12 requires it.

Additional broadcast platforms use adapters rather than changing event-runtime contracts.

## P9 — Event Runtime

Status: Active. See `P9_STATUS.md`.

Implement event execution before the visual graph editor.

```text
Event → Filter/Condition → Transform/State → Action
```

The current source slice implements exact/source/actor/text/amount filters, typed runtime state and conditions, state mutation, numeric scale/offset transforms, per-rule cooldown, bounded action-command emission, a main-thread runtime host, failure containment, diagnostics, `environment.set_state`, `camera.set_fov`, `material.set_float`, and `expression.set`.

Targets will expand to character motion, appearance/wardrobe quick change, environment transitions, broader shader/material parameters, props, effects, audio, and overlay elements without exposing Unity object references inside rule definitions.

## P10 — Broadcast Output

Status: Source implementation checkpoint-ready; standalone/OBS evidence remains deferred. See `P10_STATUS.md`.

Productionize the existing transparent-overlay path and OBS workflow on both platforms. The first source slice adds explicit configured/pending/active/unsupported/fault output states, bounded native-apply timeout behavior, and an application-side capture-readiness contract while retaining `IOverlayOutputAdapter` as the platform boundary.

Windows/macOS standalone transparency, OBS alpha capture, resize/high-DPI behavior, and 720p60/1080p60 capture stability remain evidence gates. Platform-specific high-performance output transports may be optional adapters.

## P11 — Application UI

Status: Active. See `P11_STATUS.md`.

The first source slice establishes a programmatic uGUI shell, stable navigation/availability model, runtime status summaries, configuration save, overlay recovery, and a dedicated P11 runtime-scene builder.

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

Character UI also includes a planned Appearance / Quick Change area for named outfit variants, accessory slots, named presets, restore-default, and event-compatible switching without reloading the active VRM. See `APPEARANCE_QUICKCHANGE.md`.

## P12 — Advanced One-Character Scene Tooling

- visual event/node editor
- richer environment/prop/effect automation
- multiple scene cameras if justified
- advanced shader bindings
- advanced plugin workflows
- full-body tracking integration as optional capability
- wardrobe/accessory authoring and import tooling: outfit-root registration, accessory slots/anchors, preset authoring, validated external accessory packages, and optional compatible skinned-outfit packages

## P13 — 2D Extension

Evaluate Inochi2D, Live2D integration, or another backend/extension against the established tracking/event/output contracts.

## Future only — Multi-character

Do not implement multi-person webcam tracking, multiple simultaneous performer bindings, multi-character UI, or multi-character performance work in the current roadmap.

Keep internal contracts extensible where inexpensive so a future ADR can introduce multi-character support without forcing it into current complexity.

## Scope control

Features outside the active phase are not blockers unless an ADR promotes them. Lightweight/default performance is a release criterion throughout development, not a cleanup task after advanced features are complete.
