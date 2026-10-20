# ADR-0028: iFacialMocap/FaceMotion3D UDP as the first ARKit compatibility transport

- Status: Accepted
- Date: 2026-10-01

## Context

ADR-0022 selects ARKit-compatible mobile face tracking as the preferred face/head source but intentionally did not choose a transport.

P0 needs a real iPhone/iPad-to-desktop path that can be exercised on both Windows and macOS without first building and maintaining a separate iOS companion application.

iFacialMocap publishes a third-party communication specification for direct UDP/TCP streaming. Its UDP mode sends blendshape coefficients plus head/eye transforms and can be requested directly by a desktop client. FaceMotion3D uses a compatible v2 representation where the blendshape name and value are separated by `&`, avoiding ambiguity when values may be negative.

## Decision

Implement the first P0 ARKit compatibility adapter against the documented iFacialMocap/FaceMotion3D UDP format.

The implementation is layered:

```text
UDP transport
    ↓
iFacialMocap/FaceMotion3D parser
    ↓
ArKitFaceSource
    ↓
NormalizedFaceState
    ↓
PriorityTrackingRouter
```

The parser and `ArKitFaceSource` remain Unity-independent. Socket lifecycle and head Euler conversion are isolated in the Unity transport adapter.

This transport is a compatibility path, not the permanent definition of ARKit input.

## UDP behavior

Initial P0 behavior follows the published protocol:

- desktop sends the iFacialMocap start command to the iOS device
- remote port default: 49983
- desktop receive port default: 49983
- request `sendDataVersion=v2`
- use the latest packet rather than queuing old packets
- network receive and text parsing run off the Unity main thread

The receiver periodically retries the handshake while no fresh source is available.

## Blendshape conversion

The public protocol reports blendshape values in the 0..100 convention.

The adapter converts them to the normalized 0..1 face coefficient domain.

Known `_L` / `_R` aliases are mapped to the canonical `Left` / `Right` coefficient names without changing the normalized tracking contract.

Values outside 0..100 are clamped at the normalized boundary.

## Head conversion

The stream reports head Euler angles in degrees.

P0 exposes axis-sign conversion as configuration because the desktop/avatar coordinate interpretation must be validated physically across the supported apps and platforms.

The initial compatibility profile uses:

```text
Euler sign: X=-1, Y=-1, Z=+1
```

This is a test starting point, not a product-level calibration guarantee.

Head translation is disabled by default until its scale/axis behavior is validated. Head rotation and facial coefficients are the P0 requirement.

## Presence limitation

The documented packet format does not expose a separate explicit face-tracked validity flag.

Therefore this compatibility receiver treats a successfully parsed fresh packet as subject evidence and can reliably distinguish packet/source loss, but it must not claim that an all-zero facial packet means the performer is absent.

A future native companion transport should include explicit tracking validity and source timestamps.

## Routing

The preferred face provider is ARKit.

When it is stably healthy/present:

- router selects ARKit face/head
- MediaPipe FaceLandmarker is stopped
- MediaPipe Holistic continues for hands/upper body

When ARKit becomes unavailable:

- MediaPipe FaceLandmarker is restarted
- router falls back to MediaPipe face/head
- character target detects the source-id change and recalibrates that region

## Security and scope

P0 accepts UDP only from the configured local-network workflow. The receiver is not an Internet-facing service and does not provide authentication.

Network hardening, discovery, encryption, and a first-party iOS companion are separate later decisions.

## Validation

Validate on Windows and macOS:

- parser mapping including left/right aliases and tongueOut
- UDP bind/handshake
- packet reception at practical streaming rate
- head direction on all axes
- blink/eye/mouth coefficients
- ARKit priority over MediaPipe face
- MediaPipe face CPU work stops while ARKit is active
- automatic fallback after ARKit packet loss
- source switch does not require VRM reload
- source switch does not produce a large one-frame head snap

## Sources

- iFacialMocap communication specification: https://www.ifacialmocap.com/for-developer/
- Apple ARKit face blend shapes: https://developer.apple.com/documentation/arkit/arfaceanchor/blendshapes

## Revisit conditions

Supersede this ADR if:

- a first-party iOS companion becomes the primary transport
- VMC/OSC provides a better standardized ARKit path
- iFacialMocap/FaceMotion3D changes or removes the documented transport
- physical validation shows the compatibility profile is unsuitable
