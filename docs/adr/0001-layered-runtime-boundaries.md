# ADR-0001: Layered runtime boundaries

- Status: Accepted
- Date: 2026-10-01

## Context
VirtualCharacterRender combines model loading, tracking, motion, expressions, shaders, events, protocols, and broadcast output. Direct coupling would make source/backend replacement expensive and weaken failure containment.

## Decision
Use explicit directional boundaries:

```text
Input → Normalized State → Runtime/Mixers → Rendering Abstraction → Backend → Output
```

External protocols and tracking adapters cannot directly mutate backend renderer objects.

## Consequences
Positive:
- tracking sources can be replaced or mixed
- backend details stay localized
- protocol APIs can remain stable
- runtime logic can be tested without a live renderer
- a future 2D backend can reuse upstream layers

Cost:
- more interfaces and data translation
- backend-specific features need deliberate abstraction extensions

## Revisit conditions
Revisit only if measured latency or complexity from the boundary is material and cannot be solved with batching, immutable shared data, or specialized adapters.
