# M2 rendering, streaming and long-running evidence — operator procedure

Status: **instrumentation introduced; actual interactive G acceptance PENDING**.
Do **not** turn a source build, headless Unity CI, or syntactically valid
performance report into a claim that VRM, OBS alpha, tracking, or a real
four-hour soak passed. This work is stacked on M1 PR #27; see M2 acceptance
[issue #31](https://github.com/playCosmos/VirtualCharacterRender/issues/31)
and P10 real capture
[issue #18](https://github.com/playCosmos/VirtualCharacterRender/issues/18).

## What is already available

- **P10 interactive Player:** an explicit --vcr-interactive-evidence=PATH option
  captures runtime output/readiness snapshots in a *visible native Player*;
  tools/verify-p10-interactive-evidence.py requires human-attested OBS results.
- **M2 measurement:** an additional normally inert Unity component
  M2PerformanceRecorder records a cumulative 0.25ms frame-time histogram
  and resource counters to an append-only UTF-8 CSV, **only** when a visible
  standalone Player is launched with --vcr-m2-perf-csv=ABSOLUTE_PATH.
- **No recording or network service during ordinary launches:** the new
  component does no frame sampling, ProfilerRecorder startup or filesystem
  writes unless explicitly activated. It rejects -batchmode and refuses to
  overwrite an existing session file or its .session.json metadata.
  Files remain local. Review files before sharing; nothing uploads them.
- **Durability:** one CSV line is appended approximately every five seconds.
  Existing lines may remain after a crash. A final quit_observed=1 row is
  written on normal Unity quit. Missing final marker means incomplete
  evidence, which is itself useful failure evidence.

## Running a measured GUI session

First select an exact source-bound Windows or macOS Player archive using
the existing P10 downloader/verification procedure in
docs/P10_BROADCAST_VALIDATION_PLAN.md. On the target **physical Windows x64**
or **Apple Silicon macOS arm64** desktop, create the four P10 operator
templates and use the P10 GUI launcher with M2 profiling enabled:

~~~bash
python3 tools/run-p10-interactive.py \
  --platform windows \
  --target 1080p60 \
  --source EXACT_40_HEX_TESTED_COMMIT \
  --artifacts /path/to/verified/players \
  --evidence /path/to/p10-evidence \
  --vrm /path/to/private-character.vrm \
  --m2-performance
~~~

Use --platform macos on an Apple Silicon machine. --vrm is optional for
idle runs. This launcher verifies the actual archive and runs a *visible*
Player, never -nographics or -batchmode. It records two additional files
in the P10 attachments folder:

- windows-1080p60-m2-performance.csv — cumulative frame/resource samples
- windows-1080p60-m2-performance.csv.session.json — platform/CPU/GPU/
  graphics API/driver, Unity version, histogram/GC method, start time

The launcher deliberately does not configure the GUI, press OBS controls,
evaluate transparency or create fake screenshots. Record the actual OBS
composite, alpha edges, dropped/skipped frames, model version, driver and
machine identity through the independent P10 operator evidence procedure.
The same approach applies to 720p60 and macOS. Use distinct CSV filenames
for additional long-running phases/sessions.

## Validating the local performance file

~~~bash
python3 tools/verify-m2-performance.py \
  --platform windows --target 1080p60 \
  --csv /path/to/p10-evidence/attachments/windows-1080p60-m2-performance.csv \
  --require-character --require-overlay \
  --report /path/to/p10-evidence/m2-windows-1080p60-summary.json
~~~

For a genuinely **continuous four-hour** session, also require
--min-duration-seconds 14400.

The verifier checks metadata, format/version, UTC/monotonic timestamps
(without gaps over 60 seconds), monotonic frame/GC counters, positive frame
statistics, requested render target and operating mode, normal Unity quit
marker and measured duration. Duration is anchored to the UTC session start:
the first sample must arrive within 60 seconds of recording activation and
sample timestamps must agree with elapsed time. A late first sample claiming
an already elapsed four hours, or a CSV paired with unrelated session
metadata, is rejected rather than counted as a valid soak. It computes:

- **Whole-session** frame mean and approximate P95/P99 from cumulative
  0.25ms histogram bins, slow frame count/rate above 16.667ms and
  frame count/wall-clock FPS.
- Managed/native Unity Profiler memory latest readings and
  least-squares slopes (MiB/hour) across the session.
- Gen0/Gen1/Gen2 GC collection counts. When Unity's GC Allocated In Frame
  profiler marker is available, its **latest frame** allocation is captured
  every five seconds. Unavailable is indicated by gc_alloc_available=0 and
  -1, never a fabricated zero.
- Render configuration, character presence, runtime scene state, and
  in-app overlay capture readiness, **not proof** of actual OBS output.

Frames >=256ms overflow the final histogram bin. Nonzero overflow is
reported and may make an upper percentile only a **lower bound**.
A slow frame is NOT an OBS dropped frame. The reported graphics-memory
capacity is hardware capacity, NOT observed VRAM usage. Unity's allocated
memory is NOT OS RSS, independent CPU/GPU frame cost, or OBS memory.
Those still require per-platform measurements with timestamps.

The resulting status is NATIVE_RUNTIME_MEASUREMENTS_VALIDATED, while
releaseAcceptance=false and visualObsCaptureVerified=false **even when
data are valid**. This boundary is intentional; physical capture and
model fidelity cannot be inferred from CSV content.

## Required manual M2 acceptance matrix (not automated)

For Windows x64 and Apple Silicon macOS arm64:

1. Test **two materially different real VRM 0.x models** and **two VRM 1.0
   models**, with legitimate local rights. Verify load, gaze/blink/mouth,
   hands, expressions, MToon, unload, repeat, malformed/oversized fallback.
   Record model version, privacy-preserving hash and observed visual result.
2. Verify actual OBS alpha compositing, DPI, click-through, focus, resize,
   topmost and recovery at **720p60 and 1080p60 for each OS**. Retain
   real screenshot/video, OBS version and dropped/skipped frame counters.
   Use the P10 verifier; M2 does not replace it.
3. Record a **continuous 4-hour** soak with periods of idle, real VRM,
   tracking and OBS. Timestamp phase transitions, retain per-phase/whole
   session evidence and observed errors. Confirm at least **50 actual VRM
   load/unload operations** and **50 physical/protocol source connect/
   disconnect cycles**, with transition logs and end-state checks.
   A manually recorded count alone is not proof of successful operation.
   Test GPU/device loss and restart separately.
4. Record CPU/GPU frame cost, true GC.Alloc/frame coverage, OS RSS, VRAM,
   camera inference latency, OBS capture latency, OBS dropped frames and
   memory slopes using suitable independent OS/vendor instrumentation.
   Mark unsupported counters unavailable and the corresponding gate
   DEFERRED. Publish a reference model, scene and machine profile before
   promising sustained 60 FPS.

**Block release acceptance** for crashes, incomplete 4-hour evidence,
unexplained memory growth, failed model/device recovery, mismatched source
provenance or missing visual OBS alpha evidence. None can be excused by
headless smoke or editor validation alone.

## CI/testing boundary

Repository CI runs:

~~~bash
python3 tools/verify-m2-performance.py --self-test
python3 tools/run-p10-interactive.py --self-test
~~~

These are synthetic *parser/CLI fixtures*, deliberately rejecting corrupt,
discontinuous, mismatched, missing-quit and short-soak evidence. They are
NOT M2 graphics/OBS/physical sessions. Unity compilation and native Player
smoke should continue to pass with no M2 option. No new runtime dependency
or network uploader is introduced.
