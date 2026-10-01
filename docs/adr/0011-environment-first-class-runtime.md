# ADR-0011: Environment as a first-class runtime subsystem

- Status: Accepted
- Date: 2026-10-01

## Context

Dynamic backgrounds may evolve from images and video into parallax, 3D spaces, lighting, effects, and event-reactive environments. Treating them as a renderer-only background option would force later scene/event redesign.

## Decision

Model Environment as an independent scene subsystem with state, transitions, lighting, shader/effect bindings, update policy, and event targets.

A lightweight environment can still be only a static image.

## Consequences

- simple and advanced backgrounds share one interface
- event system can target environment state directly
- environment performance can be attributed independently
- scene code must not assume every environment is a 3D world

## Revisit conditions

Backend-specific implementation may change. The environment/runtime boundary remains unless superseded.
