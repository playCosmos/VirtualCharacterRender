# Alpha Testing

## Release

Current alpha candidate: 0.1.0-alpha.1.

Immutable source snapshot: tag `v0.1.0-alpha.1` at commit `d739002abc99ac9c6adf5edfed0595aa4aa7878b`. The historical `release/0.1.0-alpha.1` branch may contain later workflow-retirement commits; build provenance follows the tag, not a mutable branch tip.

This alpha is a full-feature integration build of the current develop runtime. No existing camera, ARKit, VMC, rendering, UI, appearance, event, material, environment, diagnostics, or 2D-host implementation is compiled out for the alpha.

The first validation pass intentionally does not require physical webcam or ARKit/iFacialMocap hardware. That is a validation-scope decision only, not a product-feature removal.

## Build evidence

The most recent successful complete alpha binary workflow is [run 37933298925](https://github.com/playCosmos/VirtualCharacterRender/actions/runs/37933298925), with Unity `6000.3.25f1` at tagged source commit `d739002abc99ac9c6adf5edfed0595aa4aa7878b`.

Both platform builds, ZIP packaging, and release attachment succeeded:

- Windows x64 Development build: `VirtualCharacterRender-0.1.0-alpha.1-Windows-x64.zip` (94,912,033 bytes; SHA-256 `fa35c62520fe399fb2561c75fdee193675eecf6d631300b33b67fe824c8f7631`)
- macOS Development build: `VirtualCharacterRender-0.1.0-alpha.1-macOS.zip` (105,031,040 bytes; SHA-256 `c3bfc5daf590367b19163d53dc2e1e0f7ad690b954cfc81d4f6619dec58beb3b`)

The earlier successful run `37583662055` remains historical evidence, not evidence that its exact archives are still the ones published. The SHA-256 values above are GitHub's published asset digest fields; independent download/re-hash and player runtime testing remain separate evidence.

This closes compile/package-resolution and standalone binary-generation evidence for the tagged alpha snapshot. It does not close runtime behavior, hardware tracking, OBS, signing/notarization, or performance evidence. New alpha versions follow `docs/RELEASE_PROVENANCE.md`.

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

The script bootstraps the pinned MediaPipe 0.16.3 package when missing, creates a fresh integrated alpha runtime scene, builds a Windows x64 Development Player, and packages the output as a ZIP. The P0-P13 source-free validation chain is intentionally a separate evidence pass so Editor-only validation defects cannot prevent creation of an alpha binary that is needed to test the runtime itself.

Expected outputs:

~~~text
Builds/Alpha/0.1.0-alpha.1/Windows/VirtualCharacterRender.exe
Builds/Alpha/VirtualCharacterRender-0.1.0-alpha.1-Windows-x64.zip
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
