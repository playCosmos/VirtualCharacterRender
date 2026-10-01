# Unity

Unity with URP is the accepted primary renderer/backend for the initial 3D product.

## P0 pinned baseline

- Unity: 6000.3.25f1
- URP: 17.3.x, matched to Unity 6000.3
- UniVRM: 0.131.2
- MediaPipeUnityPlugin: 0.16.3
- UniWindowController: 0.9.8 (`v0.9.8`)

See:

- `../docs/P0_STATUS.md`
- `../docs/COMPATIBILITY_MATRIX.md`
- `../docs/P0_VALIDATION_PLAN.md`
- ADR-0024

## First setup

1. Install Unity 6000.3.25f1 with the target desktop build support.
2. From the repository root, fetch the pinned MediaPipe binary package.

Windows:

```powershell
./tools/bootstrap-mediapipe.ps1
```

macOS:

```bash
chmod +x ./tools/bootstrap-mediapipe.sh
./tools/bootstrap-mediapipe.sh
```

3. The bootstrap also extracts the pinned FaceLandmarker and Holistic model files into `Assets/StreamingAssets/VCR/Models/`.
4. Open the `unity/` directory as the Unity project.
5. Let Package Manager resolve URP and UniVRM.
6. In Unity, run:

```text
VCR > P0 > Validate Package Baseline
```

Run all source-free checks after packages resolve:

```text
VCR > P0 > Run All Source-Free Checks
```

This includes package, presence, ARKit parser, VMC codec, diagnostics math, material fallback, normalized events, environment state, and lazy capability checks.

Then create the tracking smoke-test scene with:

```text
VCR > P0 > Create Tracking Test Scene
```

The generated scene contains the shared-webcam dual-task runner from ADR-0025 plus the priority tracking router.

To preview normalized tracking on an imported/loaded VRM:

1. Put the VRM instance in the same scene as the tracking runner.
2. Select the VRM root or one of its children.
3. Run:

```text
VCR > P0 > Attach Tracking Target to Selected VRM
```

The target auto-finds a routed provider when present, prefers UniVRM's ControlRig, and calibrates face/body on the first valid frames.

### Material / custom shader P0 path

Run the source-safety self-test:

```text
VCR > P0 > Validate Material Override Runtime
```

It verifies runtime cloning, generic color parameter mutation, source-material preservation, and invalid-shader fallback.

Build precompiled validation bundles independently:

```text
VCR > P0 > Build Shader Bundle > Windows x64
VCR > P0 > Build Shader Bundle > macOS
```

Then on the current Editor platform:

```text
VCR > P0 > Validate Current Platform Shader Bundle
```

The test shader is `Assets/VCR/P0/Shaders/P0TintUnlit.shader`. Runtime raw-HLSL compilation is intentionally unsupported.

### Transparent output P0 path

The generated runtime test scene now includes the P0 transparent-window adapter and alpha-reference pattern.

Create/recreate it with:

```text
VCR > P0 > Create Runtime Test Scene
```

This also applies the project-level output baseline:

- URP Alpha Processing on
- camera clear alpha 0
- HDR off for the SDR RGBA8 P0 path
- Windows D3D11 only
- D3D11 flip-model swapchain off
- windowed/resizable
- run in background
- automatic UniWinC opacity hit testing off

Static validation:

```text
VCR > P0 > Validate Transparent Output Baseline
```

The static check is not a platform PASS. Build standalone players and validate Windows/macOS native alpha plus OBS capture separately.

### Evidence and performance players

Build menus:

```text
VCR > P0 > Build > Windows x64 Evidence Player
VCR > P0 > Build > Windows x64 Performance Player
VCR > P0 > Build > macOS Evidence Player
VCR > P0 > Build > macOS Performance Player
```

Evidence players are Development builds and automatically write `vcr-p0-system.txt` plus `vcr-p0-diagnostics.csv` under `Application.persistentDataPath`.

Performance players omit automatic CSV file I/O. Use them for final frame-time measurements.

### Standalone VRM evidence bootstrap

The generated P0 runtime scene contains `P0StandaloneBootstrap`. Standalone Evidence/Performance players can load a real model without application UI:

```text
VirtualCharacterRender-P0 --vcr-vrm="<absolute path to model.vrm>"
```

Both `--name=value` and `--name value` forms are accepted.

Optional precompiled custom-shader validation can run in the same player:

```text
--vcr-shader-bundle="<platform-specific bundle>"
--vcr-shader-id="<shader name/id>"
--vcr-material-slot="<optional slot id>"
```

If `--vcr-material-slot` is omitted, the first discovered VRM material slot is used. If the shader cannot be resolved/applied, the source material remains/restores as fallback.

No command-line option means no model or shader bundle is loaded by this bootstrap, and it has no per-frame `Update()`.

Convenience launchers are available from the repository root:

```text
tools/run-p0-player.ps1
tools/run-p0-player.sh
```

See `../tools/README.md` for examples.

### Material/custom shader P0 path

Every VRM loaded by `Vrm10CharacterLoader` gets a `MaterialOverrideController`.

Run the source-free safety test:

```text
VCR > P0 > Validate Material Override Runtime
```

The test verifies that the source Material is preserved, an override uses a runtime clone, and an invalid shader ID restores the source material.

Runtime shader resolution is precompiled-only:

```text
shader ID
  ↓
RuntimeShaderRegistry
  ├─ Shader.Find for player-included shaders
  └─ Shader registered by RuntimeShaderBundleLoader
```

External bundles are platform-specific. Build and validate separate Windows/macOS shader bundles against the pinned Unity/URP baseline.

Raw HLSL source is not compiled inside the application.

### Environment/event/capability P0 path

The generated runtime scene contains:

- `Environment` + `BasicEnvironmentRuntime`
- environment-owned directional light
- `NormalizedEventHub`
- `TrackingPresenceEventAdapter`

Tracking subject disappearance is emitted as `tracking.subject_lost`; it is not an inactivity/AFK timer.

Source-free checks:

```text
VCR > P0 > Validate Environment Runtime
VCR > P0 > Validate Normalized Event Runtime
VCR > P0 > Validate Lazy Capability Registry
```

The event hub has a bounded ingress queue and bounded per-frame dispatch. The capability registry instantiates optional services only when enabled.

### Optional ARKit/iPhone P0 path

Run the source-free presence smoke check:

```text
VCR > P0 > Validate Presence Resolver
```

Validate diagnostics percentile math:

```text
VCR > P0 > Validate Diagnostics Math
```

Validate the ARKit packet parser:

```text
VCR > P0 > Validate iFacialMocap Parser
```

Then add the receiver:

```text
VCR > P0 > Add iFacialMocap ARKit Receiver
```

Set the iPhone/iPad IPv4 address on the receiver component before Play mode. The receiver requests the documented iFacialMocap/FaceMotion3D UDP v2 stream on port 49983 by default and connects itself as the router's preferred face provider.

When fresh ARKit packets become stable, the router selects ARKit face/head and stops MediaPipe FaceLandmarker. Holistic hand/upper-body inference continues. If ARKit packets become stale, MediaPipe face restarts automatically.

The initial head Euler sign profile is provisional and must be checked against physical pitch/yaw/roll on both Windows and macOS.

### Optional VMC P0 path

Run the source-free codec test:

```text
VCR > P0 > Validate OSC and VMC Codec
```

To receive optional VMC full-body motion:

```text
VCR > P0 > Add VMC Receiver
```

The receiver defaults to UDP 39539 and accepts only `127.0.0.1`. Set an explicit sender IPv4 address for LAN use. The router treats VMC as an optional full-body provider; face/head priority remains ARKit then MediaPipe. Full-body loss returns the VMC-driven pose/expression contribution toward neutral/reference state instead of holding the last received pose.

To send the selected VRM's final runtime pose:

```text
VCR > P0 > Add VMC Sender to Selected VRM
```

The sender defaults to `127.0.0.1:39539`, 60 Hz, original/non-ControlRig VRM humanoid bones, and VRM0-compatible expression names.

If the same process is already listening with the VMC receiver on loopback port 39539, the sender refuses that destination to prevent self-feedback. Use a different target port for bidirectional local testing.

The generated tracking scene also contains `P0RuntimeDiagnostics`. It reports frame average/P95/P99, normalized channel update rates/age, MediaPipe processing latency, and protocol metrics every five seconds. CSV evidence is optional in the component Inspector.

Do not change pinned package versions to make a local error disappear without recording the compatibility evidence.

## Current module skeleton

```text
Assets/VCR/
├─ Runtime/
│  ├─ Core/
│  │  └─ LatestValueBuffer
│  ├─ Tracking/
│  │  ├─ ITrackingSource
│  │  ├─ ITrackingFrameProvider
│  │  ├─ TrackingFrame
│  │  ├─ NormalizedFaceState
│  │  ├─ NormalizedUpperBodyState
│  │  ├─ NormalizedHandState
│  │  ├─ HumanoidPoseState
│  │  ├─ NormalizedExpressionState
│  │  ├─ Routing/
│  │  └─ MediaPipe/
│  │     ├─ MediaPipeFaceSource
│  │     ├─ MediaPipeHolisticSource
│  │     ├─ MediaPipeFaceNormalizer
│  │     ├─ MediaPipeHolisticNormalizer
│  │     └─ MediaPipeWebcamTrackingRunner
│  ├─ Character/
│  │  ├─ Vrm10TrackingTarget
│  │  ├─ Vrm10HumanoidPoseTarget
│  │  └─ Vrm10MotionSnapshotProvider
│  ├─ Capabilities/
│  │  └─ CapabilityRegistry
│  ├─ Environment/
│  │  ├─ IEnvironmentRuntime
│  │  └─ Unity/BasicEnvironmentRuntime
│  ├─ Events/
│  │  ├─ NormalizedEventBus
│  │  └─ Unity/
│  │     ├─ NormalizedEventHub
│  │     └─ TrackingPresenceEventAdapter
│  ├─ Materials/
│  │  ├─ MaterialSlotDescriptor
│  │  └─ Unity/
│  │     ├─ MaterialOverrideController
│  │     ├─ RuntimeShaderRegistry
│  │     └─ RuntimeShaderBundleLoader
│  ├─ Protocols/
│  │  ├─ Osc/
│  │  ├─ Vmc/
│  │  └─ VmcUnity/
│  └─ Diagnostics/
│     └─ P0RuntimeDiagnostics
└─ Editor/
   └─ P0/
      └─ P0PackageBaselineCheck
```

Planned module areas:

```text
VCR/
├─ Runtime/Core
├─ Runtime/Character
├─ Runtime/Rendering
├─ Runtime/Materials
├─ Runtime/Shaders
├─ Runtime/Tracking
├─ Runtime/Motion
├─ Runtime/Expression
├─ Runtime/Environment
├─ Runtime/Events
├─ Runtime/Protocols
├─ Runtime/Diagnostics
└─ Runtime/Output
```

Unity-specific objects remain inside Unity/backend modules and must not become public domain/runtime contracts.

## Implementation rule

The MediaPipe LIVE_STREAM callback will publish only the newest source-neutral tracking frame into the runtime buffer. Unity/render consumers never drain an inference backlog.
