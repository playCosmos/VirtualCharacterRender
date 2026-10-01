# ADR-0017: Separate character-secondary and world physics domains

- Status: Deferred
- Date: 2026-10-01

## Context

Hair/clothing secondary motion and environment/prop collision have different stability, update, and performance requirements.

## Proposed decision

Treat character secondary physics and optional environment/world physics as separate services with separate budgets and enable/disable controls.

Full-body tracking/IK is also independent from both physics domains.

## Validation before acceptance

- confirm VRM secondary-motion integration requirements
- measure fixed/update-loop interactions
- define cross-domain collision requirements
- define deterministic fallback when world physics is disabled

## Deferral

P0 does not require world physics or a custom secondary-physics service. UniVRM secondary motion may operate independently while environment/world physics remains disabled.

Resume this ADR when environment physics, props, advanced full-body integration, or cross-domain collision becomes an active implementation requirement.

## Revisit conditions

Merge only if the selected backend makes separation impractical without measurable benefit.
