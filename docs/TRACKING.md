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

## Accepted baseline stack

ADR-0022 defines the built-in tracking stack.

Primary baseline:

```text
Webcam
 └─ MediaPipe-class tracking
      ├─ face fallback
      ├─ head fallback
      ├─ hands
      └─ upper body

ARKit-compatible mobile source
 └─ preferred face / eyes / mouth / head
```

Preferred mixed configuration:

```text
Face / Eyes / Mouth / Head = ARKit
Hands / Upper Body         = MediaPipe Webcam
```

Fallback without ARKit:

```text
Face / Eyes / Mouth / Head = MediaPipe Webcam
Hands / Upper Body         = MediaPipe Webcam
```

Do not continuously average ARKit and MediaPipe face data by default. Route by region and source health, then transition smoothly on source changes.

## Pipeline

```text
Built-in / External Source
           ↓
         Adapter
           ↓
Validation / Timestamp / Confidence
           ↓
Quality / Region Routing
           ↓
Calibration / Temporal Filtering
           ↓
    Normalized Tracking State
           ↓
       Routing / Mixer
           ↓
     One Active Character
```

## Webcam path

The MediaPipe webcam path targets:

- facial landmarks/expressions
- left/right eye openness and useful gaze estimation
- mouth openness and mouth-shape estimation
- head pose
- hands
- upper-body landmarks/pose

The tracking path may use:

- ROI cropping
- image quality analysis
- adaptive gamma/brightness correction
- local contrast enhancement
- mild denoise
- low-light preprocessing
- confidence-aware reacquisition
- eye/mouth refinement
- user calibration
- region-specific temporal filters

These operations affect tracking input only unless explicitly shown in a preview.

Face, hands, and upper body may run at different update rates while rendering remains at 60 FPS.

## Apple mobile face tracking

Support an iPhone/iPad ARKit-compatible facial tracking source.

Internally this is ARKit face-tracking data rather than Face ID authentication.

Use it as the preferred source for high-quality face/head/expression input, especially eyes and mouth, when healthy.

## Source switching

Priority for face/head:

1. ARKit when healthy
2. MediaPipe webcam fallback
3. configured neutral/audio fallback

Switching sources must not require character reload.

A short transition/cross-fade should avoid visible pose snapping.

## Audio fallback

Microphone-derived mouth/body motion can be used as an optional fallback when visual tracking is unavailable or intentionally disabled.

## Subject presence

Tracking must distinguish performer absence from source failure.

The normalized tracking/health layer must expose enough information for a presence-state service to derive:

- `SubjectLost`
- `SubjectRestored`
- `TrackingSourceLost`
- `TrackingSourceRestored`

`SubjectLost` means the configured performer is no longer reliably detected after a configurable grace period.

Presence is evaluated across configured tracking sources. For example:

- MediaPipe loses the face while ARKit remains healthy => subject remains present
- ARKit is lost while MediaPipe still detects the performer => subject remains present
- all relevant subject-detection paths are lost beyond the grace interval => `SubjectLost`

`TrackingSourceLost` means the camera, mobile sender, network transport, or adapter itself is unavailable.

Brief occlusion and temporary confidence loss must not cause rapid presence-event flapping.

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
- subject-validity state
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
