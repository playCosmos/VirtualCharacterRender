# Protocols

## Purpose

Protocols expose application concepts, normalized tracking, and normalized event inputs without leaking backend implementation details.

The product operates on one active character.

## VMC

Primary interoperability candidate for avatar motion input/output.

Current P0 implementation:

```text
OSC/UDP
  ↓
bounded OSC codec
  ↓
VmcTrackingSource
  ↓
NormalizedHumanoidPose + NormalizedExpressionState
  ↓
PriorityTrackingRouter
  ↓
Vrm10HumanoidPoseTarget
```

Outbound:

```text
Vrm10MotionSnapshotProvider
  ↓
normalized pose / expressions
  ↓
VmcUdpSender
  ↓
OSC/UDP
```

Supported P0 receive messages:

- `/VMC/Ext/OK`
- `/VMC/Ext/T`
- `/VMC/Ext/Root/Pos`
- `/VMC/Ext/Bone/Pos`
- `/VMC/Ext/Blend/Val`
- `/VMC/Ext/Blend/Apply`

The OSC reader accepts only the argument types needed for this P0 path (`int32`, `float32`, UTF-8 string), accepts bundles, bounds packets to 16 KiB, and rejects malformed/unsupported packets rather than guessing.

VMC bone transforms are represented as source-neutral local humanoid transforms. VMC/Unity uses +X right, +Y up, +Z forward, matching the normalized VCR convention for this domain.

For VRM1 outbound compatibility, the snapshot provider reads original `target.Humanoid` bones rather than ControlRig bones. Standard expressions are sent using VRM0 VMC names by default (`Joy`, `A`, `Blink_L`, etc.); VRM1 expression names are an explicit sender option.

Default receiver security posture is loopback-only sender acceptance on UDP 39539. LAN sender acceptance requires an explicit IPv4 address.

If this process has an active VMC receiver on the same loopback destination port, the VMC sender refuses to start. Bidirectional local testing must use distinct receive/destination ports to avoid self-feedback.

VMC full-body input is optional and does not belong to the baseline MediaPipe performance budget.

P0 checks:

- root/bone transport
- expressions
- timing/update behavior
- sender/receiver coexistence
- coordinate conventions
- malformed/stale packet handling
- external application interoperability
- optional-service disabled cost

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
