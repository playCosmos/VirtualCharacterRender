# P3 Status

Updated: 2026-10-02

## Active branch

```text
feature/p3-built-in-motion-capture
```

P3 starts from the preserved P2 source checkpoint:

```text
checkpoint/p2-source-implementation
8ad9d794e2c8eb0bce9d6470dc7cfaf7b3c39cbb
```

P0 hardware validation and P1/P2 Unity execution evidence remain deferred. P3 does not reinterpret those items as PASS.

## Existing tracking foundation carried forward

- one webcam shared by FaceLandmarker and HolisticLandmarker
- independent LIVE_STREAM task rates
- bounded/drop-only frame submission
- FaceLandmarker owns face/eyes/mouth/head
- Holistic owns hands/upper body
- ARKit-compatible iFacialMocap/FaceMotion3D UDP input
- ARKit face priority / MediaPipe face fallback
- tracking-derived subject presence
- source/device loss distinct from performer disappearance
- normalized source-neutral tracking payloads

P3 does not split Holistic into separate Pose/Hand tasks because there is no measured evidence that the added complexity is justified.

## Implemented in P3

### MediaPipe webcam production lifecycle

`MediaPipeWebcamTrackingRunner` is now restartable rather than a one-shot `Start()` flow.

Lifecycle states:

- Stopped
- Starting
- Running
- Suspended
- Faulted

Behavior:

- starts from `OnEnable` in play mode
- explicit `Restart`, `Suspend`, and `Resume`
- application pause can release webcam/inference resources
- application resume recreates webcam, model sources, and frame pools
- startup timeout prevents indefinite wait for placeholder webcam dimensions
- startup failures clean up partially-created runtime resources
- disabling/destroying releases coroutines, MediaPipe sources, pools, preprocessing resources, and webcam
- FaceLandmarker can still be independently disabled when ARKit owns face tracking
- Holistic remains active while webcam face inference is disabled

### Capture diagnostics

`MediaPipeWebcamStatus` exposes:

- lifecycle state
- selected device
- actual width/height
- requested camera FPS
- face inference enabled state
- preprocessing mode
- face/holistic submitted frame counts
- face/holistic pool-drop counts
- GPU readback error count
- last runtime error

Runtime metrics include the same capture state plus MediaPipe result counts and processing latency.

### Optional low-light preprocessing

A one-pass GPU preprocessing path was added:

- mode: `Disabled` by default
- optional `LowLightGamma`
- configurable exposure and gamma
- processed texture cached once per Unity frame
- Face and Holistic share the same processed frame
- disabled mode returns the original webcam texture without render-target work
- shader is stored under `Resources` so player shader stripping does not remove the optional path

The low-light mode is implemented but must not be enabled by default until real-camera quality/performance evidence exists.

### ARKit/iFacialMocap transport diagnostics

`ArKitReceiverStatus` exposes:

- Stopped / Starting / Running / SourceLost / Faulted
- remote/local endpoint settings
- received datagrams
- valid parsed frames
- rejected sender count
- parse failure count
- handshake count
- socket error count
- last error

SourceLost remains separate from a valid no-subject frame.

### Optional audio-driven mouth fallback

New `VCR.Runtime.Tracking.AudioUnity` assembly:

- `AudioDrivenExpressionSource`
- RMS-based input level
- noise threshold
- gain
- independent attack/release smoothing
- emits normalized `Aa` expression state only
- does not implement `ITrackingPresenceProvider`
- audio activity therefore cannot become visual performer-presence evidence
- audio fallback numeric inputs are finite-normalized before runtime use: non-finite serialized threshold/gain/attack/release values restore safe defaults, and `AudioDrivenExpressionMath` itself returns finite fallback output for non-finite level/smoothing inputs so NaN cannot propagate into expression frames

The source is intentionally not wired into priority routing during P3. Routing/mixing policy belongs to P4/P5.

## Validation

Interactive:

```text
VCR > P3 > Validate Built-in Tracking Runtime
```

Batch:

```text
tools/validate-p3-source-free.ps1
tools/validate-p3-source-free.sh
```

P3 batch validation runs the inherited P0, P1, and P2 suites first.

Current P3 source-free checks cover:

- ARKit source lifecycle
- ARKit face/head evidence
- explicit no-subject frame semantics
- source-loss distinction
- MediaPipe capture-status contract
- ARKit receiver-status contract
- disabled preprocessing zero-transform path
- deterministic audio RMS calculation
- threshold/gain normalization
- independent audio attack/release smoothing

## Evidence still required

With real webcam / Apple device / Unity player environment:

- webcam permission and restart behavior
- camera suspend/resume
- webcam device loss/recovery
- FaceLandmarker eye/mouth/head quality
- Holistic hand/upper-body quality
- low-light mode A/B quality
- low-light GPU/frame-time cost
- Face 30 Hz / Holistic 15 Hz practical behavior
- capture pool-drop/readback-error behavior
- physical iPhone/iPad ARKit stream
- ARKit source loss and recovery
- ARKit -> MediaPipe face fallback transition
- MediaPipe face inference disable/re-enable while ARKit is healthy/lost
- optional audio fallback behavior with real input

## P3 source implementation status

The planned P3 source implementation is complete enough to proceed to P4.

Still unresolved items are primarily device-quality and performance evidence. Do not tune low-light exposure/gamma, confidence thresholds, Face/Holistic rates, or presence timing from synthetic assumptions.

## Next phase after P3 source checkpoint

Proceed to P4 — Tracking Abstraction and Routing:

- explicit source-health snapshots
- region-level route decisions
- hot switching
- timestamps/staleness
- route diagnostics
- expression fallback routing
- source-priority policy
- preserve subject/source loss distinction
