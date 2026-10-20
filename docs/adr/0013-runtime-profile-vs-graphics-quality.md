# ADR-0013: Separate runtime capability profile from graphics quality

- Status: Accepted
- Date: 2026-10-01

## Context

Feature complexity and rendering quality are independent. A user may need advanced events on modest hardware or high-quality rendering in a simple avatar session.

## Decision

Do not use one preset to represent both feature activation and graphics quality.

```text
Capability/Profile settings
≠
Graphics quality settings
```

Graphics quality controls rendering cost. Capability settings control which systems exist and run.

## Consequences

- UI exposes distinct concepts
- stored scenes remain portable across quality levels
- performance diagnostics can distinguish feature and rendering cost

## Revisit conditions

None expected; only naming/preset structure may change.
