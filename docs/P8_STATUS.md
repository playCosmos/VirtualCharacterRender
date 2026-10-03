# P8 Status

Updated: 2026-10-03

## Active branch

```text
feature/p8-protocol-event-adapters
```

P8 starts from the preserved P7 source checkpoint:

```text
checkpoint/p7-source-implementation
3e43f84167dd0c998d4f218937b366068162182a
```

P0-P7 runtime/device/network evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## Reused foundations

P8 does not replace the existing protocol/event foundations:

- bounded OSC codec remains shared by VMC and generic OSC input
- VMC UDP receive/send remains the avatar-motion interoperability path
- `NormalizedEventHub` remains the bounded network/device ingress queue and main-thread dispatcher
- `TrackingPresenceEventAdapter` remains the source of tracking-derived subject/source lost/restored events

Tracking disappearance continues to mean tracking-subject loss, not AFK.

## External normalized-event ingress

`NormalizedEvent` now includes optional `ActorName` while preserving `ActorId`.

`NormalizedEventIngressValidator` protects external ingress:

- canonical type/source token validation
- payload size limits
- finite non-negative amounts
- required currency/unit when an amount exists
- external sequence must remain zero because main-thread dispatch owns sequence assignment
- external adapters cannot inject `tracking.*` events

External source ids and receive timestamps are assigned by the local adapter rather than trusted from remote payloads.

## Generic OSC event input

P8 adds the VCR OSC address:

```text
/vcr/event type [actorId] [text] [amount] [currency] [actorName]
```

`OscNormalizedEventMapper` maps only this address into normalized events and applies the external-ingress rules.

`OscNormalizedEventUdpReceiver` provides the concrete optional UDP transport:

- default port 39540, separate from VMC 39539
- loopback-only sender acceptance by default
- optional explicit LAN IPv4 sender
- OSC parsing on a background thread
- bounded internal event queue
- main-thread-only publication to `INormalizedEventSink`
- bounded dispatch per frame
- oldest-event dropping under sustained overload
- packet/malformed/accepted/rejected/dropped/dispatched/rejected-sender diagnostics
- no receive work while the component is disabled

## WebSocket event injection boundary

P8 defines version 1 of the transport-neutral WebSocket event message:

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

`WebSocketEventProtocol` validates and normalizes this message.

`WebSocketEventInjectionAdapter`:

- accepts complete WebSocket text messages through `IWebSocketTextMessageHandler`
- uses Unity JSON decoding
- forces a local gateway source id and monotonic receive timestamp
- publishes only validated normalized events
- exposes accepted/rejected diagnostics
- performs no socket polling itself

A concrete WebSocket listener/client transport has **not** yet been selected or claimed complete. The message/handler contract is intentionally transport-neutral so the transport can be chosen from measured Windows/macOS behavior without changing normalized-event semantics.

## SOOP chat and donation bridge

P8 introduces a VCR-owned bridge schema for SOOP:

```text
version
type = chat | donation
eventId
userId
nickname
text
count
```

The core runtime does not own SOOP credentials, login/session state, reconnect policy, or raw platform packet formats.

`SoopBridgeEventMapper` maps:

- `chat` -> `broadcast.chat.message`
- `donation` -> `broadcast.donation`
- donation `count` -> normalized amount
- normalized unit -> `SOOP_STAR_BALLOON`
- user id -> ActorId
- nickname -> ActorName

`SoopBridgeEventAdapter` adds:

- bounded message-size validation
- event-id duplicate suppression for reconnect/replay cases
- accepted/rejected/duplicate/chat/donation diagnostics
- publication only through `INormalizedEventSink`

Direct SOOP API authentication/transport remains an adapter/connector responsibility and requires separate real-service validation.

## Source-free validation

Interactive:

```text
VCR > P8 > Validate Protocol Event Adapters
```

Batch:

```text
tools/validate-p8-source-free.ps1
tools/validate-p8-source-free.sh
```

The P8 suite runs P0-P7 checks first and then covers:

- external event-ingress limits
- external tracking-event spoof rejection
- WebSocket version/op/source/timestamp normalization
- WebSocket invalid/tracking-event rejection
- OSC chat/donation mapping
- OSC wrong-address/tracking-event rejection
- OSC receiver queue -> main-thread dispatch path
- SOOP chat/donation mapping
- SOOP invalid donation rejection
- SOOP duplicate event-id suppression
- WebSocket/OSC/SOOP diagnostics

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Remaining P8 source work

Before calling P8 source-checkpoint-ready:

- select and implement a concrete WebSocket transport for Windows/macOS
- define its listen/connect security posture and lifecycle
- connect that transport only through `IWebSocketTextMessageHandler`
- add source-free lifecycle/backpressure tests for the selected transport

## Deferred P8 evidence

- execute the P0-P8 Unity source-free batch suite
- external VMC application interoperability
- actual UDP OSC sender interoperability and overload measurements
- actual WebSocket client/server interoperability on Windows and macOS
- real SOOP bridge/direct-connector chat and donation evidence
- reconnect/replay behavior against real SOOP sessions
- network load and frame-time attribution on target hardware

P8 is active; the normalized ingress and OSC/SOOP adapter slices are implemented, while concrete WebSocket transport remains open.
