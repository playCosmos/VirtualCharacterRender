# P0 Validation Plan

## Baseline stack

P0 uses the following pinned baseline:

- Unity Editor: 6000.3.25f1 (Unity 6.3 LTS)
- URP: 17.3.x, editor-matched core package
- UniVRM: 0.131.2
- MediaPipeUnityPlugin: 0.16.3
- MediaPipe runtime in plugin: 0.10.22
- UniWindowController: 0.9.8, pinned by Git tag `v0.9.8`
- macOS minimum reference: Apple Silicon M1
- Windows hardware reference: TBD

## Purpose

P0 validates the selected architecture before feature development expands.

Passing P0 means the stack can render one VRM character, track one performer, recover from normal source failures, and sustain the defined baseline frame-rate targets without forcing architecture changes.

## Source-free static suite

Run:

```text
VCR > P0 > Run All Source-Free Checks
```

The suite currently covers:

- package/version pins
- tracking presence resolver
- iFacialMocap parser
- OSC/VMC codec
- diagnostics percentile math
- material override/fallback
- normalized event bus
- environment state runtime
- lazy capability lifecycle

This suite can prove contract/code-path invariants but cannot replace standalone player, camera/iPhone, OBS, GPU, or frame-time evidence.

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

Static setup:

```text
VCR > P0 > Create Runtime Test Scene
VCR > P0 > Validate Transparent Output Baseline
```

The generated runtime scene includes:

- transparent main camera
- `DesktopRenderBootstrap`
- `UniWinCOverlayOutput`
- `P0AlphaTestPattern`
- `P0RuntimeDiagnostics`

Static PASS requires:

- UniWindowController 0.9.8 resolves from the pinned `v0.9.8` UPM tag
- URP Alpha Processing enabled
- SDR/HDR-off camera baseline with clear alpha 0
- Windows explicit D3D11
- Windows D3D11 flip-model swapchain disabled
- automatic opacity hit testing disabled

Then validate standalone players independently at 720p60 and 1080p60.

Windows PASS when:

- true alpha is demonstrated in the desktop window
- the alpha test pattern shows correct opaque/50%/25%/overlap behavior
- VRM hair/outline edges have no unacceptable black/white fringe
- OBS Game Capture with transparency enabled preserves alpha
- topmost and explicit click-through work
- resize/high-DPI and multi-monitor behavior are stable
- no persistent frame-time regression violates the P0 frame budget

macOS M1+ PASS when:

- true alpha is demonstrated in the desktop window
- the alpha test pattern shows correct opaque/50%/25%/overlap behavior
- VRM hair/outline edges have no unacceptable fringe
- OBS macOS window/screen capture workflow is usable
- topmost and explicit click-through work
- Retina scaling, resize, and multi-monitor behavior are stable
- sleep/wake and relaunch do not leave the window adapter broken
- no persistent frame-time regression violates the P0 frame budget

ADR-0007 remains Proposed until both platform gates pass.

### V2B — Material/custom shader override

Source-free gate:

```text
VCR > P0 > Validate Material Override Runtime
```

PASS when:

- the source material object is not modified
- a valid precompiled shader resolves by shader ID
- the renderer receives a runtime material clone
- runtime parameter setters affect only the clone
- an unknown shader ID does not throw out of the material subsystem
- invalid/unsupported shader resolution restores the original source material reference
- fallback/error counters are reported through diagnostics

External custom-shader gate:

- build one trivial custom URP shader for Windows and macOS
- package each target in its own AssetBundle
- load/register through `RuntimeShaderBundleLoader`
- apply the registered shader by ID to one VRM material slot
- verify reload/clear restores the imported source material
- verify missing/incorrect-platform bundle fails without breaking the VRM
- verify required shader variants survive the build/content pipeline
- measure enabled override cost; disabled material-package loading has no recurring frame cost

ADR-0029 defines the P0 execution model. Raw HLSL source compilation is not a runtime requirement.

### V2C — Environment, normalized events, and lazy capabilities

Source-free gates:

```text
VCR > P0 > Validate Environment Runtime
VCR > P0 > Validate Normalized Event Runtime
VCR > P0 > Validate Lazy Capability Registry
```

PASS when:

- an environment state changes without scene reload
- static/event-driven basic environment control has no per-frame `Update()`
- the runtime test scene contains an explicit Environment root and environment-owned light
- tracking disappearance is named/emitted as `tracking.subject_lost`, not AFK
- subject loss/restoration remains distinct from tracking source loss/restoration
- normalized event dispatch assigns monotonic sequence order
- donation payload can carry actor/text/amount/currency without service-specific runtime types
- the Unity ingress event queue is bounded and dispatch-limited per frame
- capability registration does not instantiate its service
- enable instantiates, disable disposes, and re-enable recreates the service
- the capability registry itself has no recurring frame update loop

### V3 — Webcam tracking

Source-free presence gate:

- `VCR > P0 > Validate Presence Resolver` passes
- mixed-domain check proves overall performer presence can remain Present from full-body evidence while face evidence is independently false
- source-loss and subject-loss transitions remain distinct

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
- a fresh no-subject callback is distinguished from a stale/dead callback stream
- brief loss is held through configurable grace rather than instantly snapping to neutral
- stable subject loss emits one SubjectLost transition and returns tracking-driven pose/expression toward neutral
- stable restoration emits one SubjectRestored transition and recalibrates without a large pose snap
- source disconnect produces TrackingSourceLost semantics without falsely declaring performer absence
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

Initial compatibility transport: iFacialMocap/FaceMotion3D UDP v2.

PASS when:

- the built-in parser maps v2 `_L`/`_R`, jaw, tongueOut, and head fields correctly
- desktop handshake to the configured iOS IPv4 address/port succeeds
- ARKit-compatible face/head data reaches normalized tracking state
- physical head pitch/yaw/roll directions are verified; provisional axis signs are corrected if needed
- blink, gaze, jaw/mouth, brow, and asymmetric coefficients are checked against actual performer motion
- ARKit face becomes the preferred routed face source after stable reception
- MediaPipe FaceLandmarker stops while ARKit owns face/head
- Holistic hands/upper body continue while ARKit owns face/head
- ARKit packet loss restarts MediaPipe face automatically
- source-id change causes regional recalibration without character reload
- source switching does not produce a large one-frame head snap
- loss of one source does not incorrectly emit SubjectLost while another source still sees the performer
- the compatibility path does not claim subject absence from an all-zero packet because the protocol lacks explicit tracking-valid state

### V4B — VMC interoperability

Static/self-test gate:

- `VCR > P0 > Validate OSC and VMC Codec` passes
- OSC message and bundle parsing handles VMC P0 payloads
- malformed/unsupported packet types are rejected without exception escape
- VMC VRM0 expression names map to normalized standard expressions
- normalized humanoid local pose carries root and bone transforms

External receive gate:

- receive from at least one external VMC Performer
- default `127.0.0.1:39539` path works
- explicit LAN sender IPv4 filtering works
- `/OK` and stale packets produce stable source-health transitions
- VMC default `OriginalLocal` bone rotations are converted to ControlRig normalized rotations using target initial posture data
- the explicit `NormalizedLocal` compatibility option bypasses posture conversion without double-conversion
- hips/spine/arms/legs/fingers are inspected for direction, starting-pose fidelity, and retarget continuity
- optional root position/rotation policy is tested separately
- face/head source priority remains ARKit > MediaPipe when VMC full body is active
- webcam upper-body mapping does not fight active VMC full body
- full-body source loss returns its pose/expression contribution toward neutral/reference state instead of freezing the last pose

External send gate:

- `Vrm10MotionSnapshotProvider` samples original/non-ControlRig humanoid bones
- send to at least one external VMC Marionette
- `/Root/Pos`, `/Bone/Pos`, `/Blend/Val`, `/Blend/Apply` are accepted
- default VRM0 expression vocabulary works with a VRM1 runtime
- optional VRM1 expression-name mode is verified separately
- sender frame rate can be reduced without queue buildup
- same-process loopback sender -> receiver on the same port is rejected
- bidirectional local testing uses distinct ports and does not create a feedback loop

ADR-0006 remains Proposed until these external interoperability checks pass.

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

The generated P0 tracking scene includes `P0RuntimeDiagnostics`.

Source-free diagnostics gate:

- `VCR > P0 > Validate Diagnostics Math` passes
- deterministic 1..100 ms sample produces average 50.5 ms, P95 95 ms, and P99 99 ms

Record at minimum:

- frame average/P95/P99 over the rolling window
- face/body/full-body/expression normalized update rates
- normalized snapshot source-to-consumer age where monotonic timestamps are available
- MediaPipe FaceLandmarker submit-to-callback latency
- MediaPipe Holistic submit-to-callback latency
- VMC receive packet/malformed counts when enabled
- VMC send packet/error counts when enabled

Evidence/Development players automatically write `vcr-p0-system.txt` and `vcr-p0-diagnostics.csv` to `Application.persistentDataPath`.

Performance players do not automatically write CSV so periodic file I/O does not contaminate frame-time measurements. Use console/profiler capture for the performance run and compare it with the separate evidence run.

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
