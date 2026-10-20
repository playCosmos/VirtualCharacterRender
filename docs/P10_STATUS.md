# P10 Status

Updated: 2026-10-11

## Current integration state

P10 source and evidence tooling have been integrated into `develop` across
[PR #10](https://github.com/playCosmos/VirtualCharacterRender/pull/10) through
[PR #14](https://github.com/playCosmos/VirtualCharacterRender/pull/14).
The last integrated merge commit as of this update is
[`268b0030`](https://github.com/playCosmos/VirtualCharacterRender/commit/268b0030fd5eb70986e59ded766c976fe8e43092).
The original architecture branched from the preserved P9 checkpoint
(`checkpoint/p9-source-implementation`, `296265c9`).

The **PR #14 source commit** `67f3627` passed all three CI workflows:
[repository validation](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/38060889621),
[Unity P0–P13 source-free validation](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/38060889622),
and [Windows/macOS native Player build and headless startup/shutdown smoke](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/38060889596).
The **separate post-merge `develop` run** for `268b0030` must be
independently checked; the above passing runs refer to the PR source commit.

These results verify compilation, inherited source-free checks and headless
Player startup/shutdown, **not** on-screen alpha, actual OBS compatibility,
sustained 60 FPS, webcam/ARKit operation, or device behavior.
P0–P9 runtime/device/network/service evidence remains deferred wherever
previously documented.

## Source implementation checkpoint

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

`BroadcastCaptureTarget` defines the product's minimum 1280x720@60 and recommended 1920x1080@60 configuration targets. `BroadcastCaptureReadinessEvaluator` keeps configuration readiness separate from measured performance: it checks overlay readiness, exact requested render size, configured target FPS, and Run In Background, but it does not claim that the frame-time target was actually sustained. `SingleCharacterSceneRuntime.EvaluateBroadcastCaptureTarget` exposes this combined check for later UI/diagnostics.

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
- minimum 720p60 and recommended 1080p60 capture-target configuration
- resolution/FPS/background-execution mismatch reporting

The P0–P13 inherited source-free validation has now **passed in the pinned
Unity 6000.3.25f1 GitHub Actions environment** on the PR #14 source commit,
as linked above. This does not imply a local Unity Editor or physical camera/
desktop session was exercised.

P10 now includes an opt-in standalone Player telemetry recorder
(`InteractiveOutputTelemetry`) and operator evidence tooling:

- `tools/verify-p10-interactive-evidence.py`: validates downloaded CI ZIP
  bytes against SHA-256/source sidecars, operator evidence completeness,
  and actual Player telemetry data consistency. Never judges screenshot pixels.
- `tools/run-p10-interactive.py`: verifies, extracts and launches the native
  GUI Player on Windows x64 or Apple Silicon macOS. Captures the per-case
  Player log and opt-in JSON without marking OBS results as passed.
- `--import-telemetry`: imports measured hardware identification, ZIP/source
  SHA and last frame average/P95/P99 without changing operator yes/no checks.
- Repository CI executes deterministic self-tests for the evidence verifier and
  desktop runner without asserting GUI or OBS PASS.

See `P10_BROADCAST_VALIDATION_PLAN.md` for the desktop procedure and the
acceptance criteria.

## Next gate — real desktop and OBS evidence

The next P10 step requires **four manually observed sessions** using Players
built from the same source commit as the verified CI packages:

| Platform | 1280×720 / 60 FPS | 1920×1080 / 60 FPS |
| --- | --- | --- |
| Windows x64 | Required, unverified | Required, unverified |
| Apple Silicon macOS | Required, unverified | Required, unverified |

For each session the operator must confirm:

1. A real desktop GUI starts and native overlay output reaches `Active`;
   its transparent background and edges look correct in an OBS composite.
2. Client dimensions, requested frame target and native output readiness are
   recorded alongside Player logs and the opt-in telemetry JSON.
3. The application is moved/resized, DPI changes are exercised, topmost and
   click-through are toggled, focus/minimize/restore works, and recovery works
   after an output failure — without reloading the character.
4. Measured frame average/P95/P99 plus OBS dropped/skipped-frame counters,
   capture method/version and real screenshot/video are retained.
5. `tools/verify-p10-interactive-evidence.py --verify` validates the
   *completeness and numerical consistency* of all four cases against both
   original CI archives. A human independently reviews capture quality and
   real device behavior; the verifier cannot prove those observations.

**P10 status: source/CI gates satisfied on the referenced PR head; GUI/OBS/
performance acceptance remains unverified.** Do not classify P10 as fully
passed solely because the automated evidence schema accepts operator records.
