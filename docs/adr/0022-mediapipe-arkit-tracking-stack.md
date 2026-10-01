# ADR-0022: MediaPipe webcam tracking with ARKit face priority

- Status: Accepted
- Date: 2026-10-01

## Context

VirtualCharacterRender requires built-in tracking for one performer with strong face quality, especially eyes and mouth, while also supporting head, hands, and upper body.

The product must work with an ordinary webcam, but should use higher-quality Apple ARKit-compatible face tracking when available.

The tracking architecture must remain lightweight enough to preserve the 720p60 minimum and 1080p60 recommended rendering goals.

## Decision

Use MediaPipe-class tracking as the primary built-in webcam tracking stack.

Use ARKit-compatible mobile face tracking as the preferred high-quality source for:

- face
- eyes
- mouth
- head pose

when available.

Use MediaPipe webcam tracking for:

- face fallback
- head fallback
- hands
- upper body

and for all baseline tracking when ARKit is not available.

The runtime routes tracking by body region rather than blending every source indiscriminately.

Preferred mixed-source configuration:

```text
Face / Eyes / Mouth / Head = ARKit
Hands / Upper Body         = MediaPipe Webcam
```

Fallback configuration:

```text
Face / Eyes / Mouth / Head = MediaPipe Webcam
Hands / Upper Body         = MediaPipe Webcam
```

## Webcam quality pipeline

The MediaPipe path may include tracking-only preprocessing before inference:

- face/region ROI cropping
- image-quality analysis
- adaptive gamma/brightness correction
- local contrast enhancement
- mild denoise
- low-light-specific preprocessing
- quality/confidence-aware reacquisition

These operations are applied to tracking input and do not modify the user's visible camera image unless explicitly exposed as a preview mode.

## Tracking refinement

Raw tracking output is not sent directly to the character.

The runtime may apply:

- eye-specific refinement
- mouth-specific refinement
- user calibration
- region-specific smoothing
- confidence-aware temporal filtering
- short prediction/hold for transient frame loss
- source-quality monitoring

Face, hands, and upper body may run at different tracking update rates while the renderer remains at 60 FPS.

## Source routing

Do not continuously average ARKit and MediaPipe face data by default.

Use priority and health-aware routing:

1. ARKit face/head when healthy
2. MediaPipe face/head fallback
3. configured neutral/audio fallback if both are unavailable

When switching sources, use a short transition/cross-fade to avoid visible snapping.

## Presence semantics

Subject presence is derived across the active tracking configuration.

Examples:

- MediaPipe face lost while ARKit remains healthy => subject remains present
- ARKit lost while MediaPipe still detects the performer => subject remains present
- all configured subject-detection paths lost beyond the grace interval => `SubjectLost`
- device/network loss remains `TrackingSourceLost`

ADR-0021 defines the event semantics.

## Full-body tracking

Full-body tracking is not part of this baseline stack.

It remains a separate optional capability and may use VMC, mocopi-class devices, SteamVR-class trackers, camera-based full-body solutions, or future adapters.

## Non-decisions

This ADR does not pin:

- exact MediaPipe package/version
- exact face/hand/pose model variant
- tracking FPS per region
- preprocessing thresholds
- lost/restore timing thresholds
- mobile ARKit transport protocol

Those are selected through P0/P3 validation.

ADR-0028 subsequently selects iFacialMocap/FaceMotion3D UDP v2 as the first P0 compatibility transport. That does not make it the permanent or exclusive ARKit transport.

OpenSeeFace or another tracker may later be evaluated as an optional fallback or quality-assist path, but it is not part of the accepted baseline.

## Validation

The baseline must measure:

- eye stability and blink response
- mouth-open/mouth-shape response
- head-pose stability
- hand tracking stability
- upper-body stability
- low-light tracking-loss rate
- reacquisition time
- end-to-end latency
- CPU/GPU/inference cost
- source-switch continuity
- subject-presence correctness

Validation should compare at minimum:

- unmodified MediaPipe webcam tracking
- enhanced MediaPipe webcam tracking
- ARKit face/head input
- mixed ARKit + MediaPipe routing

## Consequences

- ordinary webcam use requires no external VTuber application
- ARKit users receive higher-quality face input without changing the character pipeline
- tracking compute can be concentrated where it provides visible benefit
- source-specific code remains behind adapters
- future tracker replacements do not change normalized tracking contracts

## Revisit conditions

Revisit the baseline stack only if another implementation produces materially better quality, latency, or efficiency while preserving the same normalized tracking and source-routing contracts.
