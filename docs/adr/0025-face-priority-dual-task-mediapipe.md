# ADR-0025: Face-priority dual-task MediaPipe execution

- Status: Accepted
- Date: 2026-10-01
- Supersedes: ADR-0023

## Context

ADR-0023 selected a single HolisticLandmarker task to minimize Unity-side orchestration.

During P0 implementation, inspection of MediaPipeUnityPlugin 0.16.3 showed an important product mismatch: its C# Holistic result builder treats an empty pose-landmark packet as a failed holistic result before exposing face/hand data.

VirtualCharacterRender prioritizes face quality, especially eyes and mouth. Face tracking must therefore not depend on successful upper-body pose detection.

At the same time, three completely independent Face/Hand/Pose pipelines would add more lifecycle and scheduling complexity than necessary.

## Decision

Use two MediaPipe live-stream tasks from one webcam:

1. **FaceLandmarker**
   - primary webcam face/head source
   - one face only
   - face blendshapes enabled
   - face transformation matrix enabled
   - higher update-rate priority

2. **HolisticLandmarker**
   - hands and upper-body source
   - lower configurable update rate
   - segmentation disabled
   - face output ignored by the runtime

Both tasks use:

- MediaPipeUnityPlugin
- CPU delegate on Windows/macOS
- LIVE_STREAM asynchronous execution
- freshness-first/no-deep-queue policy

## ARKit mode

When ARKit-compatible face tracking is healthy:

- ARKit owns face/eyes/mouth/head.
- MediaPipe FaceLandmarker may be stopped entirely.
- HolisticLandmarker continues only for hands/upper body.

This avoids the redundant webcam face inference accepted by ADR-0023.

## Webcam-only mode

When ARKit is unavailable:

- FaceLandmarker owns face/eyes/mouth/head.
- HolisticLandmarker owns hands/upper body.
- normalized routing combines both sources for the one performer.

## Capture and scheduling

The tasks share one `WebCamTexture` source but maintain independent asynchronous input pumps.

Initial engineering defaults are configurable rather than product guarantees:

- face task: higher target rate
- holistic body/hand task: lower target rate
- render: 60 FPS independently

Each task keeps only a very small in-flight texture-frame pool. If its input path is busy, the task skips camera frames instead of building latency.

The exact webcam and inference rates are determined by M1/Windows P0 measurements.

## Why not three tasks initially

Separate FaceLandmarker + HandLandmarker + PoseLandmarker would remove Holistic's redundant internal face work, but increases:

- model/package management
- callbacks
- scheduler state
- frame submission paths
- source health surfaces
- synchronization/testing combinations

The two-task design is the current performance/complexity compromise.

If Holistic's unused internal face computation or pose-gated hand behavior materially blocks performance or hand quality, P0/P3 may supersede this ADR with separate Hand/Pose tasks.

## Presence

Performer presence is not taken from Holistic alone.

Presence resolution considers active face sources first:

- ARKit valid => present
- MediaPipe FaceLandmarker valid => present
- upper-body evidence may support presence
- all configured presence paths lost beyond grace => `SubjectLost`

Source/device failure remains `TrackingSourceLost`.

## Validation

On macOS M1 and Windows test hardware measure:

- FaceLandmarker face/eye/mouth stability
- face reacquisition without pose
- Holistic hand/upper-body stability
- independent callback rates
- CPU cost of each task
- camera/readback cost
- render-loop impact
- mixed ARKit + Holistic mode
- webcam-only Face + Holistic mode
- SubjectLost/SubjectRestored behavior

## Consequences

- face quality is independent of pose detection
- ARKit mode can disable webcam face inference
- hands/upper body retain a single integrated MediaPipe task
- one additional task lifecycle is accepted for better face reliability
- detailed performance attribution becomes easier because face and body/hand costs are separable

## Revisit conditions

Supersede this ADR if measured evidence shows:

- Holistic body/hand cost remains too high on M1
- Holistic hand output is too dependent on pose visibility
- separate Hand/Pose tasks materially improve performance/quality enough to justify complexity
- another tracking backend provides a clearly better supported desktop path
