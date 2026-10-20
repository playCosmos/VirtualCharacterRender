# P0 Status

Updated: 2026-10-05

> Hardware-dependent P0 validation is currently deferred because the required physical test equipment is unavailable. The implementation checkpoint is preserved at `checkpoint/p0-hardware-validation-deferred` on commit `79bbbe3e0b826c5fe791abe1cd5b17d47a8d94f9`. Remaining evidence gates stay unresolved rather than being marked PASS. P1 development continues from that exact checkpoint.


## Current branch

Current integrated source of truth:

```text
develop
```

The old P0 feature/checkpoint branches are historical phase/evidence references.

Do not accept the remaining evidence-gated ADRs or merge P0 as fully validated solely from static/source-free checks.

## Implemented / source-free ready

### Unity/runtime baseline

- Unity 6000.3.25f1 / URP 17.3.x pin
- UniVRM 0.131.2 pin
- MediaPipeUnityPlugin 0.16.3 bootstrap
- UniWindowController 0.9.8 `v0.9.8` pin
- one-character runtime loader
- 720p/1080p render baseline component
- evidence/performance standalone build menus
- standalone `--vcr-vrm` real-model autoload
- optional standalone shader bundle / shader ID / material-slot arguments
- versioned runtime configuration persistence uses atomic save plus bounded strict UTF-8 load/save (4 MiB); oversized/invalid-encoding files fail closed before JSON parsing

### Tracking

- normalized face/head state
- normalized upper-body/hand state
- normalized humanoid full-body state
- normalized expression state
- MediaPipe FaceLandmarker + Holistic LIVE_STREAM runner
- ARKit-compatible iFacialMocap/FaceMotion3D UDP adapter
- ARKit face priority / MediaPipe face fallback
- VMC OSC codec, UDP receiver/sender, full-body route
- bounded OSC parsing: 16 KiB packet, 256-message, 64-argument limits with strict UTF-8 and fail-closed partial-output clearing
- VMC/ARKit UDP worker restart is generation-safe: a previous receive thread must terminate before a replacement socket/source can start
- ARKit/VMC/MediaPipe tracking-source publish/stop/dispose boundaries are synchronized so callbacks cannot publish a new frame after lifecycle shutdown
- source vs subject loss separation
- tracking disappearance/restoration events
- source-free presence/ARKit/VMC checks, including ARKit/VMC stop/dispose source lifecycle and OSC fanout bounds

### Character/runtime mapping

- UniVRM ControlRig/fallback tracking application
- VRM runtime hot load/unload path
- full-body VMC application path
- normalized final-motion snapshot for VMC output

### Rendering/output

- transparent camera baseline
- URP Alpha Processing static configuration
- Windows P0 D3D11 + BitBlt configuration
- UniWinC output adapter behind `IOverlayOutputAdapter`
- explicit click-through/topmost state
- alpha/overlap test pattern
- Windows/macOS evidence/performance build entries

### Material/custom shader

- material-slot discovery
- non-destructive runtime material clones
- generic runtime parameter setters
- source-material restore/fallback
- precompiled shader registry
- platform-specific AssetBundle shader loader
- source-free override/fallback smoke test
- ADR-0029: no in-process raw HLSL compilation

### Events

- normalized event payload and canonical event types
- bounded thread-safe ingress queue
- bounded per-frame main-thread dispatch
- tracking subject/source event adapter
- donation/chat-compatible normalized payload fields
- `tracking.subject_lost` semantics explicitly separated from AFK/inactivity timers

### Environment

- first-class environment contract
- environment state/status
- world/camera/screen/character space modes
- static/event-driven/lower-rate/every-frame update policy
- Unity basic environment root
- environment-owned P0 directional light
- no `Update()` cost in the static/event-driven basic controller

### Capability/lifecycle

- factory-based lazy capability registry
- registration without instantiation
- enable=create
- disable=dispose
- re-enable creates a new service
- disposal failure retains the owned capability instance in `Faulted` state so cleanup can be retried; registry-wide dispose also preserves retryable entries until every instance is actually released
- source-free lifecycle self-test

### Diagnostics/evidence

- rolling frame average/P95/P99
- normalized tracking update rates
- normalized snapshot age
- MediaPipe Face/Holistic submit-to-callback latency
- protocol/event/environment/material/output metrics
- Development/Evidence build automatic CSV
- Development/Evidence build automatic system-information sidecar
- Performance build without automatic CSV file I/O

## Consolidated source-free validation

Run interactively:

```text
VCR > P0 > Run All Source-Free Checks
```

Or run the same suite in Unity batch mode with process exit codes:

```text
tools/validate-p0-source-free.ps1
tools/validate-p0-source-free.sh
```

Run the MediaPipe bootstrap first so the pinned local package can resolve.

Current suite:

1. package/version pins
2. presence resolver
3. iFacialMocap parser
4. OSC/VMC codec
5. diagnostics math
6. runtime configuration persistence bounds / atomic-save preservation
7. material override/fallback
8. normalized events
9. environment state
10. lazy capability lifecycle

Source-free PASS is necessary but not sufficient for P0 completion.

## Execution evidence still required

### Project compile/package resolution

On Unity 6000.3.25f1:

- open the project with zero compile errors
- resolve all pinned packages
- run the source-free suite
- create the P0 runtime scene
- produce standalone builds

### UniVRM — ADR-0003 remains Proposed

Required:

- real VRM 0.x runtime load
- real VRM 1.0 runtime load
- MToon rendering
- expressions
- spring bones
- repeated load/unload
- material override on real VRM
- Windows/macOS behavior

### Transparent output — ADR-0007 remains Proposed

Required independently:

Windows:

- actual alpha desktop window
- alpha edge/overlap pattern
- VRM hair/outline edge check
- OBS Game Capture + transparency
- resize/multi-monitor/topmost/click-through
- 720p60 / 1080p60

macOS M1+:

- actual alpha desktop window
- alpha edge/overlap pattern
- VRM hair/outline edge check
- OBS macOS capture path
- Retina/multi-monitor/topmost/click-through
- sleep/wake/relaunch
- 720p60 / 1080p60

### Tracking quality/performance

Required:

- webcam face/eyes/mouth/head on real camera
- hand/upper-body behavior
- low-light practical quality check
- M1 CPU/inference/frame-time evidence
- Windows CPU/GPU/RAM baseline measurement
- ARKit-compatible iPhone/iPad physical stream
- source switch/recovery behavior

### VMC — ADR-0006 remains Proposed

Required:

- receive from a real external VMC Performer
- send to a real external VMC Marionette
- inspect root/bone coordinate behavior
- inspect retarget behavior across different VRM rigs
- verify stale/loss recovery
- verify expression compatibility

### Custom shader external bundle

Required:

- trivial custom URP shader bundle for Windows
- equivalent bundle for macOS
- load/register/apply to a real VRM slot
- verify wrong-platform/missing bundle fallback
- verify required variants are present
- measure enabled cost

## Deferred beyond P0

- ADR-0015 executable-plugin security model — Deferred
- ADR-0017 character/world physics-domain split — Deferred

Neither should trigger speculative implementation during P0.

## Recommended execution order

1. `VCR > P0 > Validate Package Baseline`
2. `VCR > P0 > Run All Source-Free Checks`
3. `VCR > P0 > Create Runtime Test Scene`
4. `VCR > P0 > Validate Transparent Output Baseline`
5. enter Play mode and `Load VRM Into Runtime Scene` for editor inspection
6. build standalone player and launch with `--vcr-vrm=<absolute model path>` for end-to-end output evidence
7. webcam tracking check
8. ARKit physical check
9. VMC external interoperability check
10. Windows/macOS Evidence Player builds
11. Windows/macOS Performance Player builds
12. record PASS/FAIL evidence
13. resolve ADR-0003 / ADR-0006 / ADR-0007 from evidence
14. only then decide P0 branch merge

- published diagnostics snapshots now expose subsystem metrics through a cached read-only collection wrapper; consumers can retain allocation-free indexed reads, but cannot cast the snapshot back to a mutable metric array/list and alter the cached evidence later used by UI/JSON/CSV reporting
