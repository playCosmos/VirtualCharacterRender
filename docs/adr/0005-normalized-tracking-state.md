# ADR-0005: Normalized tracking state

- Status: Accepted
- Date: 2026-10-01

## Context
Potential tracking sources differ in names, coordinate systems, update rates, confidence data, transport, and body coverage.

## Decision
Every tracking source passes through an adapter and writes source-independent normalized state. Motion/expression mixers consume normalized state rather than device-specific payloads.

Source-specific extensions may be retained as optional metadata but cannot be required by the core renderer.

## Consequences
- supports source replacement and body-region mixing
- creates a stable testable runtime input
- requires coordinate/range normalization policies
- introduces one translation step

## Revisit conditions
Extend the normalized schema when real devices expose valuable data that cannot be represented without destructive loss. Do not bypass it merely for convenience.
