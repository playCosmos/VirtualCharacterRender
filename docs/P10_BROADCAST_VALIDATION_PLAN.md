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

1. Find a successful `.github/workflows/player-native-smoke.yml` run for
   the chosen source commit from a **develop branch push** using
   `gh run list --workflow player-native-smoke.yml --branch develop
   --event push --limit 20`. PR runs are deliberately excluded: GitHub
   builds a synthetic PR merge ref, which is not the same SHA as the PR
   head, and its artifact filename/source sidecar records that merge SHA.
   Prefer the provenance-checked
   **two-platform downloader**, requiring GitHub CLI `gh` authenticated
   for the repository, instead of manually choosing artifact names:

   ```sh
   python3 tools/download-p10-tested-players.py --run-id <SUCCESSFUL_SMOKE_RUN_ID> --source <40_CHARACTER_COMMIT_SHA> --output player-artifacts
   ```

   The downloader verifies the run's exact source SHA, repository, completed
   success conclusion, **both** platform build jobs and **both** native Player
   startup/shutdown jobs, unexpired exact-named CI artifacts, ZIP digests,
   source sidecars and binary package structure. It refuses to overwrite
   an existing `player-artifacts/` directory. A mismatch fails closed; it
   never downgrades to an unverified tag, prior build or manual PASS.
   Download on one connected machine and transfer the **original** verified
   ZIPs and sidecars unchanged to Windows and Apple Silicon test desktops.
   If `gh` is unavailable, manually download the exact two artifact names
   from that same successful run, but then independently check the GitHub
   run/commit provenance as well as the local ZIP bytes.

   On **each** testing desktop keep the relevant named CI artifact
   in `player-artifacts/` and run the following
   before unzipping or launching it; this checks the **actual ZIP bytes**
   against the CI `.sha256` sidecar, matches the `.source-sha` against
   the expected commit, and confirms the package contains a Player:

   ```sh
   python3 tools/verify-p10-interactive-evidence.py --check-archive --platform windows --artifacts player-artifacts --source <40_CHARACTER_COMMIT_SHA>
   # On Apple Silicon macOS use --platform macos instead.
   ```

   Reject any missing/mismatched evidence; do not use a different tag's alpha
   zip simply because its version string appears similar.
2. On Windows, extract the Windows ZIP, launch
   `VirtualCharacterRender.exe -screen-fullscreen 0 -logFile <LOG_FILE>`.
   On Apple Silicon macOS, `unzip` the macOS ZIP and launch the
   `VirtualCharacterRender.app` through `open -W -a <APP_PATH> --args
   -screen-fullscreen 0 -logFile <LOG_FILE>`.
   In both cases, **do not** pass `-batchmode`, `-nographics` or
   `--vcr-smoke-report`; these belong exclusively to the previous CI gate.
   Append `--vcr-interactive-evidence=<ABSOLUTE_TELEMETRY_JSON_PATH>` to
   each GUI launch (e.g. `attachments/windows-720p60-runtime.json`).
   Use distinct output paths for each target/resolution and keep the
   Player open for at least two diagnostics report intervals before
   closing it normally. This opt-in recorder observes scene startup,
   overlay state/readiness, native client dimensions, render settings,
   and diagnostics frame average/P95/P99 when available. It never
   enables capture, changes UI/output settings or exits the process.
   Launch without a VRM first; then exercise VRM0/VRM1 and the output
   behavior separately. Keep logs for each target/resolution.
3. Run a real OBS capture session for 1280x720@60 and 1920x1080@60 on
   **each** platform. Record the capture method, alpha/edge result on a
   contrasting composite, output readiness, resize/DPI, topmost, click-through,
   focus/minimize/restore, recovery, clean shutdown, observed frame average,
   P95/P99, and OBS dropped/skipped frame counts. Store a real screenshot or
   recording per platform/resolution along with the matching Player log.
   Capture the display/OS, GPU, driver, OBS version and display scale.
   Retain the generated runtime telemetry JSON alongside the Player log
   and visual capture. Runtime-readiness flags and frame percentiles
   are observations, **not** proof of correct alpha, OBS composition,
   acceptable motion or an independently verified GUI PASS.
4. Create *empty, non-passing* templates with
   `python3 tools/verify-p10-interactive-evidence.py --init evidence/p10/<SESSION>`.
   Each JSON file represents one of
   `windows-720p60`, `windows-1080p60`, `macos-720p60`,
   and `macos-1080p60`. Fill the observed data manually.
   Reference the real screenshot, player log and opt-in runtime telemetry
   JSON using paths within the evidence directory (e.g.
   `attachments/windows-720p60.png`,
   `attachments/windows-720p60-runtime.json`).
   The JSON field for the latter is `runtimeTelemetry`. Use the **same**
   requested resolution and 60 FPS target for the last two samples. After
   the native Player closes normally, the verified machine-measured fields
   can be imported into that *existing incomplete* case JSON:

   ```sh
   python3 tools/verify-p10-interactive-evidence.py --import-telemetry evidence/p10/<SESSION> --platform windows --target 720p60 --artifacts player-artifacts --source <40_CHARACTER_COMMIT_SHA>
   # On macOS use --platform macos and choose either --target 720p60 or 1080p60.
   ```

   The importer recalculates the CI ZIP SHA-256 and checks native Player
   metadata and the final two stable capture-ready samples. It imports
   `sourceSha`, `playerArchiveSha256`, `osVersion`, `cpu`,
   `gpu`, the last sampled frame average/P95/P99 and the existing
   `runtimeTelemetry` and `playerLog` attachment paths. Conflicting
   existing records are never overwritten. The operator must **still**
   record architecture, display scale, OBS capture parameters/counters,
   real screenshot/video and every observed check manually. The importer
   leaves them unchanged. Without the importer, copy the last measured frame
   average/P95/P99 into the case JSON and copy exact runtime `graphicsDevice`
   to the case `gpu` field.
   Do not fabricate missing measurements or force readiness flags to true.
   The verifier requires two stable, capture-ready Player samples with positive
   measured frame times and a normal quit callback. Non-ready or missing
   samples mean the test evidence is incomplete, not successful.
   Every manually observed check must still be explicitly confirmed; a
   failure or untested item remains `false`/`null` and cannot be reported
   as complete.
5. Gather the two original CI artifact packages in a single
   `player-artifacts/` directory. Keep each `windows.zip`, `windows.sha256`,
   `windows.source-sha`, `macos.zip`, `macos.sha256`,
   and `macos.source-sha`. Copy the **verified** ZIP SHA-256 into the
   corresponding case's `playerArchiveSha256` field. Inspect evidence with:

   ```sh
   python3 tools/verify-p10-interactive-evidence.py --verify evidence/p10/<SESSION> --artifacts player-artifacts --source <40_CHARACTER_COMMIT_SHA>
   ```

   The verifier recomputes each original ZIP's actual SHA-256, matches CI
   digest and source sidecars, checks basic Player package structure, and
   verifies that all four operator records contain that same platform's
   independently recomputed archive hash. It also checks record completeness,
   attachment existence/path containment, and the supplied runtime telemetry's
   Unity/platform identity, normal quit flag, two final stable 60 FPS target
   samples, output readiness, native client size, and presence of measured
   frame-time statistics. It rejects manual frame average/P95/P99 values that
   differ from the last runtime sample by more than 0.11 ms.
   **Ready flags are internal software-state reports, not observed OBS
   pixels.** The verifier does **not** inspect screenshots, judge OBS alpha,
   independently establish the source archive's GitHub origin, or confirm
   that attached desktop media matches the telemetry session.
   Real visual review and validation that these artifacts came from the
   nominated successful CI run are still mandatory before P10 PASS.

## Guided graphical Player session (opt-in helper)

Instead of manually extracting the downloaded CI ZIP and assembling Unity
command-line arguments, an operator can use the helper below on a **logged-in
native graphical desktop**. It reuses the verified archives and their two CI
sidecars, rejects unsafe ZIP member paths, unpacks the Player to a temporary
directory, launches the visible Player without headless flags and collects
a per-case log plus telemetry. The temporary Player extraction is deleted
when the application exits.

First create templates with `python3 tools/verify-p10-interactive-evidence.py
--init evidence/p10/<SESSION>`. Download the correct **source-matched**
CI artifact with `gh run download` and keep its `.zip`, `.sha256` and
`.source-sha` sidecars together in `player-artifacts/`.

Windows x64 PowerShell (use an installed Python 3.11+ interpreter):

```powershell
python tools/run-p10-interactive.py --platform windows --target 720p60 --source <40_CHARACTER_COMMIT_SHA> --artifacts player-artifacts --evidence evidence/p10/<SESSION>
```

Apple Silicon macOS with native arm64 Python:

```sh
python3 tools/run-p10-interactive.py --platform macos --target 1080p60 --source <40_CHARACTER_COMMIT_SHA> --artifacts player-artifacts --evidence evidence/p10/<SESSION>
```

Run **both targets on both operating systems**; the two commands above
illustrate one target each. `--target` selects the evidence filenames,
**not** the render resolution. Configure the actual size/FPS in the Player
UI, then leave it running long enough for at least two steady diagnostics
samples, do the real OBS capture and window/recovery tests, and close
the Player normally. Add `--vrm <PRIVATE_MODEL.vrm>` only for a separate
character-loaded test. Keep the model local.

The helper **never** marks visual/OBS checks as passed, fills operator
assertions, modifies your saved configuration, or uploads evidence.
Populate each case's `playerLog` and `runtimeTelemetry` paths from
the helper's `attachments/` output. Collect a real OBS screenshot/video
and set all remaining operator evidence fields yourself. Existing evidence
files are never overwritten; use a new session for retests.

Do not commit personal VRMs, Player binaries, raw logs, or captured desktop
media to the source repository. Store private test evidence separately.
