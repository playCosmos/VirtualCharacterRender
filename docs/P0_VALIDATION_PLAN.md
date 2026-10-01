# P0 Validation Plan

## Baseline stack

P0 uses the following pinned baseline:

- Unity Editor: 6000.3.25f1 (Unity 6.3 LTS)
- URP: 17.3.x, editor-matched core package
- UniVRM: 0.131.2
- MediaPipeUnityPlugin: 0.16.3
- MediaPipe runtime in plugin: 0.10.22
- macOS minimum reference: Apple Silicon M1
- Windows hardware reference: TBD

## Purpose

P0 validates the selected architecture before feature development expands.

Passing P0 means the stack can render one VRM character, track one performer, recover from normal source failures, and sustain the defined baseline frame-rate targets without forcing architecture changes.

## Validation order

### V0 — Project/bootstrap

PASS when:

- Unity 6000.3.25f1 opens the project without package-resolution errors
- URP initializes
- UniVRM 0.x and 1.0 packages resolve
- MediaPipe native plugin loads on macOS ARM64
- MediaPipe native plugin loads on Windows x86-64
- development build can be produced for both targets

### V1 — VRM/runtime

PASS when:

- one VRM 0.x model loads
- one VRM 1.0 model loads
- MToon/default materials render correctly
- expressions can be set at runtime
- spring-bone runtime survives repeated load/unload
- source model assets are not destructively modified

### V2 — Rendering/output

Validate independently at 720p60 and 1080p60.

PASS when:

- true alpha path is demonstrated
- OBS-compatible capture is demonstrated
- resize/high-DPI behavior is stable
- macOS Retina path is stable
- Windows high-DPI path is stable
- no persistent frame-time regression occurs with transparent output enabled

### V3 — Webcam tracking

Baseline path:

Webcam -> shared capture ->
- FaceLandmarker LIVE_STREAM -> face/head normalized tracking
- HolisticLandmarker LIVE_STREAM -> hands/upper-body normalized tracking

PASS when:

- face/head results are produced independently of pose
- hands/upper-body results are produced by the Holistic path
- Unity render loop is not blocked by inference
- stale camera frames do not accumulate
- callback/thread handoff is safe
- eye and mouth response are usable
- brief face loss does not crash the native plugin
- SubjectLost/SubjectRestored behavior is stable

### V3B — Normalized tracking to VRM

PASS when:

- MediaPipe callback data is converted to engine-independent normalized payloads
- normalized coordinates use +X right, +Y up, +Z forward consistently
- no MediaPipe result/list types cross into character code
- one VRM receives head rotation from normalized face state
- blink and eye-look expressions visibly follow normalized coefficients
- provisional mouth mapping drives available VRM vowel presets without exceptions
- upper-body calibration completes from shoulders/elbows/wrists/hips
- torso and upper/lower arms respond through the UniVRM ControlRig when present
- raw humanoid-bone fallback works when no ControlRig is available
- missing VRM expression presets do not fail the frame
- normalized hand joint payloads are produced even though finger retargeting is deferred

### V4 — ARKit mixed tracking

PASS when:

- ARKit-compatible face/head data reaches normalized tracking state
- face/head ownership can switch between ARKit and MediaPipe without character reload
- hands/upper body continue from webcam while ARKit owns face/head
- loss of one source does not incorrectly emit SubjectLost while another source still sees the performer

### V5 — Performance

Reference workload:

```text
1 VRM character
1 camera
1 lightweight environment
MToon/default material path
MediaPipe FaceLandmarker + Holistic webcam tracking
face/head/hands/upper body active
transparent output
no full-body tracking
no heavy post effects
no advanced event graph workload
```

Minimum gate:

- 1280x720 @ 60 FPS sustained on validated baseline

Recommended target:

- 1920x1080 @ 60 FPS

Frame-time targets:

- P95 <= 16.67 ms
- P99 <= 25 ms

macOS:

- validate first on Apple Silicon M1

Windows:

- record CPU/GPU/RAM and results
- do not define minimum/recommended hardware until enough measurements exist

### V6 — Failure recovery

Test:

- webcam disconnect/reconnect
- ARKit sender disconnect/reconnect
- model reload
- shader fallback
- MediaPipe subject loss/reacquisition
- sleep/wake where practical
- repeated scene/environment reload

Release-blocking failures include crash, unrecovered native-device state, runaway allocation, or continuously growing memory.

## Evidence

Every validation run records:

- OS and version
- CPU/GPU/RAM
- Unity editor/player version
- package versions
- build backend
- resolution
- average/P95/P99 frame time
- tracking update rate
- capture-to-result latency where measurable
- memory
- errors/crashes
- PASS/FAIL with notes

## Rule

Do not optimize by increasing architectural complexity until a measured P0 failure justifies it.
