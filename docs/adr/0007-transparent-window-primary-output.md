# ADR-0007: Transparent window as initial broadcast output

- Status: Proposed
- Date: 2026-10-01

## Context

VirtualCharacterRender is intended to act as a local virtual-character rendering overlay for broadcasting on Windows and macOS. The minimum output path should be easy to use and should not require a separate video transport.

The product baseline is one character at 720p60 minimum and 1080p60 recommended. The output layer must therefore avoid unnecessary copies, per-pixel CPU work, and mandatory video-transport services when a transparent desktop window is sufficient.

## Proposed decision

Use a transparent desktop window with an OBS-compatible capture workflow as the initial production output concept on both platforms.

The shared runtime exposes an engine-independent `IOverlayOutputAdapter`. Native window/compositor objects remain inside the Unity/platform implementation.

### P0 implementation candidate

Use UniWindowController 0.9.8 as the first Windows/macOS native-window adapter, pinned through UPM.

Reasons:

- one Unity-facing API for Windows and macOS
- alpha transparency, topmost, click-through, position, and sizing already implemented
- MIT license
- materially lower implementation risk than maintaining separate Win32 and Cocoa native plugins during P0

This is a validation dependency, not a permanent architectural dependency. The shared output contract must allow the implementation to be replaced.

### Lightweight policy

The default P0 overlay path uses:

- alpha transparency, not chroma/color-key
- automatic per-pixel opacity hit testing disabled
- manual click-through state only
- transparent black camera clear
- HDR disabled for the baseline SDR alpha path
- URP Alpha Processing enabled
- no extra RenderTexture/video-copy path solely for desktop transparency

### Windows P0 policy

The transparent-window P0 path uses:

- Direct3D 11
- D3D12 excluded from the baseline graphics API list
- D3D11 flip-model swapchain disabled
- windowed mode
- run-in-background enabled

The D3D11 BitBlt path is accepted for P0 even if it is less efficient than flip-model presentation because native alpha-composited window validation has priority. Alternative output transports may later remove this constraint.

### macOS P0 policy

Use the same shared adapter contract and UniWindowController implementation candidate.

macOS remains independently gated for:

- alpha correctness
- Retina sizing
- click-through/topmost behavior
- resize/monitor changes
- OBS capture
- Apple Silicon M1 performance

A Windows PASS does not imply a macOS PASS.

## P0 implementation

Current branch now contains:

- engine-independent output settings/status/adapter contracts
- `UniWinCOverlayOutput` Unity adapter
- pinned UniWindowController 0.9.8 dependency
- explicit Unity UI dependency required by UniWindowController
- editor configuration command for the transparent-output baseline
- static validation command
- P0 alpha/translucency overlap test pattern
- output metrics integrated into generic runtime diagnostics

## P0 validation

Static/editor gate:

- transparent output adapter exists
- camera clears to alpha zero
- baseline HDR is disabled
- URP Alpha Processing is enabled
- automatic per-pixel hit testing is disabled
- Alpha transparency mode is selected
- Windows graphics API is explicit D3D11 only
- flip-model swapchain is disabled
- run-in-background is enabled

Standalone gate on Windows and macOS independently:

- true transparent background
- 100%, 50%, and 25% alpha reference patches appear correct
- overlapping semi-transparent patches have no obvious straight/premultiplied-alpha halo
- VRM hair/outline edges remain clean
- resize retains correct alpha
- high-DPI/Retina dimensions remain correct
- topmost behavior works
- manual click-through works
- OBS-compatible capture preserves expected alpha
- 720p60 target passes
- 1080p60 recommended target is measured
- shutdown/relaunch restores window behavior cleanly

## Consequences if accepted

- simple local broadcast workflow
- platform compositor/window behavior becomes part of the support surface
- output implementation remains replaceable behind a common interface
- advanced platform-specific output paths remain possible
- Windows transparent-window mode may retain a D3D11/BitBlt-specific path even if other renderer modes later use newer APIs

## Revisit conditions

Replace or supplement UniWindowController if:

- macOS alpha/compositor behavior is unreliable
- Windows D3D11/BitBlt overhead is unacceptable
- OBS alpha capture is unreliable
- Unity updates break the integration
- a lower-overhead native transport becomes necessary

Promote another output transport if transparent-window capture on either target platform is unreliable, loses alpha fidelity, or creates unacceptable overhead.
