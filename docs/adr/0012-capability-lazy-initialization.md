# ADR-0012: Capability-based lazy initialization

- Status: Accepted
- Date: 2026-10-01

## Context

The product must provide advanced features without making simple one-character sessions pay their recurring runtime cost.

## Decision

Optional capabilities initialize only when enabled or required by loaded content.

Disabled optional services should have no meaningful recurring frame cost.

## Consequences

- capability dependencies/lifecycle must be explicit
- startup paths become more modular
- diagnostics must show active services
- advanced features can coexist with a lightweight baseline

## Validation

Measure disabled and enabled cost for each major subsystem.

## Revisit conditions

A service may remain warm if profiling proves the cost negligible and reinitialization cost harms user experience; such exceptions must be documented.
