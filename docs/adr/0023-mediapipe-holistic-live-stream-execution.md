# ADR-0023: MediaPipe Holistic live-stream execution in Unity

- Status: Superseded
- Date: 2026-10-01
- Superseded by: ADR-0025

## Context

The built-in webcam tracker must provide face, head, hands, and upper-body tracking for one performer while keeping implementation complexity and recurring CPU cost low.

Running independent FaceLandmarker, HandLandmarker, and PoseLandmarker tasks would require multiple task lifecycles, callbacks, scheduling paths, and native/API crossings for the same camera frame.

The selected Unity integration also needs to support macOS Apple Silicon and Windows without introducing a separate tracking process.

## Decision

Use the MediaPipe Holistic Landmarker as the default webcam inference path inside Unity.

Initial integration:

- homuler MediaPipeUnityPlugin
- MediaPipe Tasks API
- HolisticLandmarker
- LIVE_STREAM/asynchronous execution
- one performer
- segmentation output disabled by default
- face blendshapes enabled when the webcam face path is in use
- one shared webcam capture/preprocessing path
- results converted immediately into the project normalized tracking state

Do not implement three independent Face/Hand/Pose task loops as the initial production path.

Do not introduce a separate Python/desktop tracking process for the baseline implementation.

## Why this path

HolisticLandmarker produces face, pose, left-hand, and right-hand landmarks from one integrated task and can optionally output face blendshapes.

Using one integrated task minimizes:

- Unity-side task orchestration
- duplicated camera-frame handling
- repeated managed/native crossings
- duplicated state/error handling
- synchronization complexity between face, hands, and pose results

LIVE_STREAM mode is used so tracking does not block the render loop and may drop stale input frames rather than building latency.

## ARKit mode

ARKit remains the preferred face/eyes/mouth/head source when healthy.

For the initial implementation, the webcam Holistic task may continue running for hands/upper body while its face result is ignored when ARKit owns those regions.

This intentionally accepts some redundant face inference in exchange for a much simpler and more reliable execution path.

Only if P0/P3 profiling shows that redundant Holistic face work materially prevents the performance target should a specialized ARKit mode be introduced using separate hand/pose inference or a custom graph.

## Desktop inference

The initial desktop baseline uses the supported prebuilt/native plugin path and does not depend on desktop GPU inference.

CPU inference cost is therefore part of the P0 performance budget, especially on the macOS M1 minimum reference.

A custom native build, custom MediaPipe graph, or another delegate is an optimization path only if profiling demonstrates a requirement.

## Frame policy

Prefer freshness over processing every camera frame.

Rules:

- render loop remains independent at 60 FPS
- tracking runs asynchronously
- old camera frames are not queued deeply
- latest useful result is timestamped and consumed by the mixer
- interpolation/smoothing bridges tracking updates to render rate

Exact camera/tracking rates are determined by P0 profiling.

## Failure containment

The tracking adapter owns the native plugin/task lifecycle.

Native/plugin failures must be surfaced through tracking health and diagnostics.

If HolisticLandmarker proves unstable on a supported desktop target, first fallback is an integrated MediaPipe graph/native path behind the same adapter, not a redesign of normalized tracking contracts.

## Validation

On macOS M1 and Windows test hardware:

- face/eye/mouth quality
- hand quality
- upper-body quality
- low-light behavior
- tracking update rate
- capture-to-result latency
- CPU cost
- memory behavior
- render-loop impact
- frame-drop behavior under inference load
- SubjectLost/SubjectRestored stability
- ARKit + Holistic mixed-source behavior

Compare the integrated Holistic path against separate task execution only if the integrated path fails a quality or performance gate.

## Consequences

- lowest initial Unity tracking implementation complexity
- one webcam inference lifecycle instead of three
- easier timestamp/result synchronization
- possible redundant face compute while ARKit is active
- desktop CPU inference must be explicitly profiled
- future optimizations remain internal to the tracking adapter

## Revisit conditions

Revisit the execution model only if measured evidence shows one of the following:

- HolisticLandmarker cannot meet baseline quality
- native/plugin stability is unacceptable
- redundant ARKit-mode face processing materially breaks the M1/Windows performance target
- a custom integrated graph provides a significant measured improvement worth the maintenance cost
