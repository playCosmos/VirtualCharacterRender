# ADR-0026: Engine-independent normalized tracking payloads

- Status: Accepted
- Date: 2026-10-01

## Context

Tracking sources must remain replaceable without coupling MediaPipe, ARKit, VMC, or Unity-specific data structures to character/render code.

P0 now needs concrete face, hand, and upper-body payloads rather than only region-validity flags.

## Decision

Tracking adapters convert source-owned callback data immediately into immutable source-neutral payloads carried by `TrackingFrame`.

The normalized coordinate convention is:

- +X = performer right
- +Y = up
- +Z = forward

The tracking domain must not expose Unity vector/quaternion types.

### Face

`NormalizedFaceState` contains:

- normalized head rotation
- normalized head position when available
- a fixed semantic facial-coefficient set compatible with the MediaPipe/ARKit-style coefficient names

The coefficient set includes eye blink/gaze, brows, jaw, mouth, cheek, and nose channels.

### Upper body

`NormalizedUpperBodyState` initially contains the P0 joints required for upper-body retargeting:

- nose
- left/right shoulder
- left/right elbow
- left/right wrist
- left/right hip

Positions use world/metric source data when available and preserve per-joint confidence.

### Hands

`NormalizedHandState` contains the standard 21-joint hand topology for each hand.

Detailed finger retargeting is not part of the initial VRM P0 mapper, but the data contract is preserved now so a later solver does not require a source-layer redesign.

## MediaPipe conversion

MediaPipe real-world coordinates are converted from:

- +X right
- +Y down
- +Z back

to VCR normalized coordinates:

```text
(x, y, z) -> (x, -y, -z)
```

MediaPipe callback-owned lists are copied into normalized payloads before the callback returns.

No character code retains references to MediaPipe result buffers.

## Frame ownership

A source may populate only the domains it owns.

Examples:

```text
FaceLandmarker frame:
  Face + Head

Holistic frame:
  UpperBody + LeftHand + RightHand

ARKit frame:
  Face + Head
```

The routing/mixer layer combines domains later.

## Consequences

- source adapters can change without changing VRM/runtime contracts
- ARKit and MediaPipe can share face semantics
- callback buffer reuse cannot corrupt later character frames
- the tracking assembly remains engine independent
- hand data is available before finger retargeting is implemented

## Revisit conditions

Supersede this ADR only if a future source requires a semantic domain that cannot be represented without distorting the source data or causing unacceptable allocation/performance cost.
