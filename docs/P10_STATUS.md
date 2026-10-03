# P10 Status

Updated: 2026-10-03

## Active branch

```text
feature/p10-broadcast-output
```

P10 starts from the preserved P9 source checkpoint:

```text
checkpoint/p9-source-implementation
296265c9b3d67a1ac6897dc9b5b78d329836eeab
```

P0-P9 runtime/device/network/service evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## First P10 source slice

P10 reuses the existing output architecture instead of introducing a second broadcast pipeline:

- `IOverlayOutputAdapter` remains the shared output boundary
- `SingleCharacterSceneRuntime` remains the lifecycle owner
- `UniWinCOverlayOutput` remains the current Windows/macOS transparent-window adapter
- P0 Windows D3D11/BitBlt and macOS Apple-Silicon constraints remain explicit until replaced by measured evidence

The first productionization slice adds explicit output lifecycle state:

```text
Inactive
Configured
PendingNativeApply
Active
Unsupported
Faulted
```

`UniWinCOverlayOutput` now:

- distinguishes configured state from native-active state
- reports native-apply pending state
- fails closed after a bounded native-apply timeout instead of waiting forever
- reports state/active/pending/topmost/click-through diagnostics
- reports apply-attempt, apply-failure, and native-apply-success counters
- preserves the existing transparent/topmost/click-through settings contract
- restores camera/native state through the existing shutdown path

`OverlayCaptureReadinessEvaluator` provides a source-level readiness check for capture workflows. It requires:

- supported output adapter state
- no output fault
- native settings no longer pending
- active native overlay
- transparency enabled
- positive client width and height

This readiness result means only that the application-side overlay prerequisites are satisfied. It does not prove OBS capture, platform compositor behavior, or alpha correctness in a built player.

`SingleCharacterSceneRuntime.OverlayCaptureReadiness` exposes the same readiness contract without leaking UniWinC/native objects into the scene/runtime API.

`OverlayOutputRecovery.TryRestart` and `SingleCharacterSceneRuntime.TryRecoverOverlayOutput` provide an explicit settings-preserving recovery path. Recovery performs `Shutdown -> Apply(previous settings)`, contains adapter exceptions, and treats pending/configured restart states as a successful retry attempt while still requiring a later readiness check before capture.

## Source-free validation

Interactive:

```text
VCR > P10 > Validate Broadcast Output
```

Batch:

```text
tools/validate-p10-source-free.ps1
tools/validate-p10-source-free.sh
```

The P10 batch entry runs P0-P9 source-free suites first and then checks:

- legacy `OverlayOutputStatus` constructor compatibility
- Active/Faulted/Unsupported state mapping
- capture-ready active transparent output with positive client size
- pending native-apply rejection
- faulted/unsupported/inactive rejection
- opaque-output rejection
- invalid-client-size rejection
- settings-preserving overlay restart
- unsupported restart and adapter-exception containment

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Deferred P10 evidence

- execute the P0-P10 Unity source-free batch suite
- Windows standalone native transparency and timeout behavior
- macOS Apple-Silicon standalone native transparency
- OBS window/game capture with alpha on Windows
- OBS-compatible capture path on macOS
- resize and high-DPI behavior
- click-through/topmost behavior in standalone players
- 720p60 and 1080p60 frame-time/capture stability with the overlay active
- recovery after native output/capture failures

P10 must not convert these platform/OBS evidence gates into PASS without real standalone-player validation.
