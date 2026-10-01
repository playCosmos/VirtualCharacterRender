# ADR-0020: External broadcast events through normalized adapters

- Status: Accepted
- Date: 2026-10-01

## Context

The event system must react to away/presence state, broadcast chat, donations/support, commands, and other platform-specific interactions without coupling the runtime to one streaming service.

## Decision

All broadcast/platform integrations are event-source adapters that emit normalized events.

The event runtime consumes normalized events and drives generic actions targeting character, environment, camera, materials/shaders, props, audio, and effects.

Credentials and platform-native payloads remain inside adapters/integration configuration.

## Consequences

- streaming platforms can be added/replaced independently
- events can be tested using synthetic/local sources
- event graphs do not need service-specific nodes for every basic action
- adapters require reconnect, deduplication, queue/rate-limit, and credential policies

## Revisit conditions

Platform-specific advanced capabilities may expose extension payloads, but normalized core event semantics remain the default contract.
