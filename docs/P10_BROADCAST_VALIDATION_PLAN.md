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


## Operator-driven GUI/OBS evidence package

Automated native Player CI only verifies headless startup/shutdown. The following
must be performed on a **real Windows x64 desktop** and an **Apple Silicon macOS
desktop with a logged-in graphical session**, using builds from the *same tested
source commit*. GitHub-hosted headless execution never counts as GUI/OBS PASS.

1. Find a successful `.github/workflows/player-native-smoke.yml` run for the
   chosen source commit using `gh run list --workflow player-native-smoke.yml`.
   Download the exact `player-windows-<SHA>` and `player-macos-<SHA>`
   artifacts with `gh run download <RUN_ID> --name <ARTIFACT_NAME> -D <DIR>`.
   Verify each archive's SHA-256 against its adjacent `.sha256` file, and
   each `.source-sha` against the chosen 40-character Git commit SHA.
   Reject any missing/mismatched evidence; do not use a different tag's alpha
   zip simply because its version string appears similar.
2. On Windows, extract the Windows ZIP, launch
   `VirtualCharacterRender.exe -screen-fullscreen 0 -logFile <LOG_FILE>`.
   On Apple Silicon macOS, `unzip` the macOS ZIP and launch the
   `VirtualCharacterRender.app` through `open -W -a <APP_PATH> --args
   -screen-fullscreen 0 -logFile <LOG_FILE>`.
   In both cases, **do not** pass `-batchmode`, `-nographics` or
   `--vcr-smoke-report`; these belong exclusively to the previous CI gate.
   Launch without a VRM first; then exercise VRM0/VRM1 and the output
   behavior separately. Keep logs for each target/resolution.
3. Run a real OBS capture session for 1280x720@60 and 1920x1080@60 on
   **each** platform. Record the capture method, alpha/edge result on a
   contrasting composite, output readiness, resize/DPI, topmost, click-through,
   focus/minimize/restore, recovery, clean shutdown, observed frame average,
   P95/P99, and OBS dropped/skipped frame counts. Store a real screenshot or
   recording per platform/resolution along with the matching Player log.
   Capture the display/OS, GPU, driver, OBS version and display scale.
4. Create *empty, non-passing* templates with
   `python3 tools/verify-p10-interactive-evidence.py --init evidence/p10/<SESSION>`.
   Each JSON file represents one of
   `windows-720p60`, `windows-1080p60`, `macos-720p60`,
   and `macos-1080p60`. Fill the observed data manually.
   Reference the real screenshot and player log using paths within the
   evidence directory (e.g. `attachments/windows-720p60.png`).
   Every check must be explicitly confirmed; a failure or untested item
   remains `false`/`null` and cannot be reported as complete.
5. Inspect evidence with
   `python3 tools/verify-p10-interactive-evidence.py --verify evidence/p10/<SESSION> --source <40_CHARACTER_COMMIT_SHA>`.
   This only checks **operator-supplied record completeness**, platform,
   provenance fields, metrics sanity and attachment presence/path safety.
   It does **not** evaluate screenshots, prove that alpha is correct,
   verify the reported SHA against a downloaded artifact or independently
   confirm device behavior. Real visual review and source-archive hash
   verification remain required before P10 is marked PASS.

Do not commit personal VRMs, Player binaries, raw logs, or captured desktop
media to the source repository. Store private test evidence separately.
