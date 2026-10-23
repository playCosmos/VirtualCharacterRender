# Alpha Testing

## Release

Current published alpha: 0.1.0-alpha.4.

Immutable source snapshot: tag `v0.1.0-alpha.4` at commit `9536fdf625f9bce69a8061e4ebbfbf9dc7859193`. The tag is a lightweight tag on the release commit; build provenance follows the tag, not a mutable branch tip.

This alpha is a full-feature integration build of the current develop runtime. No existing camera, ARKit, VMC, rendering, UI, appearance, event, material, environment, diagnostics, or 2D-host implementation is compiled out for the alpha.

The first validation pass intentionally does not require physical webcam or ARKit/iFacialMocap hardware. That is a validation-scope decision only, not a product-feature removal.

## Build evidence

The most recent successful complete alpha binary workflow is [run 39550583196](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39550583196), with Unity `6000.3.25f1` at tagged source commit `9536fdf625f9bce69a8061e4ebbfbf9dc7859193`. Both platform builds, ZIP packaging, the native Windows Player smoke, and release attachment succeeded:

- Windows x64 **IL2CPP** Development build: `VirtualCharacterRender-0.1.0-alpha.4-Windows-x64.zip` (469,605,154 bytes; SHA-256 `0e0e6afe5e33118b3743345cfde2c97f693e7995311aa5b25688633378bfd21b`)
- macOS Development **Mono** build: `VirtualCharacterRender-0.1.0-alpha.4-macOS.zip` (105,075,696 bytes; SHA-256 `91a6201383b45aeaa3c1dd26132bb5286d4f49e3b5162c2b7103b82b4c80f63c`)

The Windows IL2CPP build produced a 155,316,224-byte `GameAssembly.dll` (SHA-256 `90a057f56dc9b2f8395ae21abb7b3f46cc63770f584a7c95c4ff2aa8b19d6cf5`) and an `il2cpp_data/Metadata/global-metadata.dat` file; no `MonoBleedingEdge` runtime is present. The packaged Player started, reached the scene, reported no character loaded, shut down cleanly, and exited 0 with a 21,641 ms lifetime on `Windows-10-10.0.20348-SP0`.

Published evidence assets are part of the release:

- `il2cpp-evidence.txt` — backend, Unity version, source SHA, and `GameAssembly.dll`/`UnityPlayer.dll`/`global-metadata.dat` hashes
- `windows-il2cpp-smoke.json` — Player-owned startup/shutdown report
- `windows-il2cpp-runner-evidence.json` — archive/source binding, exit code, and lifetime
- `build-provenance.json` and `SHA256SUMS.txt`

Independent download verification matched both ZIP hashes to `SHA256SUMS.txt`, and `python tools/verify-windows-il2cpp-release.py` returned **PASS** against the published Windows ZIP and evidence.

The macOS leg was re-run once after a transient Unity Bee build-backend failure on its first attempt. The published macOS ZIP comes from the successful re-run (attempt 2); the successful Windows IL2CPP artifact from attempt 1 was reused. The retry is recorded here rather than hidden.

This closes Windows IL2CPP compile, package-resolution, binary-generation, and native startup/shutdown evidence for the tagged alpha snapshot. It does not close VRM/character runtime behavior, hardware tracking, OBS, signing/notarization, clean-install/rollback, or performance evidence. New alpha versions follow `docs/RELEASE_PROVENANCE.md`.

A local `0.1.0-alpha.5` non-Development IL2CPP candidate was built from `c87e98d03f65f170d77bb7a86de62f4e53a16ea7` and passed the packaged Player smoke and `tools/verify-windows-il2cpp-release.py`. A local candidate GUI shutdown check with the private Alicia VRM at a 720p window recorded window-gone 441.9 ms and PID exit 754.7 ms with exit code 0, after `CharacterReady`. A local 4-model x 2-run VRM matrix (two VRM0 and two VRM1 samples) then reached `CharacterReady` and clean exit in 8/8 runs. A separate local 2-run packaged headless repeat recorded quit-marker-to-PID-exit tails of 2,043.3 ms and 2,090.9 ms despite exit 0; a local 5-run set passed 5/5 with median 2,065.7 ms. The first CI 5-run on `5a7a97e7` then recorded Windows IL2CPP non-Development 5/5 PASS (verifier PASS, tail median 2,063.0 ms, range 2,062.0-2,063.0 ms) and native Windows Mono 5/5 PASS (median 94.0 ms, range 79-109 ms). The macOS repeat was blocked by a harness `unzip` replacement prompt and passed only 1/5; `d30d11af` fixed clean unpacking, and the same-commit native rerun [39875640833](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39875640833) passed Windows Mono 5/5 (median 94.0 ms) and macOS Mono 5/5 (median 227.9 ms, range 127.6-405.1 ms). The same-commit Windows IL2CPP rerun [39876883333](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39876883333) also passed 5/5 with verifier PASS and tail median 2,062.0 ms (range 2,047.0-2,078.0 ms). A native stack at the tail was symbolized with the matching Unity 6000.3.25f1 non-Development IL2CPP PDB: the main thread was waiting in `audio::DualThreadManager::ControlWaitForFlush` (`ControlPanicClear` -> `ClearAllManagers` -> `CleanupAllObjects` -> `CleanupEngine` -> `PlayerCleanup`). This attributes the tail to Unity audio cleanup, not the managed VCR callback or IL2CPP finalizer; the exact reason for the flush wait and whether it is avoidable remain open. These are process diagnostics, not OBS, hardware, or M2 evidence. The candidate is **not published**: there is no alpha.5 tag or GitHub Release. See `docs/IL2CPP_RELEASE_CHECKLIST.md` for the item-by-item evidence boundary.

Independent CI verification also passed on `a9aab228`: Windows IL2CPP run [39734065562](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39734065562) built a non-Development `0.0.0-alpha.0` artifact, passed the packaged smoke (exit 0, 5,359.0 ms), and passed the verifier step; native Windows/macOS Mono smoke run [39733680939](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39733680939) and P0-P13 run [39733680901](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/39733680901) also passed. These are process/provenance checks, not GUI/OBS/hardware acceptance.

## Startup policy

The integrated alpha scene includes these physical/external input components but starts them disabled:

- MediaPipe webcam tracking
- ARKit/iFacialMocap UDP receiver
- VMC UDP receiver
- VMC UDP sender

They remain available to the runtime/UI and can be enabled later when the corresponding equipment or external application is available.

The P13 2D runtime host is also included. It starts disabled because no production Live2D/Inochi2D backend has been accepted yet.

## Included runtime surface

The alpha scene assembles VRM character load/reload/unload; one-character scene lifecycle; renderer, camera, light and transparent overlay runtime; application UI; desktop VRM file selection; MediaPipe, ARKit/iFacialMocap and VMC implementations; tracking router; motion/expression mixer; manual expression source; material override controller; Appearance/outfit/accessory runtime; appearance transition executor; normalized event ingress; event runtime and current action handlers; environment runtime; diagnostics; and the P13 backend-neutral 2D runtime host.

## Build on Windows

Requirements are Unity 6000.3.25f1, a checkout of the alpha source snapshot, and PowerShell.

Run:

~~~powershell
./tools/build-alpha.ps1
~~~

The script bootstraps the pinned MediaPipe 0.16.3 package when missing, creates a fresh integrated alpha runtime scene, builds a Windows x64 Development Player with the **IL2CPP** scripting backend, and packages the output as a ZIP for local diagnostics. The release workflow is different: it uses `VCR.Editor.Alpha.AlphaBuildMenu.BuildWindowsReleaseCandidateFromMenu` with `BuildOptions.None` (non-Development), writes to `Builds/ReleaseCandidates/<version>/Windows`, and requires `tools/verify-windows-il2cpp-release.py` before upload/publication. The Unity installation must include the Windows IL2CPP module; the committed project default is also IL2CPP. The P0-P13 source-free validation chain is intentionally a separate evidence pass so Editor-only validation defects cannot prevent creation of an alpha binary that is needed to test the runtime itself.

Expected outputs:

~~~text
Builds/Alpha/0.1.0-alpha.4/Windows/VirtualCharacterRender.exe
Builds/Alpha/VirtualCharacterRender-0.1.0-alpha.4-Windows-x64.zip
~~~

Run the non-hardware validation chain separately:

~~~powershell
./tools/validate-alpha-source-free.ps1
~~~

## Launch

Without a model:

~~~powershell
./tools/run-alpha.ps1
~~~

With a VRM:

~~~powershell
./tools/run-alpha.ps1 -Vrm "C:\models\avatar.vrm"
~~~

With an existing runtime configuration:

~~~powershell
./tools/run-alpha.ps1 -Vrm "C:\models\avatar.vrm" -Config "C:\configs\vcr-runtime-config.json"
~~~

## First-pass test scope without camera or ARKit

Run these before enabling any physical tracking source:

1. application starts and exits cleanly with no character loaded;
2. all application UI sections open and remain responsive;
3. VRM load, unload and reload;
4. 720p60 / 1080p60 target switching;
5. render scale, FPS, VSync and run-in-background controls;
6. transparent/topmost/click-through output controls;
7. camera projection/output settings that do not require capture hardware;
8. manual expression controls through the Motion / Expression UI;
9. material slot discovery after VRM load, runtime override and restore;
10. environment state switching and transition behavior;
11. Appearance outfit/accessory/preset controls when the loaded VRM provides the required appearance hierarchy;
12. event rule editing, persistence and locally triggerable action-dispatch paths;
13. diagnostics snapshot, metric paging, console output and CSV evidence controls;
14. runtime configuration save/load and relaunch;
15. repeated character replacement and shutdown cleanup;
16. inherited VMC/OSC parser and protocol source-free tests;
17. P13 2D host lifecycle and parameter-mapping source-free checks.

## Deferred from this first alpha pass

Do not mark these PASS from the initial alpha run:

- physical webcam capture
- MediaPipe tracking quality and frame-time cost on a real camera
- low-light camera quality
- physical ARKit/iFacialMocap stream
- ARKit versus MediaPipe live fallback behavior
- external VMC Performer/Marionette interoperability
- OBS capture evidence
- final 720p60 / 1080p60 performance evidence
- production 2D model rendering

Transparent-window behavior itself can be exercised without a camera, but OBS evidence remains a separate platform validation item.

## Failure reporting

For every failure, record the alpha version, commit SHA, Windows version/GPU, Unity build result when relevant, whether a VRM was loaded, the exact action that failed, the Console/player log excerpt, and reproduction steps.

The first alpha goal is to turn the remaining source-only assumptions into concrete compile, package-resolution, player-runtime, VRM, UI and desktop-output evidence before physical tracking validation begins.
