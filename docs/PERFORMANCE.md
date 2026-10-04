# Performance

## Product performance target

VirtualCharacterRender is optimized for one active 3D character.

The runtime must defend 60 FPS when the host PC is otherwise not under meaningful load, using the validated baseline configuration.

## Reference hardware status

### macOS

Minimum reference hardware class:

- Apple Silicon
- M1 or newer

M1 is the current minimum macOS performance-validation floor.

This does not mean every M1 configuration guarantees every optional advanced feature at 1080p60. Feature-specific guarantees remain tied to the validated baseline workload.

### Windows

Minimum/recommended Windows hardware is currently **TBD**.

Do not publish a Windows CPU/GPU minimum until P0 measurements establish a defensible baseline.

Windows hardware tiers will be defined from measured 720p60/1080p60 behavior rather than guessed specifications.

## Resolution tiers

### Minimum supported target

- 1280×720
- 60 FPS
- transparent overlay
- one character
- built-in basic tracking enabled
- default/MToon-class material path
- normal broadcast UI/services

This tier is the minimum release support target.

### Recommended target

- 1920×1080
- 60 FPS
- same baseline feature set

1080p60 is the primary optimization and validation target.

### Higher resolutions

1440p, 4K, ultrawide, and custom resolutions are allowed.

They are supported as configurable output modes, but performance guarantees are documented separately by platform/hardware class and are not implied by the 720p60/1080p60 targets.

## Frame-time gates

At 60 FPS the nominal frame budget is 16.67 ms.

For validated baseline sessions:

- average frame rate: 60 FPS target
- P95 frame time: <= 16.67 ms target
- P99 frame time: <= 25 ms target
- sustained frame drops must be diagnosable by subsystem
- startup/transitions may have separate documented transient budgets

Average FPS alone is not a release gate.

## Baseline session

Performance measurements use an explicitly versioned reference workload:

```text
1 active VRM character
1 primary camera
1 environment
MediaPipe webcam tracking
basic face/head/hand/upper-body tracking
optional ARKit face/head source
basic expressions/motion
MToon/default material path
transparent overlay
no heavy optional post effects
no advanced event graph workload
no full-body tracking
```

A reference model/content package and platform test machines must be recorded before performance claims are treated as release guarantees.

## Tracking latency targets

Initial engineering targets:

- tracking adapter -> normalized state: <= 30 ms P95 where source permits
- local VMC -> runtime: <= 30 ms P95
- mobile ARKit-compatible LAN input -> runtime: <= 70 ms P95
- webcam capture -> visible character response: <= 80 ms target

These are engineering targets subject to P0 hardware/source validation.

## Runtime cost attribution

Diagnostics must attribute at least:

- render CPU time
- render GPU time
- tracking/inference time
- motion/expression mixer time
- shader/post-processing cost
- environment cost
- event/protocol cost
- memory use
- render-target/texture allocation
- active capabilities/services
- MediaPipe Face/Holistic GPU→CPU readback count and latest readback wait time when webcam tracking is active

The desktop MediaPipe baseline currently uses separate LIVE_STREAM Face and Holistic submissions. The optional low-light preprocessor already caches its processed texture once per Unity frame, but CPU async readback and MediaPipe `Image` creation remain task-specific. Do not share one `Image` between both native tasks until the pinned plugin's ownership/lifetime contract is proven under concurrent LIVE_STREAM use. Use `tracking.mediapipe.face.readbacks`, `tracking.mediapipe.face.readback_wait`, `tracking.mediapipe.holistic.readbacks`, and `tracking.mediapipe.holistic.readback_wait` to measure this cost before changing the capture topology.

## Managed-allocation gate

The 60 FPS baseline is also a managed-GC target, not only a CPU/GPU frame-time target.

For steady-state tracking and UI operation:

- expression custom-channel merge scratch storage must be reused; do not reintroduce per-frame `Dictionary`, `HashSet`, or `List` construction in the mixer hot path,
- immutable output snapshots may allocate when a genuinely new tracking state is published, but temporary merge containers are not part of that allowance,
- UI refresh must reuse cached navigation labels/components and must not allocate a full section snapshot on every refresh tick,
- missing optional dependencies may trigger bounded discovery retries, not an unbounded per-frame `FindObjectsByType` scan,
- Profiler evidence for 720p60 and 1080p60 must record GC.Alloc/frame and GC spikes alongside frame time before release claims are accepted.

A temporary allocation that is necessary for an immutable published frame is evaluated separately from avoidable scratch allocation. Do not trade correctness or frame immutability for unsafe pooling without ownership/lifetime evidence.

## Disabled capability rule

A disabled optional capability should create no meaningful recurring frame cost.

Examples:

- no camera inference when webcam tracking is disabled
- no full-body solver when full tracking is disabled
- no protocol polling/listener if not enabled
- no event graph evaluation if no graph is active
- no heavy post-processing allocation when unused

## Stability gates

Candidate release soak tests should include:

- 1 hour normal interactive session
- 8 hour extended session
- repeated tracker disconnect/reconnect
- repeated model/environment reload
- shader failure and fallback
- sleep/wake or display changes where platform behavior permits

Crash, runaway allocation, unrecovered device loss, and continuously increasing memory are release blockers until characterized.

## Performance policy

New features must report:

1. disabled recurring cost
2. enabled incremental CPU cost
3. enabled incremental GPU cost
4. memory/VRAM delta
5. latency impact where applicable

Performance regression is an architectural concern, not a final optimization phase.
