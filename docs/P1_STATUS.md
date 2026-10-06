# P1 Status

Updated: 2026-10-02

## Active branch

```text
feature/p1-renderer-core
```

P1 starts from the exact preserved P0 implementation checkpoint:

```text
79bbbe3e0b826c5fe791abe1cd5b17d47a8d94f9
checkpoint/p0-hardware-validation-deferred
```

P0 hardware-dependent validation is deferred because the required physical test equipment is not currently available. This does not convert the remaining P0 evidence gates into PASS. The preserved checkpoint remains the reference state for later real-device validation.

## Implemented in the first P1 slice

### Scene lifecycle

- new `VCR.Runtime.Scene` assembly
- one-character scene runtime coordinator
- explicit runtime states:
  - Uninitialized
  - Ready
  - LoadingCharacter
  - CharacterReady
  - Faulted
  - Suspended
  - ShuttingDown
  - Stopped
- model replacement delegates to the existing atomic `Vrm10CharacterLoader`
- failed replacement preserves the previously active character
- reload uses the currently active model path
- explicit unload and shutdown
- pending model operations are cancelled when superseded or shut down
- no per-frame `Update()` loop in the scene coordinator

### Rendering lifecycle

- P0 `DesktopRenderBootstrap` promoted into the P1 renderer-core path
- runtime render-scale control for URP
- render scale clamped to 0.5–2.0
- resolution and frame-pacing controls remain independent from capability selection
- global runtime settings are captured before first override:
  - run-in-background
  - VSync
  - target frame rate
  - URP render scale
- camera clear/background/HDR/MSAA state is captured
- controlled shutdown restores captured runtime/camera state
- no per-frame `Update()` loop in the render bootstrap

### Camera/light abstraction

- explicit primary camera adapter for transform/projection state
- explicit primary light adapter for directional-light state
- no per-frame update loops
- apply/restore lifecycle owned by the scene runtime
- baseline light shadows remain disabled by default

### Scene/output/environment configuration

- serializable renderer settings snapshot
- render settings availability probe through `TryCaptureRenderSettings`: returns the live bootstrap settings when available and preserves the historical default-settings fallback while explicitly reporting unavailable when the bootstrap is missing
- serializable camera/light/output scene configuration
- capture and reapply without storing Unity object references
- overlay output lifecycle owned through `IOverlayOutputAdapter`
- explicit overlay shutdown contract
- dynamic environment state routed through the existing `IEnvironmentRuntime`
- environment state included in scene configuration snapshots

### Capability lifecycle

- scene runtime owns the P0 lazy `CapabilityRegistry`
- registration remains non-instantiating
- enabled capabilities are disposed before scene teardown
- capability counts are exposed through runtime metrics

### Application lifecycle and persistence

- new `VCR.Runtime.Application` assembly
- product `ApplicationRuntimeBootstrap`
- startup order: saved configuration -> scene runtime -> optional VRM
- `--vcr-vrm` and `--vcr-config` command-line options
- explicit application shutdown API
- platform pause/resume delegates to non-destructive scene suspend/resume
- active character and capability registrations remain intact across suspend
- versioned runtime configuration envelope
- current configuration schema version: 1
- unsupported future/legacy versions fail explicitly
- atomic replace for existing configuration files
- P1 runtime scene path: `Assets/VCR/P1/P1Runtime.unity`
- P1 build outputs are isolated under `Builds/P1`
- P1 Windows/macOS launchers support optional VRM and configuration path

### Diagnostics

- P0 diagnostics implementation promoted to reusable `RuntimeDiagnostics`
- `P0RuntimeDiagnostics` retained only as a compatibility wrapper
- generated P1 runtime scene uses `RuntimeDiagnostics`
- immutable low-frequency `RuntimeDiagnosticsSnapshot`
- `LatestSnapshot` polling API
- `SnapshotUpdated` event for later UI
- frame/tracking/subsystem metrics copied only at report cadence, not every frame
- reporting options exposed without coupling UI to the diagnostics `Update()` loop

### Validation

Interactive:

```text
VCR > P1 > Validate Renderer Core
```

Batch:

```text
tools/validate-p1-source-free.ps1
tools/validate-p1-source-free.sh
```

The P1 batch entrypoint first runs the inherited P0 source-free suite, then the P1 renderer-core checks.

Current P1 source-free checks cover:

- custom resolution minimum clamp
- render-scale minimum/maximum clamp
- frame-rate minimum clamp
- empty scene initialization
- live render-bootstrap availability/current-settings capture
- unload state transition
- shutdown state transition
- restoration of process-global frame settings
- absence of per-frame coordinator/render-bootstrap `Update()` loops
- camera projection/transform apply and restore
- light state apply and restore
- overlay Apply/Shutdown lifecycle
- environment state routing and snapshot capture
- lazy capability create/dispose lifecycle
- renderer/scene configuration round-trip
- application launch-option parsing
- configuration save/load round-trip
- unsupported future configuration version rejection
- non-destructive suspend/resume
- application bootstrap configuration-load/start/shutdown lifecycle

## P1 implementation status

The planned P1 renderer-core source implementation is complete enough to proceed to P2.

Still unresolved:

- Unity 6000.3.25f1 compile/package-resolution execution
- actual P1 Windows/macOS standalone build execution
- real platform suspend/resume behavior
- all hardware-dependent P0 evidence carried forward from the preserved P0 checkpoint

These remain validation items, not reasons to expand P1 architecture further without evidence.

## Next phase

Proceed to P2 — Material and Shader Runtime.

Hardware-specific P0 evidence remains deferred and should be resumed from the preserved checkpoint when test equipment becomes available.
