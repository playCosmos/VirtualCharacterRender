# ADR-0008: Windows and macOS as first-class desktop targets

- Status: Accepted
- Date: 2026-10-01

## Context

VirtualCharacterRender is intended to run as a desktop virtual-character renderer and overlay on both Windows and macOS. Treating macOS as a later port would allow Windows-specific renderer/output assumptions to leak into core interfaces.

## Decision

Windows and macOS are first-class targets from P0 onward.

Shared runtime/domain contracts remain platform-independent. Native windowing, capture, permissions, packaging, and GPU-specific behavior are implemented through platform/backend adapters.

## Consequences

Positive:

- portability constraints are tested before architecture hardens
- shared runtime behavior is consistent
- platform-specific output technologies do not become core dependencies

Cost:

- P0 validation matrix increases
- alpha/window/output behavior must be verified twice
- platform-specific packaging and diagnostics are required

## Revisit conditions

Additional desktop platforms may be added later. Removing either Windows or macOS support requires a superseding ADR.
