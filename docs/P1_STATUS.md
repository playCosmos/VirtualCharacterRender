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
- unload state transition
- shutdown state transition
- restoration of process-global frame settings
- absence of per-frame coordinator/render-bootstrap `Update()` loops

Actual Unity compilation/package resolution still requires a Unity 6000.3.25f1 environment.

## Next P1 work

1. camera abstraction
2. light abstraction
3. renderer/scene configuration snapshot suitable for later UI and persistence
4. output-adapter lifecycle integration with scene startup/shutdown
5. diagnostics promotion from P0-specific naming into reusable renderer diagnostics
6. dynamic-environment hook promotion into the scene runtime
7. platform lifecycle hooks for suspend/resume/relaunch-sensitive resources
8. source-free lifecycle tests for the new abstractions

Hardware-specific P0 evidence remains deferred and should be resumed from the preserved checkpoint when test equipment becomes available.
