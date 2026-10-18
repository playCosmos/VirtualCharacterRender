# Commercial readiness plan — VirtualCharacterRender

Status: **active; not release-approved**. Baseline: `develop` plus unmerged
Windows/macOS shutdown and native Settings work from [PR #26](https://github.com/playCosmos/VirtualCharacterRender/pull/26).
This plan is a prioritized implementation and evidence contract, **not a claim
that all P0–P13 milestones have passed on real equipment**.

## Product boundary

Ship a dependable **one-performer VRM 0.x/1.0 desktop renderer** for
Windows x64 and Apple Silicon macOS, with manual controls, basic built-in
tracking, optional protocol sources, and an OBS-compatible output.
Disabled tracking and optional extension features must have negligible
recurring cost. Advanced 2D SDKs, multi-character workflows, executable
third-party plugins and unvalidated premium effects are outside the first
commercial release gate. Do not delete their source merely because they are
deferred; keep them isolated and disabled by default.

## Evidence levels

- **S — source/contract:** deterministic tests, static validation, pinned
  dependencies, exact SHA, reproducible assets. This is *not* execution evidence.
- **N — native headless:** build and run a real Player per platform, exercise
  startup and normal `OnApplicationQuit`, verify evidence and exit code. No
  claims about a GUI, capture hardware, webcam or graphics performance.
- **G — interactive hardware:** real desktop, model, capture application and
  physical device tests with session logs, exact build SHA, machine/driver
  identity and operator observation; no manufactured/synthetic PASS.
- **R — distribution:** signed/notarized installer/archive, uninstall,
  migration/rollback, license and security review. A ZIP from CI is not R.

Release requires all mandatory **G** and **R** criteria as well as **S/N**.
`docs/P10_STATUS.md`, `docs/P0_VALIDATION_PLAN.md`,
`docs/PERFORMANCE.md`, and `docs/RELEASE_PROVENANCE.md` remain normative.

## Work packages (ordered)

### M0 — close baseline defects and establish gates (blocking)

1. Complete PR #26 verification; preserve exact source SHA, test logs and
   source-bound native artifacts. Do not silently merge or publish it.
2. Windows normal GUI exit with unloaded scene, VRM loaded, camera on, and
   Ultraleap connected/disconnected: isolate native teardown versus managed
   cleanup using the read-only collector. Investigate the historical ~17 s
   affected-machine delay and ~2.1 s Windows non-development headless tail
   separately; never attribute either without stack/device evidence.
3. Build and smoke **non-development macOS** as well as Windows; ensure both
   retail-like builds exercise real normal Unity cleanup without debugging
   build flags. Explicitly record that unsigned, headless artifacts are not
   distribution acceptance.
4. Crash/timeout and corrupted-config recovery tests, error messages that
   describe the action to take, no silent device-retry loops or data loss.
5. Collect remaining P2/real-GUI Settings issues (window focus, title-bar
   drag, modal replacement, keyboard access, DPI, async feedback) on both OSes.

Exit: **S/N green**, zero reproducible data-loss/startup blockers, no
unexplained >10 s normal-quit cases on test hardware, documented remaining
G/R blockers. A test passing under `-batchmode -nographics` is insufficient.

### M1 — finish first-run and operator workflows (blocking)

- Cold start without camera, model or tracking; discoverable load, unload,
  reload, last-model restore failure, reset to known-good defaults and
  error reporting without a debugger.
- Configuration: bounded JSON, atomic writes, version handling, backup and
  recovery/migration tests, file permissions and user-writable storage.
- Native Settings: repeated open/close, OS focus, resizable pane without
  clipped inputs, all action results, input validation, keyboard navigation,
  Windows DPI/macOS Retina and background-rendering preference restoration.
- Optional input transitions: webcam access denied, camera unplug/replug,
  ARKit/VMC packet loss and malformed input, Ultraleap connect/disconnect,
  network port collision, no-source neutral return. Recovery must not require
  restarting the application unless clearly documented.

Exit: all user-journey scripts pass on both OSes with evidence; no uncaught
exception, unrecoverable optional input failure or unintended persistent
setting change.

### M2 — rendering and streaming acceptance (blocking)

- VRM 0.x and 1.0: at least two materially different real models per version;
  loading, materials/MToon, gaze/blink/mouth, hands, expressions, asset unload,
  malformed/oversized models and fallback diagnostics.
- P10 [issue #18](https://github.com/playCosmos/VirtualCharacterRender/issues/18):
  real GUI transparency, click-through, window order, OBS alpha capture,
  resize/DPI and capture recovery on Windows and macOS.
- Run 720p60 and 1080p60 on *declared hardware profiles*; report mean,
  P95/P99 frame time, frame drops, GC.Alloc/frame, CPU/GPU cost, VRAM/RAM,
  webcam inference delay and capture latency. The 60 FPS commitment is
  conditional on a published reference model, scene and tested hardware.
- Soak for 4 hours (idle, model, tracking, OBS), observe resource slopes and
  error counts, plus >=50 model load/unload and >=50 optional source
  connect/disconnect cycles. Test GPU/device loss and restart separately.

Exit: four Windows/macOS × 720p/1080p real OBS sessions accepted, sustained
reference-target results recorded, no reproducible crash or sustained memory
growth; deficiencies block promotion rather than being called PASS.

### M3 — release engineering, privacy and support (blocking)

- Signed Windows distributions and a signed/notarized Apple Silicon macOS
  `.app`, verified on clean supported OS accounts, including permission prompts
  and launch without Unity Editor, Python or development tools.
- Installer or clearly documented portable ZIP behavior, removal, app data
  persistence, upgrade, rollback, backwards-compatible configuration and
  fresh-profile recovery. Immutable SHA-256/source provenance; independent
  archive rehash; no credential exposure in logs or packages.
- Dependency inventory/SBOM and redistribution license check for Unity,
  UniVRM, MediaPipe, Ultraleap, UniWinC, fonts, models and native libraries.
  Audit network listeners, external file loading, untrusted shaders and
  executable plugin boundary. Keep third-party executable plugins disabled.
- A user-facing privacy statement: local-only storage by default, no automatic
  upload of Player logs, minidumps or camera imagery; explicit review before
  sharing a diagnostic ZIP. No unannounced telemetry, updater or background
  network service.
- Crash/support runbook, troubleshooting, first-run manual and known
  limitations; test localization and keyboard accessibility.

Exit: signed/notarized packages, license/security review, reproducible
clean-install and upgrade/rollback evidence. Never infer this from alpha CI.

### M4 — release candidate and maintenance

- Fix and retest all reproducible severity-0/1 issues, triage severity-2
  issues with documented workarounds; freeze features before RC.
- Candidate fingerprint, release notes, test matrix and rollback artifact;
  no mutable-release overwrite or moving prior tags.
- Stage an RC/beta, collect consented hardware feedback, run the same suite
  after every hotfix; maintain support response and CVE/dependency patch plan.

## Mandatory acceptance ledger

| Area | Automated today | Required before release |
| --- | --- | --- |
| P0–P13 source contracts | Unity source-free CI | No further deferred G evidence may be promoted by implication |
| Win/macOS executable | Development and Windows no-debug headless smoke | Non-debug macOS, actual GUI and OS/device scenarios |
| Normal shutdown | Managed + PID logging, timeout tests | Real GUI/hardware repeatability and native stack attribution |
| Native Settings | Source checks + AppKit compilation | Physical Windows/macOS interaction incl. keyboard, DPI and async actions |
| VRM 0.x/1.0 | Source contract only for real models | Real load/render/unload and failure paths |
| Webcam/ARKit/Ultraleap/VMC | Isolated adapter contracts | Real devices, loss/recovery/permission errors |
| Transparent OBS output | Source readiness contracts | 4 P10 real capture sessions |
| 60 FPS / allocation budget | Target documented | Recorded per-platform measured percentiles and reference hardware |
| Signing/notarization | Not a CI acceptance gate | Verified production archives/installers |
| Privacy/licenses/support | Partial diagnostic guidance | Distribution audit and operator documentation |

## Quality policy

1. Every fix must link to a **reproducible failure, demonstrated risk or
   contractual requirement**; avoid speculative refactors and new dependencies
   for their own sake.
2. Validate the smallest relevant unit first, then repository validation,
   P0–P13 Editor check and native Players when a runtime path changes.
3. One coherent work package per commit/PR where possible; do not mix
   unrelated experiments into a release. Preserve user changes.
4. Mark every result **PASS / FAIL / DEFERRED (missing real evidence)**.
   CI success does not turn deferred GUI or physical-device tests into PASS.
5. Do not merge, promote a tag or publish a release from this plan alone.
