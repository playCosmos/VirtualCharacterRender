# Protocols

## Purpose
Protocols expose application concepts and normalized state without leaking backend implementation details.

## VMC
Primary interoperability candidate for avatar motion input/output.

P0 checks:
- root and bone transport
- expressions
- timing/update behavior
- sender/receiver coexistence
- coordinate conventions
- malformed/stale packet handling

## OSC
Generic integration path for external tools, controls, and events.

## WebSocket
Application control API candidate.

Initial conceptual operations:
- enumerate/load/unload characters
- get/set runtime parameters
- set expressions
- play/stop motion
- set material/shader parameters
- control camera
- query diagnostics
- subscribe to events

The concrete API is not frozen during bootstrap.

## MIDI
Optional input for buttons, continuous controls, performance triggers, shader bindings, and events.

## Boundary
Do not expose backend calls such as `setUnityGameObject` or raw material pointers. Prefer application commands such as `setCharacterTransform`, `setShaderParameter`, and `setExpression`.

## Reliability
Protocol handlers validate payloads, enforce limits, avoid blocking the render loop, expose diagnostics, reject unsupported operations explicitly, and tolerate stale/disconnected clients.

## Versioning
Every externally persisted or remotely consumed protocol/schema receives an explicit version before stability is claimed.
