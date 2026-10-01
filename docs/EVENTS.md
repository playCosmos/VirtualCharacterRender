# Event Runtime

## Purpose

Events enter the application through normalized adapters. Tracking, broadcast chat, donations/support, manual controls, protocols, and future platform integrations do not call renderer objects directly.

The P0 implementation establishes the source/event boundary and bounded dispatch path. Action execution and the visual node editor remain later phases.

## Canonical P0 event types

```text
tracking.subject_lost
tracking.subject_restored
tracking.source_lost
tracking.source_restored
broadcast.chat.message
broadcast.donation
local.manual
```

### Tracking disappearance is not AFK

`tracking.subject_lost` means the tracking presence resolver has concluded that the performer/subject disappeared from available tracking evidence after the configured grace period.

It does **not** mean:

- an inactivity timer expired
- the user manually selected an AFK state
- the camera/network source disconnected

A source/device failure is emitted separately as `tracking.source_lost`.

## P0 flow

```text
TrackingPresenceResolver
        ↓
TrackingPresenceEventAdapter
        ↓
NormalizedEvent
        ↓
NormalizedEventHub bounded ingress queue
        ↓
NormalizedEventBus main-thread dispatch
        ↓
future Event Runtime actions
```

Broadcast adapters will feed the same `INormalizedEventSink` boundary.

## Payload

The initial normalized payload carries:

- canonical type
- source ID
- monotonic timestamp
- sequence assigned at main-thread dispatch
- optional actor ID
- optional text
- optional amount/currency

Service-specific credentials and raw payloads remain inside their adapters.

## Queue policy

External/network producers may publish from non-main threads into `NormalizedEventHub`.

The hub:

- uses a bounded queue
- dispatches a bounded number per frame
- drops the oldest queued events under sustained overload
- reports queue/dispatched/dropped counters through runtime diagnostics

This prevents chat/donation bursts from monopolizing render time.

## Action layer

The later event runtime consumes normalized events and targets application-level concepts:

```text
Event
 ↓
Filter / Condition
 ↓
Transform / State
 ↓
Action
 ├ Character
 ├ Expression / Motion
 ├ Environment
 ├ Material / Shader parameter
 ├ Camera
 ├ Prop / Effect
 ├ Audio
 └ Overlay
```

No event node receives a Unity `GameObject`, `Material`, native window handle, or streaming-service SDK object as its core contract.
