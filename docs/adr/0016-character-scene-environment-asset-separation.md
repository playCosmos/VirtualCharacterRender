# ADR-0016: Separate character, scene, and environment assets

- Status: Accepted
- Date: 2026-10-01

## Context

Character identity/configuration, scene arrangement, and environment content have different reuse and lifecycle requirements.

## Decision

Persist them as separate concepts.

- Character/Profile: model, expressions, material overrides, tracking bindings
- Environment: background/world content, states, lighting, transitions
- Scene: composition that references character, environment, camera, props, effects, and event configuration

The initial product scene references one active character.

## Consequences

- character can move between environments
- environments can be reused across scenes
- event and tracking settings do not need to be baked into model files
- migration/versioning can be scoped per asset type

## Revisit conditions

Packaging may bundle multiple assets for distribution while preserving logical separation.
