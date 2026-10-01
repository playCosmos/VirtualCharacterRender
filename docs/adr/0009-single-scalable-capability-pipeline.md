# ADR-0009: Single scalable capability pipeline

- Status: Accepted
- Date: 2026-10-01

## Context

The product must cover both lightweight VSeeFace-class usage and Warudo-class advanced scene/event/environment behavior for one performer. Separate runtimes would duplicate character, tracking, shader, protocol, event, and serialization logic.

## Decision

Use one one-character runtime pipeline with capability-driven, lazily initialized subsystems.

Capability presets and graphics-quality presets are independent.

Core flow remains:

```text
Inputs
 → Normalized Tracking/Events
 → Mixers/Event Runtime
 → Character/Scene/Environment
 → Rendering
 → Output
```

Advanced systems attach to the same state/service boundaries used by lightweight sessions.

Multi-character operation is not an Advanced capability in the current product.

## Consequences

- simple sessions avoid loading unused advanced services
- advanced sessions add scene/environment/event/plugin features without replacing the core runtime
- profiling must measure subsystem activation cost
- capabilities require explicit dependencies/lifecycle management
- UI can scale in complexity without changing the active character data model

## Validation

P0/P1 must demonstrate:

- minimal one-character session with optional subsystems disabled
- advanced capabilities can be enabled without changing character/runtime contracts
- disabled protocol/event/tracking/full-body services consume no meaningful recurring work
- capability/quality setting changes do not invalidate persistent character configuration

## Revisit conditions

A future multi-character product requirement requires a separate ADR rather than being inferred from Advanced mode.

A separate process/service may be introduced for isolation/performance if it implements the same contracts and does not create a second product data model.
