# ADR-0020: External broadcast events through normalized adapters

- Status: Accepted
- Date: 2026-10-01

## Context

The event system must react to tracking-derived performer presence, broadcast chat, donations/support, commands, and other platform-specific interactions without coupling the runtime to one streaming service.

Performer presence is defined separately by ADR-0021 and is not a timer-based AFK assumption.

## Decision

All broadcast/platform integrations are event-source adapters that emit normalized events.

Tracking-derived subject-presence events enter the same event runtime through their own local/tracking event source.

The event runtime consumes normalized events and drives generic actions targeting character, environment, camera, materials/shaders, props, audio, and effects.

Credentials and platform-native payloads remain inside adapters/integration configuration.

## Consequences

- streaming platforms can be added/replaced independently
- tracking presence and broadcast events share one action pipeline without sharing source semantics
- events can be tested using synthetic/local sources
- event graphs do not need service-specific nodes for every basic action
- adapters require reconnect, deduplication, queue/rate-limit, and credential policies

## Revisit conditions

Platform-specific advanced capabilities may expose extension payloads, but normalized core event semantics remain the default contract.
