# ADR-0019: 3D-first product with 2D as a separate extension

- Status: Accepted
- Date: 2026-10-01

## Context

Supporting 2D and 3D equally from the first release would expand renderer, model, material, editor, and test scope.

## Decision

The primary product/runtime implementation is 3D-first.

2D support is a future backend/extension that should reuse applicable upstream systems such as event input, normalized tracking, protocols, and output abstractions without forcing the initial 3D renderer to share every rendering primitive.

## Consequences

- VRM/3D quality can be completed first
- 2D-specific rig/render concerns do not complicate P0/P1
- upstream contracts remain intentionally reusable

## Revisit conditions

Begin 2D implementation only after the 3D runtime and public extension boundaries are stable enough to evaluate Inochi2D/Live2D or another backend.
