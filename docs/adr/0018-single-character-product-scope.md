# ADR-0018: Single active character product scope

- Status: Accepted
- Date: 2026-10-01

## Context

The product is intended for one performer/avatar. Multi-character scenes and multi-person webcam tracking would significantly expand identity assignment, tracking, UI, performance, and test scope.

## Decision

Design and validate the product for exactly one active character.

Do not implement:

- multi-person webcam identity tracking
- multiple simultaneous performer bindings
- multi-character scene orchestration
- multi-character performance guarantees

The architecture should avoid irreversible assumptions that make future expansion impossible, but future multi-character support is not a current feature requirement.

## Consequences

- tracking source selection is simpler
- performance budgets focus on richer quality for one avatar
- scene/event systems target one primary character plus environment/props/effects
- internal identifiers should still avoid relying on a hard-coded singleton object where inexpensive to do so

## Revisit conditions

A future product requirement may introduce multi-character support through a new ADR and dedicated performance/tracking design.
