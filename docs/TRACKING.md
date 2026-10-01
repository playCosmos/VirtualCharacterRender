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
ADR-0025 defines the active Unity/MediaPipe execution strategy. ADR-0026 defines normalized payloads and coordinates. ADR-0027 defines the current UniVRM target path. ADR-0028 defines the first ARKit compatibility transport. ADR-0023 is superseded.

Primary webcam baseline:

```text
Webcam
  ├─ FaceLandmarker (higher priority/rate)
  │    └─ face / eyes / mouth / head
  └─ HolisticLandmarker (lower configurable rate)
       └─ hands / upper body

Both use LIVE_STREAM asynchronous execution.
```

Preferred mixed configuration:

```text
Face / Eyes / Mouth / Head = ARKit
Hands / Upper Body         = MediaPipe Holistic webcam path
```

Fallback without ARKit:

```text
Face / Eyes / Mouth / Head = MediaPipe FaceLandmarker
Hands / Upper Body         = MediaPipe HolisticLandmarker
```

Do not continuously average ARKit and MediaPipe face data by default. Route by region and source health, then transition smoothly on source changes.

## Unity execution model

Initial implementation uses:

- homuler MediaPipeUnityPlugin
- MediaPipe Tasks API
- FaceLandmarker for face/head
- HolisticLandmarker for hands/upper body
- LIVE_STREAM/asynchronous execution
- one shared webcam source
- independent small input pools
- one performer
- segmentation disabled

Face tracking is independent of pose detection. When ARKit owns face/head, the MediaPipe FaceLandmarker can stop entirely while Holistic continues for hands/upper body.

Do not start with separate HandLandmarker and PoseLandmarker loops. Split those only if M1/Windows profiling or hand-quality testing justifies the added complexity.

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

Source adapters may consume their own single-slot callback buffers, but `ITrackingFrameProvider` exposes the latest normalized frames non-destructively. Multiple consumers must use sequence/timestamp values rather than removing frames from each other.

## Apple mobile face tracking

Support an iPhone/iPad ARKit-compatible facial tracking source.

Internally this is ARKit face-tracking data rather than Face ID authentication.

Use it as the preferred source for high-quality face/head/expression input, especially eyes and mouth, when healthy.

### P0 ARKit compatibility transport

The first desktop compatibility adapter supports the documented iFacialMocap/FaceMotion3D UDP v2 stream:

```text
iPhone/iPad ARKit
   ↓
iFacialMocap / FaceMotion3D public stream
   ↓ UDP v2
IFacialMocapUdpReceiver
   ↓
ArKitFaceSource
   ↓
NormalizedFaceState
```

The desktop sends the v2 start request to port 49983 and receives the blendshape/head stream on the configured local port. Network receive and text parsing run off the Unity main thread.

The public packet format does not expose a dedicated tracking-valid flag. A fresh parsed packet is treated as ARKit subject evidence; packet staleness is treated as source loss. Do not infer subject absence from an all-zero packet.

Head Euler-axis signs are configurable. The current `(-X, -Y, +Z)` profile and disabled head translation are P0 test defaults pending physical validation.

## Source switching

Priority for face/head:

1. ARKit when healthy
2. MediaPipe webcam fallback
3. configured neutral/audio fallback

Switching sources must not require character reload.

A short transition/cross-fade should avoid visible pose snapping.

The P0 `PriorityTrackingRouter` implements this region priority. While ARKit face is stably present it stops the MediaPipe FaceLandmarker through `IFaceTrackingActivationControl`; Holistic hands/upper body remain active. When ARKit is unavailable, MediaPipe face inference restarts automatically.

Frames carry `SourceId`, allowing the VRM target to recalibrate only the region whose source changed.

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

The P0 implementation now uses `TrackingPresenceResolver` and `ITrackingPresenceProvider`.

It distinguishes:

```text
fresh callback + no performer -> subject absence candidate
no fresh callback            -> source unavailable candidate
```

Current configurable P0 starting values are 0.50 s lost grace, 0.15 s restore stability, and 1.00 s callback stale timeout. These are not final product thresholds.

During the grace period the character holds the last valid tracking state. On stable loss/source unavailability, the P0 VRM target discards stale payloads and smoothly returns tracking-driven head, expressions, torso, and arms toward neutral/reference state. Restoration recalibrates on fresh stable frames.

Region evidence is preserved independently from overall performer presence. A VMC full-body source may keep the performer globally `Present` while face evidence is lost; in that case face/head returns toward neutral while VMC body motion continues. The same rule applies to webcam upper-body evidence independently of face evidence.

## Full-body tracking

Full-body tracking is a separate optional capability.

The P0 VMC path is now:

```text
VMC OSC/UDP
  ↓
VmcTrackingSource
  ↓
HumanoidPoseState
  ↓
PriorityTrackingRouter external-pose slot
  ↓
Vrm10HumanoidPoseTarget
```

`HumanoidPoseState` carries model-root transform plus local transforms for the Unity/VMC humanoid bone-name set and an explicit `HumanoidPoseSpace`. VMC defaults to `OriginalLocal`; normalized ControlRig senders must opt into `NormalizedLocal`. `NormalizedExpressionState` separately carries standard expressions and arbitrary custom expression name/value pairs.

The P0 VRM full-body target converts VMC original humanoid local rotations into UniVRM ControlRig normalized local rotations using the target model's initial `BoneInitialRotation` data. This preserves the sender's current pose instead of discarding the first received pose as a calibration baseline. Root translation/rotation remain separate opt-in policies.

Default routing behavior:

- ARKit/MediaPipe still owns face/head priority
- VMC can own optional full-body pose
- while VMC full body is available, the webcam upper-body mapper is suspended
- the VMC target skips head/eye/jaw bones by default so the selected face source can override them
- when VMC becomes unavailable, webcam upper-body mapping can resume
- when the VMC full-body source becomes unavailable or loses performer evidence, its pose/expression contribution returns smoothly toward the target neutral/reference state instead of freezing the last VMC pose

Other future full-body sources may include:

- SteamVR-class trackers
- mocopi-class devices
- camera-based full-body solutions
- future plugins/adapters

Full-body solvers, leg/floor calibration, root policy, and advanced IK do not belong to the baseline performance budget unless explicitly enabled.

## Normalized domains

Coordinates are source-neutral and engine independent:

```text
+X = performer right
+Y = up
+Z = forward
```

MediaPipe world coordinates are converted as `(x, y, z) -> (x, -y, -z)`.

Face:

- normalized head rotation and position when available
- fixed MediaPipe/ARKit-compatible semantic coefficient set
- eye blink/look channels
- brow channels
- jaw/mouth channels
- cheek/nose channels

Body:

- P0 upper-body joints: nose, shoulders, elbows, wrists, hips
- metric/world positions where the source provides them
- confidence per joint when available

Hands:

- left/right 21-joint topology
- metric/world positions where available
- confidence per joint when available

Humanoid pose:

- model root position/rotation
- local position/rotation per canonical humanoid bone
- used by VMC and future full-body adapters

Expressions:

- standard neutral/emotion/vowel/blink/look channels
- custom named expression values
- independent from face-landmark coefficients

MediaPipe callback-owned result buffers are never retained outside the adapter. The adapter copies them into immutable normalized payloads before returning from the callback.

## Current VRM target

`Vrm10TrackingTarget` consumes only normalized tracking data.

- UniVRM runtime ControlRig bones are preferred when present.
- raw humanoid bones are fallback only.
- first valid face frame establishes neutral head orientation.
- first valid upper-body frame establishes torso/arm reference directions.
- blink and eye look map to VRM standard expression presets.
- mouth coefficients use provisional P0 geometric-to-phoneme heuristics.
- normalized hand data is preserved, but finger retargeting is intentionally deferred.

The P0 relative-direction upper-body solver is a feasibility mapper, not the final IK/full-body solver.

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
