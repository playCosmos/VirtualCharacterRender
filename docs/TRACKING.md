# Tracking

## Principle

VirtualCharacterRender provides built-in basic motion capture for one performer while accepting higher-grade external tracking.

No tracking implementation owns character transforms directly.

## Product scope

The baseline tracking set is:

- face, with eye and mouth quality prioritized
- head
- hands
- upper body

Full-body tracking is separate and optional.

Multi-person identity tracking from one webcam is not an initial feature.

## Pipeline

```text
Built-in / External Source
           ↓
         Adapter
           ↓
Validation / Timestamp / Confidence
           ↓
    Normalized Tracking State
           ↓
       Routing / Mixer
           ↓
     One Active Character
```

## Built-in capture

### Webcam

The built-in webcam path targets:

- facial landmarks/expressions
- left/right eye openness and useful gaze estimation
- mouth openness and mouth-shape estimation
- head pose
- hands
- upper-body landmarks/pose

Quality may vary with camera, lighting, occlusion, and inference backend. Capability health/confidence must be observable.

The webcam path may use separate inference models internally, but they emit one normalized subject state for the active performer.

### Apple mobile face tracking

Support an iPhone/iPad ARKit-compatible facial tracking source.

Internally this is ARKit face-tracking data rather than Face ID authentication.

Use it primarily for high-quality face/head/expression input, including eyes and mouth where the source provides suitable coefficients.

### Mixed source routing

Different body regions may come from different sources for the same performer.

Example:

```text
Face/Eyes/Mouth = ARKit mobile
Head            = ARKit mobile
Hands           = Webcam
Upper Body      = Webcam
```

or:

```text
Face/Head       = Webcam
Hands/UpperBody = VMC or another adapter
```

Changing sources must not require character reload.

### Audio fallback

Microphone-derived mouth/body motion can be used as an optional fallback when visual tracking is unavailable or intentionally disabled.

## Full-body tracking

Full-body tracking is a separate capability.

It may use:

- VMC
- SteamVR-class trackers
- mocopi-class devices
- camera-based full-body solutions
- future plugins/adapters

Full-body solvers, leg tracking, floor/root calibration, and advanced IK do not belong to the baseline performance budget unless explicitly enabled.

## Normalized domains

Face:

- head-related face transform where appropriate
- left/right eye openness
- gaze
- brows
- mouth openness
- mouth-shape coefficients
- generic expressions

Body:

- root/upper-body transforms appropriate to source capability
- humanoid bone transforms where available
- optional confidence per joint

Hands:

- wrist pose
- finger curl or detailed joint pose according to source quality

## Source health

Each adapter reports:

- availability/connection
- last-update timestamp
- update rate
- stale state
- confidence where available
- error state
- active inference/transport state

## Timing

Source timestamps are preserved where practical. Network and camera sources are not assumed to update at render-frame frequency.

Interpolation/extrapolation belongs to mixer/runtime policy, not source adapters.

## Explicit non-goals

Current releases do not need:

- multi-person detection-to-avatar assignment
- stable identity tracking for several people in one webcam
- multiple active character bindings
