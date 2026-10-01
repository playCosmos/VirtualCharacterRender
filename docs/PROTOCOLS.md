# Protocols

## Purpose

Protocols expose application concepts, normalized tracking, and normalized event inputs without leaking backend implementation details.

The product operates on one active character.

## VMC

Primary interoperability candidate for avatar motion input/output.

P0 checks:

- root/bone transport
- expressions
- timing/update behavior
- sender/receiver coexistence
- coordinate conventions
- malformed/stale packet handling

VMC may provide optional full-body input, but full-body processing is not part of the baseline tracking budget.

## OSC

Generic integration path for external controls, runtime values, and events.

## WebSocket

Application control/event API candidate.

Initial conceptual operations:

- load/unload/query the active character
- get/set runtime parameters
- set expressions
- play/stop motion
- set material/shader parameters
- control camera/environment
- inject/query normalized events
- query health/diagnostics
- subscribe to events

The concrete API is not frozen during bootstrap.

## Broadcast integrations

Streaming-service chat, donation/support, and other interaction APIs are not core protocols.

Each service uses an adapter:

```text
Platform API / Local bridge
        ↓
Event Source Adapter
        ↓
Normalized Event
        ↓
Event Runtime
```

Credentials, reconnect logic, raw payloads, rate limits, and platform-specific semantics stay inside the adapter/integration layer.

## MIDI

Optional input for buttons, continuous controls, performance triggers, shader bindings, and events.

## Boundary

Do not expose backend calls such as `setUnityGameObject` or raw material pointers.

Prefer application commands such as `setCharacterTransform`, `setShaderParameter`, `setExpression`, `setEnvironmentState`, and `emitEvent`.

## Reliability

Protocol and event handlers validate payloads, enforce limits, avoid blocking the render loop, expose diagnostics, reject unsupported operations explicitly, and tolerate stale/disconnected clients.

## Versioning

Every externally persisted or remotely consumed protocol/schema receives an explicit version before stability is claimed.
