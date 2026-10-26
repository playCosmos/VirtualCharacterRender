# IL2CPP Release-Backend Checklist (16-Item Audit)

Status date: 2026-10-26

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

Current audit count: **9 PASS, 5 PARTIAL, 1 OPEN, 1 HOLD**. The count includes
the complete local release-candidate archive, native smoke, verifier evidence,
one candidate GUI shutdown check, local two- and five-run headless repeats, a
local 4-model x 2-run VRM GUI matrix, CI Windows IL2CPP 5-runs that
reproduced the >2 s tail, a same-commit native Windows/macOS 5-run, a
symbolized native audio-cleanup stack, a project-level audio-disable
diagnostic that isolates the tail to Unity audio cleanup, and the Windows
IL2CPP alpha.8 actual-input run for native Settings, load/reload, output
controls, config save, and the file dialog. It still does not include
release-grade OBS, DPI/resize, hardware, or M2 acceptance; those remain in the
partial/open items below.

Decision applied 2026-10-22 in `a9aab228b998a8b370700fef10a13c075de9edb8`: the
alpha publication workflow and the Windows IL2CPP CI workflow now use the
non-Development release-candidate path. Windows IL2CPP CI run
[39734065562](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39734065562)
then passed on that commit with a packaged smoke and the verifier. Alpha.5
remains unpublished and is not authorized for tagging/release.

Alpha.5 exposed native Settings defects after its candidate build. Its IL2CPP
window procedure lacked `[AOT.MonoPInvokeCallback(typeof(WindowProcedure))]`,
the window could open behind the topmost Unity overlay, and only the top-level
Win32 window was pumped, so native edit/button mouse and keyboard messages
were not dispatched. The source now adds the reverse P/Invoke attribute, keeps
the native window above the overlay, and pumps the top-level window, category
tabs, and category controls on the Unity main thread. A fresh Windows IL2CPP
`0.1.0-alpha.8` candidate then passed the actual native-input run: real
keyboard entry, real button load/reload, output 400-403, config save 205, and
the native file dialog. This closes the native Settings implementation blocker,
but it does **not** authorize publication. The alpha.5 archive/tag/release
boundary is unchanged: no tag or GitHub Release exists, and explicit approval
is still required before any publication.

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
| I-02 | A tracked, reproducible **non-Development** Windows IL2CPP release-candidate build path exists. | Commit `c87e98d03f65f170d77bb7a86de62f4e53a16ea7` adds `BuildWindowsReleaseCandidate()` with `BuildOptions.None` and a separate `Builds/ReleaseCandidates/<version>/Windows` output. A clean build from that commit completed with `Build Finished, Result: Success` and passed the native smoke and verifier. Both the Windows IL2CPP CI workflow and the alpha publication workflow now call the non-Development method. CI run `39734065562` from `a9aab228` built version `0.0.0-alpha.0` with `BuildOptions.None` and completed the verifier step successfully. | **PASS** | None for the build path. Publication authorization remains I-16. |
| I-03 | The Windows package contains `VirtualCharacterRender.exe`, `UnityPlayer.dll`, `GameAssembly.dll`, and `VirtualCharacterRender_Data/il2cpp_data/Metadata/global-metadata.dat`. | The current candidate archive contains all four files: EXE 667,136 bytes; UnityPlayer 36,719,024 bytes; GameAssembly 42,990,592 bytes; global-metadata 9,917,920 bytes. Alpha.4 independently recorded the same required IL2CPP shape. The non-Development CI artifact (`39734065562`) also passed the same required-file checks. | **PASS** | None for artifact presence. |
| I-04 | The Windows package contains no `MonoBleedingEdge` directory or Mono runtime fallback. | The current candidate package and the non-Development CI artifact (`39734065562`) were inspected; `MonoBleedingEdge` is absent from both. Their `GameAssembly.dll` is the IL2CPP output and each archive passes `verify-windows-il2cpp-release.py`. | **PASS** | None for packaging. Runtime behavior is covered by separate items. |
| I-05 | IL2CPP AOT, managed stripping, and reflection/serialization preservation are adequate for the shipped runtime surface. | `unity/Assets/VCR/link.xml` preserves `UniJSON.ConcreteCast` and `UniJSON.FormatterExtensions`; its audit note says player-side VCR code does not use reflection. The IL2CPP build compiles and alpha.4 smoke passed. The current candidate GUI loaded the private Alicia VRM, and the Windows IL2CPP alpha.8 candidate loaded and reloaded the private Sendagaya VRM through native Settings and saved config, exercising load/reload/UI/config paths. The full material/expression/tracking matrix and future reflection additions remain unverified. | **PARTIAL** | Re-run the full VRM/material/expression/UI matrix and confirm the link policy remains sufficient after future reflection additions. |
| I-06 | macOS remains on Mono; Windows-only IL2CPP changes do not alter the macOS backend. | `AlphaBuildMenu.BuildMac()` selects `ScriptingImplementation.Mono2x`; alpha.4 published a macOS Mono ZIP from the same release workflow. | **PASS** | This is a backend/build-scope pass only. Apple Silicon macOS runtime and hardware evidence remain M2 work. |
| I-07 | Native plugins used by the product (MediaPipe, Ultraleap/LeapC, UniWinC) are packaged and load under IL2CPP. | The current candidate package contains `VirtualCharacterRender_Data/Plugins/x86_64/LeapC.dll`, `LibUniWinC.dll`, `mediapipe_c.dll`, and `UltraleapOpenXRUnity.dll`; the corresponding managed assemblies are present under the IL2CPP backup folder. The headless smoke does not exercise the plugin entry points. | **PARTIAL** | Exercise each plugin in a real Player where hardware or a deterministic fixture permits; packaging alone is not runtime loading evidence. |
| I-08 | P/Invoke, native callbacks, delegate marshalling, and AOT-generated wrappers are safe on Windows IL2CPP. | Project-owned P/Invoke exists in `DesktopNativeSettingsWindow`, `DesktopWindowChromeController`, and `DesktopCharacterFileSelectionAdapter` (Windows `user32`/`comdlg32`); macOS settings use `VcrSettingsWindow` under platform guards. The alpha.5 IL2CPP candidate exposed a missing `[AOT.MonoPInvokeCallback(typeof(WindowProcedure))]` on the native Settings window procedure, so the window could not be created. The source now carries the attribute, keeps the native window above the topmost Unity overlay, and pumps the top-level window, category tabs, and category controls on the Unity main thread; `PollAction` only drains queued commands. The Windows IL2CPP alpha.8 candidate (`GameAssembly.dll` 42,993,152 bytes, SHA-256 `30021409F1ED51610E0FBB0269A30AFE573840D70BF473D701BD779204ADCF44`, no `MonoBleedingEdge`) then passed the actual path: the native Settings window was topmost and visible; an actual category click selected Character; real Ctrl+A/Ctrl+V entered the private Sendagaya path into edit 12; real button 300 loaded it and button 302 reloaded it (generation 1->2->3); the registry and main-window screenshot showed Sendagaya; real buttons 400-403 and 205 exercised output recovery and config save; and an actual 301 click opened a topmost, foreground `#32770` "Select VRM Character" dialog. Cancelling the dialog returned to a visible Settings window, and the app exited 0. A second actual-input run populated the dialog filename field with the private Sendagaya path and clicked the real Open button; the dialog closed, real button 300 loaded Sendagaya (generation 2->3 after a prior Alicia load), the registry changed from Alicia to Sendagaya, and the rendered screenshot showed Sendagaya. | **PASS** | None for P/Invoke/native-callback safety. Window chrome minimize/zoom, OBS, DPI, and resize remain in I-10. |
| I-09 | VRM 0.x/1.0 load, reload, unload, material/MToon, expression, blink, and mouth behavior work under Windows IL2CPP. | The fixed non-Development diagnostic build loaded an Alicia VRM and was used for quit-timing screenshots. The current alpha.5 candidate GUI run also loaded the private Alicia VRM (`CharacterReady`, `characterLoaded=true`, screenshot) and exited 0. A local 4-model x 2-run matrix then loaded VRM0 (`AliciaSolid`, `Sendagaya_Shibu`) and VRM1 (`Seed-san`, `VRM1_Constraint_Twist_Sample`) 8/8 times with `CharacterReady`, `characterLoaded=true`, `quitCallbackObserved=true`, and exit 0; evidence is local-only under `Builds/ReleaseCandidates/0.1.0-alpha.5/vrm-matrix`. A separate local negative check used a synthetic 9-byte non-GLB file and a 1,073,741,825-byte VRM: both failed to load without crashing, logged a clear load error, stayed at `sceneState=Ready` with `characterLoaded=false`, and exited 0; `runtime.json` did not expose the error and no visual fallback/UI review was done. These samples are private local evidence and the runs did not exercise expressions, blink/mouth, materials, or UI/configuration combinations. A Windows Mono alpha.8 diagnostic then loaded the private Sendagaya VRM through actual native Settings keyboard input and a real button click, and reloaded it with native button 302; the app read the Sendagaya path, reached `CharacterReady`, and the main-window screenshot showed Sendagaya. The Windows IL2CPP alpha.8 candidate repeated the actual input path: real keyboard entry into edit 12, a real button 300 load (generation 1->2), and a real button 302 reload (generation 2->3). The registry and main-window screenshot again showed Sendagaya. This closes the basic IL2CPP load/reload path, but not unload, material/MToon, expression, blink/mouth, VRM1, or the full UI/configuration matrix. | **PARTIAL** | Repeat with a clean release candidate and the full real-VRM/material/expression/UI matrix; keep public samples private. Do not count this as M2 PASS. |
| I-10 | Transparent window, topmost, click-through, resize, DPI, and desktop output behavior work under IL2CPP. | The published alpha.4 smoke used `-batchmode -nographics`, so it does not prove GUI presentation or transparent-window behavior. Diagnostic and current candidate GUI screenshots exist. A Windows Mono alpha.8 diagnostic exercised actual native buttons for topmost (400), transparency (401), click-through (402), and recover (403); `RecoverOutput` is settings-preserving, and telemetry showed `output.overlay.apply_attempts` increasing from 5 to 6 while the false/false/true settings were retained, followed by an explicit 402/401/400 restore to true/true/false. The Windows IL2CPP alpha.8 candidate repeated this with real button clicks: 400/401/402 produced false/true/false, false/false/false, and false/false/true; 403 retained false/false/true while `output.overlay.apply_attempts` increased 5->6; 402/401/400 restored true/true/false; and 205 saved a 1,264-byte config. The native 301 file dialog was topmost and foreground above the overlay. These are local IL2CPP interactive diagnostics. A separate local run recorded per-monitor DPI awareness (2), 96 DPI on both 1920x1080 monitors, moved the window to the secondary monitor, and resized to 1600x900 and 1024x576; telemetry and the overlay client size followed at 1584x861, 1008x537, and 1264x681. Both monitors were 96 DPI, so high-DPI scaling was not exercised. OBS 720p60/1080p60 and release-grade multi-monitor capture are still missing. | **PARTIAL** | Add OBS 720p60/1080p60 capture and high-DPI scaling; multi-monitor move at 96 DPI is only a local diagnostic. |
| I-11 | MediaPipe webcam, ARKit/iFacialMocap, VMC/OSC, and tracking routing work under IL2CPP. | The components are included in the alpha scene but start disabled. P0-P13 source-free Editor validation passed; no physical camera/ARKit or real VMC/OSC IL2CPP runtime evidence was found. | **OPEN** | Exercise real and loopback tracking paths in the release candidate; hardware evidence remains required for M2. |
| I-12 | Ultraleap/LeapC lifecycle and shutdown are correct under IL2CPP. | `924ddbc7` adds `VcrUltraleapRuntimePolicy`, which avoids the unused LeapC hint connection. Fixed diagnostic non-Development runs exited in ~0.6-1.1 s; actual Ultraleap hardware was not tested. | **PARTIAL** | Test hardware-enabled startup/shutdown, repeat-run acceptance, and retain a native stack/evidence boundary. |
| I-13 | The packaged Windows IL2CPP Player passes real startup/shutdown smoke. | Alpha.4 passed the packaged Player smoke and exited 0 (21,641 ms lifetime). The current clean candidate also passed: exit 0, process lifetime 4,332.6 ms, with `started`, `sceneReady`, `noCharacter`, `stopped`, and `passed` all true. CI run `39734065562` independently passed the non-Development packaged smoke from `a9aab228`: exit 0, process lifetime 5,359.0 ms, all flags true. | **PASS** | This is headless process evidence, not GUI acceptance; interactive presentation remains I-10. |
| I-14 | There is no unexplained long quit tail in the non-Development IL2CPP path. | Baseline diagnostic: ~16,946 ms window / ~17,224 ms PID. Fixed diagnostic: window ~429/780/772 ms; PID ~705/1,066/1,046 ms. The current alpha.5 candidate package was checked with the private Alicia VRM at 720p: window gone 441.9 ms, PID exit 754.7 ms, exit 0, with `CharacterReady` and `quitCallbackObserved=true`. A local 2-run headless repeat recorded tails of 2,043.3/2,090.9 ms; a separate local 5-run set passed 5/5 with median 2,065.7 ms, range 2,040.3-2,068.6 ms. A local 4-model x 2-run GUI matrix recorded window-gone 454.3-783.0 ms and PID exit 729.5-1,083.5 ms, which is close-path timing, not the headless tail. CI then confirmed the non-Development tail: Windows IL2CPP 5/5 PASS with verifier PASS and marker-to-PID-exit median 2,063.0 ms, range 2,062.0-2,063.0 ms; native Windows Mono 5/5 PASS with median 94.0 ms, range 79-109 ms. A same-commit Windows IL2CPP rerun [run 39876883333](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39876883333) on `d30d11af` also passed 5/5 with verifier PASS, tail median 2,062.0 ms, range 2,047.0-2,078.0 ms. The macOS repeat initially passed only 1/5 because the harness reused `unpacked-macos` and `unzip` prompted for replacement; `d30d11af` fixed the clean-unpack step, and the same-commit native rerun [run 39875640833](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39875640833) passed Windows Mono 5/5 (median 94.0 ms, range 78-110 ms) and macOS Mono 5/5 (median 227.9 ms, range 127.6-405.1 ms). A native stack at the tail (PID 26600, 2,071.6 ms, 87 threads frozen) was symbolized with the matching local Unity 6000.3.25f1 non-Development IL2CPP PDB (signature `9C93C608-27D2-4C18-B06C-5D7A4D4DCE28`, age 1, DbgHelp `PdbUnmatched=0`): the main thread was in `Baselib_Semaphore_TryTimedAcquire` -> `audio::DualThreadManager::ControlWaitForFlush` -> `ControlPanicClear` -> `ClearAllManagers` -> `CleanupAllObjects` -> `CleanupEngine` -> `PlayerCleanup`. This attributes the tail to Unity audio cleanup, not the managed VCR quit callback or the IL2CPP finalizer. A single local `-noaudio` diagnostic still recorded a 2,029.8 ms tail (control 2,057.3 ms), so the flag is not a mitigation. A local project-level `m_DisableAudio: 1` diagnostic build (`0.1.0-alpha.99`, source label `d30d11af...` plus local setting) then passed 5/5 with tail median 50.3 ms, range 0.0-50.5 ms, while the same-machine audio-enabled control passed 5/5 with median 2,055.6 ms, range 2,054.0-2,058.6 ms. The two builds had identical `VirtualCharacterRender.exe`, `UnityPlayer.dll`, and `GameAssembly.dll` SHA-256 hashes; the only changed variable was the AudioManager setting. This causally attributes the tail to Unity audio-manager cleanup. Disabling audio would remove product audio features, so it is not a release fix; the audio-enabled headless tail is accepted as a process-only delay after the managed quit callback. The local GUI close path remains 454.3-783.0 ms window-gone and 729.5-1,083.5 ms PID exit. Evidence: `Builds/ReleaseCandidates/audio-disable-diagnostic/` (local, not committed). | **PASS** | No product-safe removal is known; keep the 10 s CI gate. Ultraleap hardware/macOS/M2 boundaries remain tracked by #32, outside this item. |
| I-15 | `tools/verify-windows-il2cpp-release.py` accepts the archive, source, smoke, and IL2CPP evidence as one consistent artifact. | The verifier passed for the published alpha.4 Windows ZIP and evidence. It also passed for the current candidate: archive `b2256bfc6d9a7210d9c3498a87f53d646553c46167e8f5af9fbcc6b81b8189ef`, source `c87e98d03f65f170d77bb7a86de62f4e53a16ea7`, version `0.1.0-alpha.5`, with matching `.sha256`/`.source-sha` sidecars, IL2CPP evidence, smoke JSON, and runner evidence. The Windows IL2CPP CI workflow now runs the verifier before uploading its artifact. CI run `39734065562` passed step 16 with archive `1b9f1b663e66ab53ed85184fd9910314e0c5d6c08125bcde8ab2cd2c1e9cd75b`, source `a9aab228b998a8b370700fef10a13c075de9edb8`, version `0.0.0-alpha.0`; the `.sha256` sidecar and runner evidence matched. The same-commit rerun `39876883333` also passed the verifier with archive SHA-256 `af14513320fd14f6cc58983706b8acb89e43cece3b17291289befb778eadd5a8`, source `d30d11afa5d17688058ad5c7e7a8a1c36b79916e`, version `0.0.0-alpha.0`. | **PASS** | None for the local candidate or CI provenance. Publication still requires explicit authorization and a green publication run. |
| I-16 | Release provenance is immutable and publication is explicitly authorized. | Alpha.4 is the latest published release at `9536fdf625f9bce69a8061e4ebbfbf9dc7859193`. Alpha.5 remains an unpublished local candidate; no new tag or release was created. Windows IL2CPP CI run `39734065562`, native Windows/macOS Mono smoke run `39733680939`, and P0-P13 run `39733680901` passed on `a9aab228`; the candidate GUI shutdown check passed once. The publication workflow and Windows IL2CPP CI now use the non-Development release-candidate method. The same-commit native and Windows IL2CPP reruns are green (`39875640833`, `39876883333`). The native Settings blocker is closed locally by the Windows IL2CPP alpha.8 actual-input run. | **HOLD** | Do not tag or publish alpha.5 without explicit authorization. The non-Development CI/verifier gate and native-input checks are green; publication and release-grade OBS/DPI/hardware evidence still require authorization/external validation. |

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
- The candidate has been locally exercised with four private VRM samples in
  8 GUI runs, but not with OBS, physical tracking hardware, Apple Silicon
  macOS, or the full expression/material/UI matrix. It is release-candidate
  process plus local load/unload evidence, not M2 acceptance.

### Local repeat and VRM matrix (unpublished diagnostic)

- Harness: `tools/run-player-smoke-repeat.py` invokes the existing native
  runner once per run, preserves per-run `player.log`/`player-smoke.json`/
  `runner-evidence.json`, writes `repeat-summary.json`, and can require the
  `VCR exit: OnApplicationQuit finished` marker.
- Local candidate repeat: 2/2 runs passed, source
  `c87e98d03f65f170d77bb7a86de62f4e53a16ea7`, archive SHA-256
  `b2256bfc6d9a7210d9c3498a87f53d646553c46167e8f5af9fbcc6b81b8189ef`.
  Quit-marker-to-PID-exit tails were 2,043.3 ms and 2,090.9 ms; process
  lifetimes were 7,296.7 ms and 3,960.2 ms. Both exited 0. A separate 5-run
  set passed 5/5 with median 2,065.7 ms (range 2,040.3-2,068.6 ms) and
  process lifetime median 3,444.4 ms (range 3,364.3-3,598.7 ms). Evidence:
  `Builds/ReleaseCandidates/0.1.0-alpha.5/smoke-repeat-local/` and
  `smoke-repeat-local-5/`.
- Local VRM matrix: VRM0 `AliciaSolid_vrm-0.51` and `Sendagaya_Shibu`;
  VRM1 `Seed-san` and `VRM1_Constraint_Twist_Sample`; 2 runs each at a 720p
  window. All 8/8 reached `CharacterReady` with `characterLoaded=true`,
  `quitCallbackObserved=true`, and exit 0. Window-gone was 454.3-783.0 ms;
  PID exit was 729.5-1,083.5 ms. Evidence:
  `Builds/ReleaseCandidates/0.1.0-alpha.5/vrm-matrix/`.
- Local negative VRM diagnostics: a synthetic 9-byte non-GLB file and a
  1,073,741,825-byte VRM both failed to load without crashing, logged
  `glb header not found` / the 1 GiB safety-limit error, remained at
  `sceneState=Ready` with `characterLoaded=false`, and exited 0. The runtime
  telemetry did not expose the load error, and no visual fallback/UI review
  was performed. Evidence:
  `Builds/ReleaseCandidates/0.1.0-alpha.5/vrm-negative/`.
- The local repeat and matrix are diagnostics. The first Windows IL2CPP CI
  5-run now reproduces the >2 s tail, but this is still not OBS
  720p60/1080p60, camera/Ultraleap hardware, Apple Silicon macOS, soak/cycle,
  or M2 PASS evidence. The macOS and same-commit native reruns now pass 5/5;
  the same-commit Windows IL2CPP rerun also passed 5/5 with verifier PASS. A
  local `m_DisableAudio: 1` diagnostic passed 5/5 with median 50.3 ms versus an
  audio-enabled control at 5/5 and median 2,055.6 ms, with identical Player
  binaries; this isolates the tail to Unity audio cleanup. The audio-enabled
  headless delay is accepted as process-only; no product audio disable is
  planned.
- Native tail stack: PID 26600, marker-to-PID-exit 2,071.6 ms, 87 threads
  frozen for DbgHelp. The matching local Unity 6000.3.25f1 nondevelopment
  IL2CPP PDB (signature `9C93C608-27D2-4C18-B06C-5D7A4D4DCE28`, age 1,
  `PdbUnmatched=0`) resolves the main thread to
  `Baselib_Semaphore_TryTimedAcquire` -> `audio::DualThreadManager::ControlWaitForFlush`
  -> `ControlPanicClear` -> `ClearAllManagers` -> `CleanupAllObjects` ->
  `CleanupEngine` -> `PlayerCleanup`. This attributes the tail to Unity's
  audio cleanup, not the managed VCR callback or IL2CPP finalizer. A single
  `-noaudio` diagnostic still recorded 2,029.8 ms (control 2,057.3 ms), so the
  flag is not a mitigation. A project-level `m_DisableAudio: 1` diagnostic
  (5/5, median 50.3 ms, range 0.0-50.5 ms) versus an audio-enabled control
  (5/5, median 2,055.6 ms, range 2,054.0-2,058.6 ms), with identical
  `VirtualCharacterRender.exe`/`UnityPlayer.dll`/`GameAssembly.dll` hashes,
  confirms the audio-manager cause. The setting was restored; this is not a
  product build. Evidence: `Builds/ReleaseCandidates/audio-disable-diagnostic/`.
  A separate 8,453,015-byte minidump (`quit-tail.dmp`) from PID 42816 (tail
  2,053.3 ms) is retained for an external native debugger. Evidence:
  `Builds/ReleaseCandidates/0.1.0-alpha.5/native-tail-stack/`.

### Native Settings child-pump diagnostic (unpublished)

- Windows Mono `0.1.0-alpha.8` diagnostic build: PASS, output
  `Builds/Alpha/0.1.0-alpha.8/Windows`, size `222,533,065` bytes, build time
  `00:15:49.3132056`. This build carried temporary path/action logging for
  diagnosis; those logs were removed from the final source. It is a Mono
  diagnostic, not a release candidate.
- Root cause: `DesktopNativeSettingsWindow.PollAction()` called
  `PeekMessageW` only with the top-level HWND, so child edit/button mouse and
  keyboard messages were never dispatched. `PumpMessages()` now pumps the
  top-level window, the four category tabs, and all controls in every category
  on the Unity main thread; `PollAction()` only drains the resulting command
  queue. Category tabs are tracked separately so they cannot be hidden by
  `SelectCategory`.
- Actual input evidence: after selecting the Character category, the harness
  clicked edit 12, sent Ctrl+A/Ctrl+V with `keybd_event`, and clicked button
  300 with the real mouse. The application read the Sendagaya path from the
  same HWND, loaded it successfully, and the main-window screenshot showed
  Sendagaya. Button 302 then reloaded Sendagaya (`operationGeneration` 2->3),
  and the app log recorded a successful VRM reload. Evidence:
  `m1-native-childpump-keyevent` and `m1-native-childpump-keyevent-reload`
  (local temp, not committed).
- Actual output/config evidence: real button clicks 400/401/402/403 changed
  topmost/transparent/click-through as expected. `403` is explicitly
  **settings-preserving**: after click-through was enabled, recovery retained
  `false/false/true` and telemetry showed `output.overlay.apply_attempts`
  increasing (5->6), proving a restart rather than a settings reset. The
  harness then clicked 402/401/400 to restore `true/true/false`. Category 0
  was selected with WM_COMMAND, the harness waited until button 205 was
  visible, and a real click saved configuration (`configChanged=true`,
  1,264-byte config). Evidence: `m1-native-childpump-output-config` (local
  temp, not committed).
- Harness limitation: cross-process `GetWindowText` can return a cached edit
  title even after the real edit text changes. The harness's `loadedPath` JSON
  therefore remained Alicia while the application's `GetText(12)` and load
  path were Sendagaya. Do not use harness `GetWindowText` as edit-text
  evidence; use the application's load/telemetry result, registry, and the
  rendered screenshot. The final source no longer logs the edit text.
- Windows IL2CPP `0.1.0-alpha.8` candidate build: PASS, output
  `Builds/ReleaseCandidates/0.1.0-alpha.8/Windows`, size `1,884,599,237`
  bytes, build time `00:17:14.9273170`. `GameAssembly.dll` is 42,993,152 bytes
  with SHA-256
  `30021409F1ED51610E0FBB0269A30AFE573840D70BF473D701BD779204ADCF44`;
  the EXE SHA-256 is
  `145680641A2600D595CCB13B626CC9C873ACF1AA118C441AA09DE6FDD2C55B80`;
  `MonoBleedingEdge` is absent.
- IL2CPP actual input evidence: the native Settings window was topmost and
  visible. A real click selected the Character category, real Ctrl+A/Ctrl+V
  changed edit 12 to the private Sendagaya path, and a real button 300 click
  started the load without any `WM_COMMAND` fallback. `operationGeneration`
  went 1->2; the registry and `main-after-native-load.png` showed Sendagaya.
  A real button 302 click then reloaded it (generation 2->3). Evidence:
  `m1-native-il2cpp-keyevent-actual` (local temp, not committed).
- IL2CPP actual output/config evidence: real buttons 400/401/402/403 produced
  the expected topmost/transparent/click-through transitions. `403` retained
  `false/false/true` while `output.overlay.apply_attempts` increased 5->6;
  402/401/400 restored `true/true/false`. A real 205 click saved a
  1,264-byte config (`configChanged=true`). Evidence:
  `m1-native-il2cpp-output-config` (local temp, not committed).
- IL2CPP file-dialog evidence: a real button 301 click opened a visible
  `#32770` dialog titled `Select VRM Character`; it was topmost and foreground
  above the overlay, the native Settings window remained visible, and
  cancelling the dialog returned to a responsive Settings window. The app
  exited 0. Evidence: `m1-native-il2cpp-browse2` (local temp, not committed).
- IL2CPP file-dialog selection/returned-path evidence: a second run first
  loaded Alicia through a real button 300 click (generation 1->2), then opened
  the 301 dialog and populated its standard filename edit with the private
  Sendagaya path. A real click on the dialog's Open button closed the dialog;
  a real button 300 click then loaded Sendagaya (generation 2->3), the registry
  changed from Alicia to Sendagaya, and the main-window screenshot after
  closing native Settings showed Sendagaya. The harness set the standard
  filename edit with `WM_SETTEXT` because a clipboard-paste attempt did not
  focus it; the Open click and the application load were real. Evidence:
  `m1-native-il2cpp-browse-select3` (local temp, not committed).
- IL2CPP resize/DPI/multi-monitor diagnostic: the process reported per-monitor
  DPI awareness (2), and both 1920x1080 monitors were at 96 DPI. The window was
  moved to the secondary monitor and resized to 1600x900 and 1024x576;
  telemetry and the overlay client size followed (1584x861, 1008x537,
  1264x681). Exit 0. Evidence: `m1-native-il2cpp-resize-settle` (local temp,
  not committed). High-DPI scaling and OBS capture were not exercised.
- The first IL2CPP run falsely reported "button 300 no action" because the
  harness looked for a temporary `VCR native action` log that had been removed
  from final source and then sent a fallback command. It also read cached
  `GetWindowText`; the registry and screenshot already showed Sendagaya. The
  clean rerun waits on `operationGeneration` and sends no fallback, so the
  button evidence is actual.
- This closes the native Settings release blocker and I-08. I-09 and I-10
  remain partial/open because the expression/material/UI matrix, OBS,
  DPI/resize, macOS, and hardware evidence are separate.

### CI evidence and its boundary

Recent repository validation, P0-P13 source-free validation, and native
Windows/macOS smoke workflows passed on `main` and `develop` after the quit
fix. Those workflows do not prove interactive VRM rendering, GUI/OBS output,
physical tracking, Ultraleap hardware, or M2 performance. The Windows IL2CPP
release-candidate smoke is a separate process-level gate.

Both the Windows IL2CPP and native Windows/macOS smoke workflows now accept a
`smoke_repeat_count` dispatch input (1-10). When it is greater than 1, the
native runner records the quit-marker-to-process-exit tail in
`repeat-summary.json` and requires the marker. The first 5-run dispatch on
`5a7a97e7` produced:

- Windows IL2CPP non-Development:
  [run 39754710821](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39754710821)
  — **5/5 PASS**, verifier **PASS**, tail median **2,063.0 ms** (range
  2,062.0-2,063.0 ms). This reproduces the >2 s non-Development tail in CI.
- Native Windows Mono:
  [run 39754714410](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39754714410)
  — **5/5 PASS**, tail median **94.0 ms** (range 79-109 ms).
- Native macOS Mono: **1/5** in the first run. Run 01 passed with a 341.1 ms
  tail; runs 02-05 reused `unpacked-macos`, so `unzip` prompted to replace
  existing files and exited non-zero under CI. `d30d11af` deletes the unpack
  tree before extraction.
- Same-commit native rerun on `d30d11af`
  [run 39875640833](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39875640833):
  **success**. Windows Mono **5/5 PASS** (tail median **94.0 ms**, range
  **78-110 ms**); macOS Mono **5/5 PASS** (tail median **227.9 ms**, range
  **127.6-405.1 ms**). The macOS clean-unpack fix is validated.
- An earlier same-commit Windows IL2CPP attempt
  [run 39875638572](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39875638572)
  failed before the Player build because the Unity container could not create
  `C:/Users/ContainerAdministrator/AppData/Local/Unity/Caches` and Package
  Manager IPC was cancelled. This is a CI environment failure, not a
  source/build failure.
- Same-commit Windows IL2CPP rerun on `d30d11af`
  [run 39876883333](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39876883333):
  **success**. **5/5 PASS**, verifier **PASS**, tail median **2,062.0 ms**
  (range **2,047.0-2,078.0 ms**); archive SHA-256
  `af14513320fd14f6cc58983706b8acb89e43cece3b17291289befb778eadd5a8`,
  source `d30d11afa5d17688058ad5c7e7a8a1c36b79916e`, version
  `0.0.0-alpha.0`.

The Windows CI result confirms the tail is not a local harness artifact, but it
is not M2 acceptance. The local repeat and matrix results remain diagnostics.
The same-commit native macOS and Windows IL2CPP reruns are now green.

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
6. For quit-tail diagnostics, run
   `tools/run-player-smoke-repeat.py --platform windows --repeat 5
   --require-quit-marker` and inspect `repeat-summary.json`; this is still
   process evidence, not GUI/OBS/hardware acceptance.
7. Run `tools/verify-windows-il2cpp-release.py` with the smoke, runner,
   evidence, archive, source, and version inputs.
8. Keep the result as release-candidate evidence only; do not publish without
   explicit authorization.
