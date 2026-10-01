# Tracking

## Principle
Tracking devices and algorithms are interchangeable inputs. No tracking implementation owns character transforms directly.

## Pipeline
```text
Source → Adapter → Validation/Timestamp → Normalized State → Routing/Mixer → Character Runtime
```

## Candidate sources
- webcam face tracking
- OpenSeeFace
- MediaPipe-class face/hand/body tracking
- iPhone ARKit-compatible data
- VMC
- SteamVR-class trackers
- mocopi-class tracking
- audio-derived fallback motion

Candidate status does not imply P0 implementation commitment.

## Normalized domains
Face: head pose, eye openness, gaze, brows, mouth openness, mouth shapes, generic expressions.

Body: root transform, humanoid bone transforms, optional confidence.

Hands: wrist pose plus finger curl or joint pose depending on source capability.

## Source health
Each adapter reports availability, last-update timestamp, source rate, stale state, confidence where available, and error state.

## Mixing
A character can route sources by body region.

```text
Face  = ARKit
Eyes  = ARKit
Body  = VMC
Hands = MediaPipe
```

Routing policy is explicit and persisted.

## Timing
Source timestamps are preserved where practical. Interpolation/extrapolation policy belongs to mixer/runtime logic, not source adapters.
