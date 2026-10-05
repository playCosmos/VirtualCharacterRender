# P8 Status

Updated: 2026-10-05

## Active branch

Current integrated source of truth:

```text
develop
```

The P8 feature/checkpoint branches are historical references. P8 originally starts from the preserved P7 source checkpoint:

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
- destroyed Unity event-sink references are rejected; the bounded resolver can discover a live replacement
- oldest-event dropping under sustained overload
- packet/malformed/accepted/rejected/dropped/dispatched/rejected-sender diagnostics
- no receive work while the component is disabled

## WebSocket event injection and bridge transport

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

`WebSocketEventClientTransport` provides the concrete optional transport using the .NET `ClientWebSocket` API:

- default endpoint `ws://127.0.0.1:39541/vcr/events`
- plaintext `ws://` allowed only for loopback
- remote endpoints require `wss://`
- URI user-info credentials are rejected
- asynchronous receive with fragmented-text assembly
- binary/oversize/invalid UTF-8 rejection
- bounded text-message queue
- main-thread-only delivery to `IWebSocketTextMessageHandler`
- bounded dispatch per frame
- destroyed Unity message-handler references are rejected; the bounded resolver can discover a live replacement
- configurable automatic reconnect delay
- generation-guarded restart so an old async loop cannot overwrite a new connection state
- transport connection/receive/drop/dispatch/rejection diagnostics
- no third-party WebSocket package dependency

This is an outbound bridge-client transport, not a public listener/server. A future application-control server, if required, remains a separate protocol surface rather than being implied by this event-ingress client.

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
- WebSocket endpoint security rules
- WebSocket bounded queue -> main-thread handler delivery
- WebSocket transport diagnostics
- OSC chat/donation mapping
- OSC wrong-address/tracking-event rejection
- OSC receiver queue -> main-thread dispatch path
- SOOP chat/donation mapping
- SOOP invalid donation rejection
- SOOP duplicate event-id suppression
- WebSocket/OSC/SOOP diagnostics
- replacement WebSocket handler and OSC sink recovery after the previous Unity component is destroyed

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Source implementation scope

The P8 source scope now covers:

- inherited VMC receive/send interoperability contracts
- generic OSC normalized-event injection with a concrete UDP receiver
- WebSocket v1 normalized-event injection with a concrete outbound client bridge
- tracking-derived subject/source event adaptation
- SOOP chat/donation bridge mapping with duplicate suppression
- bounded network-to-main-thread queues and diagnostics

The source architecture is complete enough for a checkpoint. Direct real-service/network evidence remains deferred.

## Deferred P8 evidence

- execute the P0-P8 Unity source-free batch suite
- external VMC application interoperability
- actual UDP OSC sender interoperability and overload measurements
- actual WebSocket bridge-client interoperability on Windows and macOS
- decide separately whether a public/local WebSocket listener API is required for P11/P12 application control
- real SOOP bridge/direct-connector chat and donation evidence
- reconnect/replay behavior against real SOOP sessions
- network load and frame-time attribution on target hardware

P8 source implementation is checkpoint-ready, but source-free/network/real-service evidence is not marked PASS.
