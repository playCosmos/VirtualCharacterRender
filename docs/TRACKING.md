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

ADR-0022 defines the source strategy.
ADR-0023 defines the Unity/MediaPipe execution strategy.

Primary webcam baseline:

```text
Webcam
 ↓
Shared capture/preprocess
 ↓
MediaPipe HolisticLandmarker
 (LIVE_STREAM)
 ├─ face
 ├─ pose / upper body
 ├─ left hand
 └─ right hand
 ↓
Normalized Tracking State
```

Preferred mixed configuration:

```text
Face / Eyes / Mouth / Head = ARKit
Hands / Upper Body         = MediaPipe Holistic webcam path
```

Fallback without ARKit:

```text
Face / Eyes / Mouth / Head = MediaPipe Holistic webcam path
Hands / Upper Body         = MediaPipe Holistic webcam path
```

Do not continuously average ARKit and MediaPipe face data by default. Route by region and source health, then transition smoothly on source changes.

## Unity execution model

Initial implementation uses:

- homuler MediaPipeUnityPlugin
- MediaPipe Tasks API
- HolisticLandmarker
- LIVE_STREAM/asynchronous execution
- one webcam capture/preprocessing path
- one performer
- segmentation disabled by default

Do not start with separate FaceLandmarker, HandLandmarker, and PoseLandmarker loops.

This reduces task orchestration, repeated camera handling, synchronization, and managed/native crossings.

When ARKit owns the face/head regions, Holistic may still run its face path initially while those outputs are ignored. This deliberate redundancy keeps the implementation simple. Split hand/pose inference is considered only if profiling proves that redundant face inference materially breaks the performance target.

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

## Webcam quality path

The webcam path targets:

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

## Frame policy

Tracking freshness is more important than processing every camera frame.

- renderer remains 60 FPS
- webcam inference runs asynchronously
- do not build a deep frame queue
- stale input frames may be dropped
- results carry timestamps
- mixer interpolation/smoothing bridges tracking rate to render rate

Exact tracking/camera rates are selected after M1/Windows profiling.

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
