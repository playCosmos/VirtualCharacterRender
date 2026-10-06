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
HumanoidPoseState + NormalizedExpressionState
  ↓
PriorityTrackingRouter
  ↓
Vrm10HumanoidPoseTarget
```

Outbound:

```text
Vrm10MotionSnapshotProvider
  ↓
source-neutral humanoid pose / expressions
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

VMC receive frames advertise every payload domain they actually carry: a pose payload sets `TrackingRegion.FullBody`, an expression payload sets `TrackingRegion.Expressions`, and a combined packet sets both. This invariant is owned by `VmcFrameAccumulator`; downstream routing does not repair missing region flags.

VMC bone transforms are represented as source-neutral local humanoid transforms. VMC/Unity uses +X right, +Y up, +Z forward, matching the VCR coordinate convention for this domain. The full-body envelope does not falsely claim every VMC bone rotation is normalized: `HumanoidPoseSpace` explicitly marks `OriginalLocal` versus `NormalizedLocal`. On receive, `HumanoidPoseSpace.OriginalLocal` rotations are converted into the target UniVRM ControlRig normalized local-rotation space from the target model's captured initial posture. A compatibility switch accepts senders that explicitly transmit already-normalized ControlRig bones.

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

The existing bounded OSC codec remains shared with VMC.

P8 adds generic normalized-event injection on UDP 39540 by default:

```text
/vcr/event type [actorId] [text] [amount] [currency] [actorName]
```

`OscNormalizedEventUdpReceiver` is loopback-only by default, parses off the main thread, queues valid events in a bounded buffer, and publishes them to `INormalizedEventSink` on the main thread. VMC remains on its separate default UDP 39539 path.

External OSC cannot inject `tracking.*` event types.

## WebSocket

P8 freezes only the normalized-event ingress slice.

Version 1 message:

```json
{
  "version": 1,
  "op": "event.inject",
  "type": "local.manual",
  "actorId": "optional",
  "actorName": "optional",
  "text": "optional",
  "amount": 0,
  "currency": "optional",
  "hasAmount": false
}
```

`WebSocketEventClientTransport` is an outbound bridge client built on `ClientWebSocket`. It connects to `ws://127.0.0.1:39541/vcr/events` by default. Plain `ws://` is restricted to loopback; remote endpoints require `wss://`. Complete text messages are queued and delivered to `IWebSocketTextMessageHandler` on the Unity main thread.

The broader application-control API remains unfrozen. Future operations such as character loading, runtime parameter control, expressions/motion, environment/material control, diagnostics, and event subscriptions require a separate versioned surface rather than being silently added to `event.inject`.

## Broadcast integrations

Streaming-service chat, donation/support, and other interaction APIs are not core protocols.

P8's first service adapter is SOOP. The VCR runtime consumes a small bridge schema containing event id, user id, nickname, text, and donation count. SOOP credentials, login/session state, reconnect behavior, and raw service packets stay outside the normalized runtime. Chat maps to `broadcast.chat.message`; star-balloon support maps to `broadcast.donation` with unit `SOOP_STAR_BALLOON`. Replayed event ids are suppressed by the Unity bridge adapter.

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
