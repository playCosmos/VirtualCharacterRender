# P10 Broadcast Output Validation Plan

Updated: 2026-10-03

## Purpose

This plan turns the remaining P10 platform/OBS work into reproducible evidence. Source-level readiness is not a substitute for these tests.

## Test platforms

### Windows

- standalone Windows x64 player
- D3D11 baseline
- flip-model swapchain disabled for the current UniWinC/DWM alpha path
- record OS build, GPU, driver, display scale, monitor resolution, and OBS version

### macOS

- standalone Apple-Silicon player
- M1 or newer
- record macOS version, SoC, display scale, monitor resolution, and OBS version

Intel macOS is outside the current supported baseline.

## Required capture targets

Run the baseline at both:

| Target | Application request | Frame target |
| --- | --- | --- |
| Minimum | 1280x720 | 60 FPS |
| Recommended | 1920x1080 | 60 FPS |

Higher/custom resolutions may be exercised separately but do not inherit the same performance claim.

## Pre-capture checks

Before recording an OBS result:

1. The runtime overlay state reports `Active`.
2. `OverlayCaptureReadiness.Ready` is true.
3. The selected `BroadcastCaptureTarget` reports ready.
4. Transparent output is enabled.
5. Client width and height are positive.
6. Run In Background is enabled.
7. No native-output error is present.

If any check fails, record the failure and do not classify the OBS result as a valid capture test.

## Alpha test scene

Use a deterministic scene containing:

- fully transparent background
- opaque reference geometry
- semi-transparent edges or sprites
- fine hair/outline geometry where available
- no post effect that intentionally destroys alpha

Verify that the captured output does not substitute black/white background pixels for transparency and that semi-transparent edges remain stable.

## OBS evidence

For each target/platform combination record:

- capture method used
- whether alpha was retained
- application client size
- OBS canvas/output size
- application target FPS
- observed frame average/P95/P99 from VCR diagnostics
- OBS dropped/skipped-frame indicators when available
- screenshot or recording showing transparent composition over a contrasting OBS background

Exact OBS source names/UI labels are evidence metadata, not runtime contracts.

## Window behavior

Exercise:

- normal move
- resize
- display-scale/high-DPI change where available
- topmost on/off
- click-through on/off
- loss and regain of application focus
- minimize/restore
- display disconnect/reconnect where practical

The application must not require character reload to recover output.

## Failure/recovery test

Force or simulate an output failure where practical, then:

1. confirm runtime output state becomes `Faulted` or otherwise not ready
2. invoke the settings-preserving recovery path
3. confirm it reaches `PendingNativeApply` or `Active`
4. wait for bounded native apply completion
5. confirm readiness becomes true again
6. confirm OBS capture returns without restarting the application

A timeout must remain an explicit fault; it must not wait indefinitely.

## Performance evidence

For the baseline workload in `PERFORMANCE.md`:

- 720p60: collect at least a representative sustained run
- 1080p60: collect at least a representative sustained run
- record frame average, P95, P99
- record output apply failures/retries
- attribute tracking/render/output costs where available

P10 does not create a performance PASS from configured target FPS alone.

## Closeout rule

P10 platform/OBS evidence is PASS only when both supported platforms have standalone-player evidence for the required alpha/capture path and the recorded result can be reproduced from this plan.

Until then the P10 source checkpoint remains implementation-complete but evidence-deferred.
