# ADR-0004: Custom shaders as a first-class runtime feature

- Status: Accepted
- Date: 2026-10-01

## Context
Custom shader support is a required product capability for character/material styling and runtime-reactive effects. Adding it after material architecture is fixed would create avoidable redesign.

## Decision
Custom shaders are part of the core rendering architecture from the material-abstraction phase onward.

Required:
- per-material override
- parameter metadata
- runtime parameter changes
- external/runtime bindings
- presets
- package/plugin path
- diagnostics
- deterministic fallback material

Default VRM materials remain usable without custom shaders.

## Consequences
- material/shader abstractions exist early
- serialized profiles cannot assume MToon only
- shader failure handling is part of runtime reliability
- UI/API must support generic parameters

## Revisit conditions
The requirement itself remains. Backend-specific implementation can be superseded by later ADRs.
