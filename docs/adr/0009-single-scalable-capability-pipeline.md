# ADR-0009: Single scalable capability pipeline

- Status: Accepted
- Date: 2026-10-01

## Context

The product must cover both lightweight VSeeFace-class usage and advanced Warudo-class scenes and interactions. Separate runtimes would duplicate character, tracking, shader, protocol, and serialization logic and create compatibility drift.

## Decision

Use one runtime pipeline with capability-driven, lazily initialized subsystems.

Profiles such as Lightweight, Standard, and Advanced are capability presets and resource budgets, not separate implementations.

Core flow remains:

```text
Inputs → Normalized State → Mixers/Runtime → Character/Scene → Rendering → Output
```

Advanced systems attach to the same state and service boundaries used by lightweight sessions.

## Consequences

- simple sessions avoid loading unused advanced services
- advanced sessions can add scene/event/plugin features without replacing the core runtime
- profiling must measure subsystem activation cost
- capabilities require explicit dependencies and lifecycle management
- UI can scale from simple to advanced without changing stored character data

## Validation

P0/P1 must demonstrate:

- minimal single-character session with optional subsystems disabled
- advanced capabilities can be enabled without changing character/runtime contracts
- disabled protocol/event/tracking services consume no meaningful recurring work
- profile switching cannot invalidate persistent character configuration

## Revisit conditions

A separate process or service may be introduced for isolation/performance, but it must implement the same contracts and must not create a separate product data model.
