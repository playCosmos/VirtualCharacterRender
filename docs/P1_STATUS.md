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
- non-finite scene/render inputs fail safe before reaching Unity APIs: camera transform/projection components fall back to `SceneCameraSettings.Default`, light rotation/color/intensity/shadows fall back to `DefaultDirectional`, render scale falls back to `1.0`, and unsupported resolution presets normalize to `Recommended1080p`
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
- render-settings capture and broadcast-target evaluation resolve only `DesktopRenderBootstrap`; they no longer invoke full character/camera/light/overlay/environment dependency discovery for a render-only read
- `StatusChanged` notification is subscriber-isolated: one throwing observer cannot abort scene lifecycle transitions such as suspend/resume; failures are counted as `scene.status_subscriber_failures`
- character-load cancellation now has an explicit recovery boundary: an isolated cancellation restores `Ready`/`CharacterReady` only while the same generation is still in `LoadingCharacter`, so stale cancellation completions cannot overwrite a newer operation, Suspend, or Unload state
- direct overlay setting changes now commit `_overlayConfiguration` only after the adapter `Apply` succeeds; a throwing adapter no longer leaves the persisted/captured scene configuration claiming settings that were never applied
- full `ApplyConfiguration` now captures the previous scene configuration and performs reverse-order best-effort rollback if a later subsystem apply fails, preventing overlay failures from leaving earlier environment/render/camera/light changes partially committed; rollback-step failures are isolated and reported without skipping the remaining restore steps
- if any `ApplyConfiguration` rollback step itself fails, the scene now transitions to `Faulted`, records both the original apply error and rollback-failure details, and marks configuration recovery pending instead of remaining `Ready`/`CharacterReady` while the runtime may be only partially restored; `Initialize()` alone cannot clear this fault, and only a later successful full `ApplyConfiguration()` clears the pending recovery state
- `Suspend()` and `Resume()` now contain presentation/overlay adapter exceptions and return `false` with an explicit `Faulted` scene state instead of leaking exceptions from their boolean lifecycle contract or leaving an ambiguous partially suspended/resumed state
- `Initialize()` now uses the same lifecycle contract for render/camera/light presentation setup: apply exceptions are contained, previously captured presentation state is restored in reverse order on a best-effort basis, and the scene transitions to `Faulted` with a diagnostic error instead of leaking the initialization exception
- `TryRecoverOverlayOutput(out error)` now preserves its Try-style contract while suspended/faulted/stopped: lifecycle rejection is returned as `false` plus an error string instead of escaping as an `InvalidOperationException`
- `SetEnvironmentState(..., out error)` now follows the same boolean/error contract: lifecycle rejection or environment runtime exceptions return `false` plus a structured error without mutating the scene lifecycle state
- `TryApplyBroadcastCaptureTarget(..., out error)` now contains both lifecycle and render-apply exceptions; a failed render apply performs best-effort rollback to the previously captured render settings instead of leaking an exception or leaving a partially changed broadcast baseline
- if both broadcast render-target apply and its rollback fail, the scene now transitions to `Faulted` while returning the combined apply/rollback error, so callers cannot treat an unknown partially restored render baseline as healthy
- `ApplicationRuntimeBootstrap.SaveConfiguration(out error)` now contains scene snapshot/adapter getter exceptions as `false` plus a structured error, so a faulty optional output adapter cannot abort shutdown before the scene runtime is stopped
- application shutdown now propagates scene cleanup failures through its own bool/error result and remains callable after `_quitting` is latched so retained capability cleanup can be retried; a successful retry clears the scene registry instead of silently returning success before cleanup runs
- failed application startup attempts now restore the pre-attempt scene configuration before rethrowing the startup error; if character loading temporarily faults an otherwise recoverable scene, the baseline is reapplied and the scene is reinitialized so a later `StartRuntimeAsync()` call can retry on the same bootstrap without inheriting the failed attempt's persisted render/camera/light/output state
- the application configuration-store target is now staged per startup attempt and assigned to `_configurationStore` only after the attempt succeeds, so a failed `--vcr-config` launch cannot silently change the path used by later saves or make `ConfigurationPath` report a failed attempt as committed state
- application quit logging now reports the aggregated shutdown error contract rather than labelling every scene cleanup failure as a configuration-save failure
- scene shutdown retryability now covers every recorded cleanup failure, not only retained capability disposal: the `Stopped` fast-path is taken only when the capability registry is released and `LastError` is clear, allowing transient overlay shutdown or presentation-restore failures to be retried on a later shutdown call
- overlay adapter discovery now treats `Settings` reads as best-effort so `OverlayOutput` resolution itself stays usable; readiness converts `Settings`/`Status` getter exceptions into a `Faulted` readiness result, recovery returns `false/error`, while explicit scene configuration capture still samples live settings and surfaces getter failure to the save boundary instead of silently persisting stale output state
- `ApplicationRuntimeBootstrap.Suspend()` / `Resume()` now return and propagate the underlying scene lifecycle result, so callers can observe failed suspend/resume instead of the bootstrap silently discarding a `false` result
- missing `DesktopRenderBootstrap` lookup is negative-cached for one monotonic second, preserving late discovery while preventing repeated global lookup during temporary/partial scene configuration
- serializable camera/light/output scene configuration
- capture and reapply without storing Unity object references
- overlay output lifecycle owned through `IOverlayOutputAdapter`
- explicit overlay shutdown contract
- dynamic environment state routed through the existing `IEnvironmentRuntime`
- environment state included in scene configuration snapshots
- missing optional overlay/environment service discovery is negative-cached with a 1-second monotonic retry window and uses one reusable `List<MonoBehaviour>` with Unity's non-alloc `GetComponentsInChildren` overload, preserving late runtime discovery without recurring component-array allocation

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
