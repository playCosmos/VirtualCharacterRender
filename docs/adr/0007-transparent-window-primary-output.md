# ADR-0007: Transparent window as initial broadcast output

- Status: Proposed
- Date: 2026-10-01

## Context

VirtualCharacterRender is intended to act as a local virtual-character rendering overlay for broadcasting on Windows and macOS. The minimum output path should be easy to use and should not require a separate video transport.

## Proposed decision

Use a transparent desktop window with an OBS-compatible capture workflow as the initial production output concept on both platforms.

The shared runtime exposes an output abstraction. Native window/compositor implementation is platform-specific.

High-performance native transports are optional adapters rather than core dependencies; examples may include Windows-specific Spout-class output or macOS-specific Syphon-class output after separate validation.

Virtual camera and NDI remain optional future outputs.

## P0 validation

On Windows and macOS independently:

- alpha correctness
- OBS-compatible capture reliability
- high-DPI/retina and multi-monitor behavior
- resize/fullscreen behavior
- click-through/topmost policy
- performance at target frame rates
- interaction with post effects and outlines
- shutdown/relaunch behavior

## Consequences if accepted

- simple local broadcast workflow
- platform compositor/window behavior becomes part of the support surface
- output implementation remains replaceable behind a common interface
- advanced platform-specific output paths remain possible

## Revisit conditions

Promote another output transport if transparent-window capture on either target platform is unreliable, loses alpha fidelity, or creates unacceptable overhead.
