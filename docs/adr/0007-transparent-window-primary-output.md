# ADR-0007: Transparent window as initial broadcast output

- Status: Proposed
- Date: 2026-10-01

## Context
VirtualCharacterRender is intended to act as a character rendering overlay for broadcasting. The minimum output path should be simple and local.

## Proposed decision
Use a transparent desktop window captured by OBS as the first production output path.

Spout2 is a secondary candidate. Virtual camera and NDI remain optional future outputs.

## P0 validation
- alpha correctness
- OBS capture reliability
- high-DPI and multi-monitor behavior
- resize/fullscreen behavior
- click-through/topmost policy
- performance at target frame rates
- interaction with URP post effects and outlines

## Consequences if accepted
- simple local broadcast workflow
- Windows compositor behavior becomes part of the support surface
- additional output paths remain possible

## Revisit conditions
Promote Spout2 or another output if transparent-window capture is unreliable, loses alpha fidelity, or creates unacceptable overhead.
