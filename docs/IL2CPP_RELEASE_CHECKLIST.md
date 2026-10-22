# IL2CPP Release-Backend Checklist (16-Item Audit)

Status date: 2026-10-22

This document is an evidence audit of the Windows x64 IL2CPP release backend.
It is not an M2, M3, M4, P10, OBS, hardware, or performance acceptance report.
Every item below distinguishes **build/package evidence** from **interactive
runtime evidence**.

## Why this list exists

The repository, docs, workflows, and searched GitHub issues did not contain a
tracked enumeration of the previously referenced "16 IL2CPP items". This audit
therefore normalizes the release-backend requirements into 16 explicit items.
If a different canonical list exists, it should replace this mapping rather
than be silently treated as satisfied by this document.

The list covers the Windows IL2CPP path and the Mono boundary for macOS. It
does not convert the older Development alpha build, the headless startup
smoke, or a local diagnostic build into interactive product acceptance.

Current audit count: **7 PASS, 6 PARTIAL, 2 OPEN, 1 HOLD**. The count includes
the complete local release-candidate archive, native smoke, and verifier
evidence below. It still does not include interactive GUI, hardware, OBS, or
M2 acceptance.

Decision applied 2026-10-22: the alpha publication workflow and the Windows
IL2CPP CI workflow now use the non-Development release-candidate path.
Alpha.5 remains unpublished and is not authorized for tagging/release.

## Legend

- **PASS**: the stated item is directly supported by source/build/published
  evidence, at the stated evidence level.
- **PARTIAL**: some evidence exists, but it is not release-grade, not from the
  current candidate, or does not exercise the relevant runtime path.
- **OPEN**: no adequate evidence was found in this audit.
- **HOLD**: intentionally not performed without separate authorization.

## The 16 items

| ID | Checklist item | Current evidence | Status | Remaining gap |
|---|---|---|---|---|
| I-01 | Windows scripting backend is IL2CPP, not Mono. | `unity/ProjectSettings/ProjectSettings.asset` has `scriptingBackend: Standalone: 1` (IL2CPP). `AlphaBuildMenu.BuildWindows()` sets `ScriptingImplementation.IL2CPP`. Published alpha.4 Windows ZIP is IL2CPP with `GameAssembly.dll`. | **PASS** | None for backend selection; runtime coverage is covered by separate items. |
| I-02 | A tracked, reproducible **non-Development** Windows IL2CPP release-candidate build path exists. | Commit `c87e98d03f65f170d77bb7a86de62f4e53a16ea7` adds `BuildWindowsReleaseCandidate()` with `BuildOptions.None` and a separate `Builds/ReleaseCandidates/<version>/Windows` output. A clean build from that commit completed with `Build Finished, Result: Success` and passed the native smoke and verifier. Both the Windows IL2CPP CI workflow and the alpha publication workflow now call the non-Development method. | **PASS** | None for the build path. Publication authorization remains I-16. |
| I-03 | The Windows package contains `VirtualCharacterRender.exe`, `UnityPlayer.dll`, `GameAssembly.dll`, and `VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat`. | The current candidate archive contains all four files: EXE 667,136 bytes; UnityPlayer 36,719,024 bytes; GameAssembly 42,990,592 bytes; global-metadata 9,917,920 bytes. Alpha.4 independently recorded the same required IL2CPP shape. | **PASS** | None for artifact presence. |
| I-04 | The Windows package contains no `MonoBleedingEdge` directory or Mono runtime fallback. | The current candidate package was inspected and `MonoBleedingEdge` is absent. Its `GameAssembly.dll` is the IL2CPP output and the archive passes `verify-windows-il2cpp-release.py`. | **PASS** | None for packaging. Runtime behavior is covered by separate items. |
| I-05 | IL2CPP AOT, managed stripping, and reflection/serialization preservation are adequate for the shipped runtime surface. | `unity/Assets/VCR/link.xml` preserves `UniJSON.ConcreteCast` and `UniJSON.FormatterExtensions`; its audit note says player-side VCR code does not use reflection. The IL2CPP build compiles and alpha.4 smoke passed, but the current candidate has not yet exercised VRM reflection paths. | **PARTIAL** | Re-run VRM/load/UI/configuration paths against the release candidate and confirm the link policy remains sufficient after future reflection additions. |
| I-06 | macOS remains on Mono; Windows-only IL2CPP changes do not alter the macOS backend. | `AlphaBuildMenu.BuildMac()` selects `ScriptingImplementation.Mono2x`; alpha.4 published a macOS Mono ZIP from the same release workflow. | **PASS** | This is a backend/build-scope pass only. Apple Silicon macOS runtime and hardware evidence remain M2 work. |
| I-07 | Native plugins used by the product (MediaPipe, Ultraleap/LeapC, UniWinC) are packaged and load under IL2CPP. | The current candidate package contains `VirtualCharacterRender_Data/Plugins/x86_64/LeapC.dll`, `LibUniWinC.dll`, `mediapipe_c.dll`, and `UltraleapOpenXRUnity.dll`; the corresponding managed assemblies are present under the IL2CPP backup folder. The headless smoke does not exercise the plugin entry points. | **PARTIAL** | Exercise each plugin in a real Player where hardware or a deterministic fixture permits; packaging alone is not runtime loading evidence. |
| I-08 | P/Invoke, native callbacks, delegate marshalling, and AOT-generated wrappers are safe on Windows IL2CPP. | Project-owned P/Invoke exists in `DesktopNativeSettingsWindow`, `DesktopWindowChromeController`, and `DesktopCharacterFileSelectionAdapter` (Windows `user32`/`comdlg32`); macOS settings use `VcrSettingsWindow` under platform guards. No `MonoPInvokeCallback` was found in `Assets`. The project compiles and the headless smoke starts, but GUI P/Invoke paths are not exercised. | **PARTIAL** | Exercise the native settings/window/file-dialog paths and package callbacks in the release candidate; add a focused marshalling audit if new callbacks are introduced. |
| I-09 | VRM 0.x/1.0 load, reload, unload, material/MToon, expression, blink, and mouth behavior work under Windows IL2CPP. | The fixed non-Development diagnostic build loaded an Alicia VRM and was used for quit-timing screenshots. That sample is private local evidence; the run is not release-grade and does not establish the full model/material/expression matrix. | **PARTIAL** | Repeat with a clean release candidate and the required real-VRM matrix; keep public samples private. Do not count this as M2 PASS. |
| I-10 | Transparent window, topmost, click-through, resize, DPI, and desktop output behavior work under IL2CPP. | The published alpha.4 smoke used `-batchmode -nographics`, so it does not prove GUI presentation or transparent-window behavior. A diagnostic GUI screenshot exists, but no release-grade interactive or OBS evidence exists. | **OPEN** | Run interactive Windows IL2CPP checks, including OBS 720p60/1080p60 and DPI/resize cases. |
| I-11 | MediaPipe webcam, ARKit/iFacialMocap, VMC/OSC, and tracking routing work under IL2CPP. | The components are included in the alpha scene but start disabled. P0-P13 source-free Editor validation passed; no physical camera/ARKit or real VMC/OSC IL2CPP runtime evidence was found. | **OPEN** | Exercise real and loopback tracking paths in the release candidate; hardware evidence remains required for M2. |
| I-12 | Ultraleap/LeapC lifecycle and shutdown are correct under IL2CPP. | `924ddbc7` adds `VcrUltraleapRuntimePolicy`, which avoids the unused LeapC hint connection. Fixed diagnostic non-Development runs exited in ~0.6-1.1 s; actual Ultraleap hardware was not tested. | **PARTIAL** | Test hardware-enabled startup/shutdown, repeat-run acceptance, and retain a native stack/evidence boundary. |
| I-13 | The packaged Windows IL2CPP Player passes real startup/shutdown smoke. | Alpha.4 passed the packaged Player smoke and exited 0 (21,641 ms lifetime). The current clean candidate also passed: exit 0, process lifetime 4,332.6 ms, with `started`, `sceneReady`, `noCharacter`, `stopped`, and `passed` all true. | **PASS** | This is headless process evidence, not GUI acceptance; interactive presentation remains I-10. |
| I-14 | There is no unexplained long quit tail in the non-Development IL2CPP path. | Baseline diagnostic: ~16,946 ms window / ~17,224 ms PID. Fixed diagnostic: window ~429/780/772 ms; PID ~705/1,066/1,046 ms. The original issue's 2.1-2.5 s non-Development CI tail is separate and still open. | **PARTIAL** | Run the required repeated acceptance, collect a native stack if the CI tail remains, and test actual Ultraleap hardware/macOS boundaries. |
| I-15 | `tools/verify-windows-il2cpp-release.py` accepts the archive, source, smoke, and IL2CPP evidence as one consistent artifact. | The verifier passed for the published alpha.4 Windows ZIP and evidence. It also passed for the current candidate: archive `b2256bfc6d9a7210d9c3498a87f53d646553c46167e8f5af9fbcc6b81b8189ef`, source `c87e98d03f65f170d77bb7a86de62f4e53a16ea7`, version `0.1.0-alpha.5`, with matching `.sha256`/`.source-sha` sidecars, IL2CPP evidence, smoke JSON, and runner evidence. The Windows IL2CPP CI workflow now runs the verifier before uploading its artifact. | **PASS** | None for the local candidate. Publication still requires explicit authorization and a green publication run. |
| I-16 | Release provenance is immutable and publication is explicitly authorized. | Alpha.4 is the latest published release at `9536fdf625f9bce69a8061e4ebbfbf9dc7859193`. Alpha.5 remains an unpublished local candidate; no new tag or release was created. The publication workflow and Windows IL2CPP CI now use the non-Development release-candidate method, but publication still requires explicit authorization. | **HOLD** | Do not tag or publish alpha.5 without explicit authorization. Require a green non-Development CI/verifier run, and run the candidate GUI shutdown check before using the fix as published GUI evidence. |

## Evidence ledger

### Published alpha.4 (historical baseline, Development build)

- Tag: `v0.1.0-alpha.4`
- Source commit: `9536fdf625f9bce69a8061e4ebbfbf9dc7859193`
- Windows ZIP SHA-256: `0e0e6afe5e33118b3743345cfde2c97f693e7995311aa5b25688633378bfd21b`
- `GameAssembly.dll` SHA-256: `90a057f56dc9b2f8395ae21abb7b3f46cc63770f584a7c95c4ff2aa8b19d6cf5`
- Packaged Player smoke: PASS, exit 0, 21,641 ms lifetime on `Windows-10-10.0.20348-SP0`
- Verifier: PASS against the published Windows ZIP and evidence
- This proves Windows IL2CPP compile/package/startup-shutdown for alpha.4.
  It does **not** prove VRM runtime, GUI, OBS, tracking hardware, or M2
  acceptance.

### Quit-tail fix

- Fix commit: `924ddbc7cda5fdde3f77de639be73c6397b9ebcb`
- Root cause: an unused Ultraleap LeapC hint connection; `HandTrackingHintManager.StartupHints()` created a LeapC `Controller`, and `Connection.Stop()` waited in `_polster.Join()`.
- Fix: `VcrUltraleapRuntimePolicy` sets `HandTrackingSourceUtility.LeapOpenXRHintingAvailable = true` in `BeforeSceneLoad`, so the unused hint connection is not opened. No local-package patch is committed.
- Diagnostic build hashes:
  - Baseline `GameAssembly.dll`: `1E0C317DF283959E244A5999861C63513A8A0D6A059442609D0AAB11A6581A23`
  - Fixed `GameAssembly.dll`: `9498535249ABD00D422C2C6585C8B76DCB5556F98A3B01814D5D604191EEF326`
  - Both diagnostic EXEs: `145680641A2600D595CCB13B626CC9C873ACF1AA118C441AA09DE6FDD2C55B80`
- The diagnostic build used an untracked editor-only build method and is not
  release provenance.

### Current release-candidate evidence (local, unpublished)

- Commit: `c87e98d03f65f170d77bb7a86de62f4e53a16ea7`
  (runtime fix `924ddbc7cda5fdde3f77de639be73c6397b9ebcb` plus the
  build-tooling-only release-candidate method)
- Method: `VCR.Editor.Alpha.AlphaBuildMenu.BuildWindowsReleaseCandidateAndExit`
- Build options: `BuildOptions.None` (non-Development)
- Unity: `6000.3.25f1`; target: `StandaloneWindows64`
- Build result: `Build Finished, Result: Success`, size `1,884,571,037` bytes,
  time `00:17:14.1546273`
- Output: `Builds/ReleaseCandidates/0.1.0-alpha.5/Windows/VirtualCharacterRender.exe`
- Source version: `0.1.0-alpha.5` (local candidate; unpublished)
- Archive: `VirtualCharacterRender-0.1.0-alpha.5-Windows-x64.zip`,
  `422,971,706` bytes
- Archive SHA-256:
  `b2256bfc6d9a7210d9c3498a87f53d646553c46167e8f5af9fbcc6b81b8189ef`
- `GameAssembly.dll`: `42,990,592` bytes,
  SHA-256 `9498535249abd00d422c2c6585c8b76dcb5556f98a3b01814d5d604191eef326`
- `VirtualCharacterRender.exe`: `667,136` bytes,
  SHA-256 `145680641a2600d595ccb13b626cc9c873acf1aa118c441aa09de6fdd2c55b80`
- `UnityPlayer.dll`: `36,719,024` bytes,
  SHA-256 `3fc09d493e387c33b2c0df29d9ec75803158a3a98ad885917b1039d13bfc6c6d`
- `global-metadata.dat`: `9,917,920` bytes,
  SHA-256 `70858d9fec0289110dc036ca1b0b5ae07d522fd6ffba4266a5316eb388ef9fe8`
- `MonoBleedingEdge`: absent
- Packaged native DLLs include `LeapC.dll`, `LibUniWinC.dll`,
  `mediapipe_c.dll`, and `UltraleapOpenXRUnity.dll`.
- Native smoke: PASS, exit `0`, process lifetime `4,332.6 ms`;
  `player-smoke.json` has all of `started/sceneReady/noCharacter/stopped/passed`
  true.
- Verifier: PASS (`tools/verify-windows-il2cpp-release.py`) using the archive,
  sidecars, source, version, IL2CPP evidence, smoke JSON, and runner evidence.
- The candidate has **not** been interactively exercised with a VRM, OBS,
  physical tracking hardware, or macOS. It is release-candidate process
  evidence, not M2 acceptance.

### CI evidence and its boundary

Recent repository validation, P0-P13 source-free validation, and native
Windows/macOS smoke workflows passed on `main` and `develop` after the quit
fix. Those workflows do not prove interactive VRM rendering, GUI/OBS output,
physical tracking, Ultraleap hardware, or M2 performance. The Windows IL2CPP
release-candidate smoke is a separate process-level gate.

## Explicitly not claimed

- M2 PASS (real VRM matrix, OBS 720p60/1080p60, cameras, Ultraleap hardware,
  Apple Silicon macOS, 4-hour soak, >=50 load/unload and tracking cycles,
  performance metrics).
- M3 PASS (Authenticode/Developer ID, notarization, clean install, upgrade,
  rollback, license approval).
- M4 PASS or R0-R6 start.
- P10/OBS/hardware/perf PASS.
- Alpha.5 publication, a new tag, or a new release.
- Interactive Windows IL2CPP behavior from headless smoke or diagnostic
  screenshots.

## Reproduction checklist for the current candidate

1. Build from the committed source with
   `VCR.Editor.Alpha.AlphaBuildMenu.BuildWindowsReleaseCandidateAndExit`.
2. Verify the package paths and absence of `MonoBleedingEdge`.
3. Produce `il2cpp-evidence.txt` with backend, version, source SHA, Unity
   version, and the required hashes/sizes.
4. Create the ZIP and `.sha256`/`.source-sha` sidecars.
5. Run `tools/run-player-smoke.py --platform windows`.
6. Run `tools/verify-windows-il2cpp-release.py` with the smoke, runner,
   evidence, archive, source, and version inputs.
7. Keep the result as release-candidate evidence only; do not publish without
   explicit authorization.
