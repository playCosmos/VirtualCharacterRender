# ADR-0007: Transparent window as initial broadcast output

- Status: Proposed
- Date: 2026-10-01
- P0 implementation updated: 2026-10-02

## Context

VirtualCharacterRender is intended to act as a local virtual-character rendering overlay for broadcasting on Windows and macOS. The minimum output path should be easy to use and should not require a separate video transport.

## Proposed decision

Use a transparent desktop window with an OBS-compatible capture workflow as the initial production output concept on both platforms.

The shared runtime exposes `IOverlayOutputAdapter`. Native window/compositor implementation is platform-specific.

High-performance native transports are optional adapters rather than core dependencies; examples may include Windows-specific Spout-class output or macOS-specific Syphon-class output after separate validation.

Virtual camera and NDI remain optional future outputs.

## P0 candidate implementation

P0 uses UniWindowController 0.9.8, pinned by Git tag `v0.9.8`, behind `UniWinCOverlayOutput`.

The dependency is an implementation candidate, not a shared runtime contract.

Rendering and window ownership remain separate:

- `DesktopRenderBootstrap`: resolution/frame pacing/camera render baseline
- `UniWinCOverlayOutput`: transparency/topmost/click-through/native window status

### URP

P0 alpha path:

- URP Alpha Processing enabled
- SDR RGBA8 path
- camera HDR disabled
- Solid Color clear with alpha 0

### Windows

P0 transparent-window path:

- Windows x86-64
- explicit D3D11
- D3D12 excluded for this output validation path
- D3D11 flip-model swapchain disabled
- BitBlt presentation path
- OBS Game Capture with transparency enabled

This is an output-path constraint, not a general claim that D3D12 is unsuitable for rendering.

### macOS

P0 uses the same adapter boundary with the dependency's macOS native implementation and Metal.

Retina scaling, multi-monitor behavior, sleep/wake, relaunch, and OBS macOS capture must be validated on M1+.

### Click-through

Automatic opacity/pixel hit testing is disabled in the lightweight baseline. Explicit click-through is used instead.

This avoids the dependency's heavier per-pixel hit-test path. Remaining native-controller overhead is measured rather than assumed zero.

## P0 validation

On Windows and macOS independently:

- alpha correctness
- opaque/50%/25%/overlap test pattern
- straight vs premultiplied-alpha behavior
- VRM hair/outline edge artifacts
- OBS-compatible capture reliability
- high-DPI/Retina and multi-monitor behavior
- resize/window behavior
- click-through/topmost policy
- performance at 720p60 and 1080p60
- interaction with post effects and outlines
- shutdown/relaunch behavior
- sleep/wake where practical

Static project configuration is not sufficient to accept this ADR.

## Consequences if accepted

- simple local broadcast workflow
- platform compositor/window behavior becomes part of the support surface
- output implementation remains replaceable behind a common interface
- advanced platform-specific output paths remain possible

## Revisit conditions

Promote another output transport if transparent-window capture on either target platform is unreliable, loses alpha fidelity, or creates unacceptable overhead.
