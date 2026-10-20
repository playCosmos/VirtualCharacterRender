# Event System

## Purpose

The event system turns broadcast, tracking-presence, application, and external events into character, environment, camera, shader, audio, prop, and effect actions.

It is optional. A basic avatar session does not require the event runtime to be active.

## Event sources

Initial source categories include:

### Tracking-derived presence

- subject lost
- subject restored
- tracking source lost
- tracking source restored

The product's "자리비움 / 복귀" behavior is based on the performer disappearing from the active webcam or ARKit-compatible tracking source.

This is not a timer-based AFK concept.

A brief face occlusion or confidence dip should not immediately become a presence event. Presence state uses configurable grace/recovery timing and source confidence.

### Local/application state

- microphone activity/silence
- application state
- timers
- explicitly configured idle timers, if added later

Any future time-based AFK/idle event is separate from tracking-derived subject presence.

### Broadcast interaction

- chat message
- chat keyword/command
- subscription/follow-like platform events where integrations permit
- donation/support event
- donation amount/range
- gift/item event
- viewer interaction
- moderation/system event

Platform-specific integrations are adapters. The event runtime does not depend on a specific streaming platform.

### Runtime and protocol

- WebSocket
- OSC
- MIDI
- VMC-related runtime state where useful
- plugin-generated events
- scene/environment events

## Pipeline

```text
External / Tracking / Local Event
              ↓
         Source Adapter
              ↓
        Normalized Event
              ↓
      Filter / Condition
              ↓
      Transform / State
              ↓
            Action
```

## Event envelope

Every normalized event should contain:

- event type
- source ID/type
- timestamp
- stable event ID when available
- typed payload
- optional user/display metadata
- optional amount/value
- confidence/validity where relevant
- privacy/redaction flags where relevant

Platform-native raw payloads do not become the runtime contract.

## Actions

Actions may target:

- expression
- motion
- pose/procedural motion
- material/shader parameter
- environment state
- environment transition
- camera
- light
- prop
- effect/particle
- audio
- UI/overlay element
- timer/state variable

## Subject presence

Tracking adapters expose raw health/validity, while a presence-state layer derives stable performer-presence events.

```text
Tracking Frames
      ↓
Validity / Confidence
      ↓
Presence Debounce / Hysteresis
      ↓
SubjectLost / SubjectRestored
      ↓
Event Runtime
```

Example:

```text
Performer leaves camera/ARKit view
        ↓
Lost grace period satisfied
        ↓
SubjectLost
        ↓
Configured away motion
+ environment change
+ optional overlay notice
```

When the performer returns and remains stably detectable for the configured recovery interval, `SubjectRestored` is emitted.

Device/network loss is different:

```text
Camera disconnected / ARKit sender offline
        ↓
TrackingSourceLost
```

This allows the scene to react differently to the performer leaving versus the tracking hardware/source failing.

## Reliability and safety

Event adapters must:

- handle duplicates where source IDs permit
- tolerate reconnect/replay
- avoid blocking render thread
- enforce rate limits/queues
- expose adapter health
- make dropped events visible in diagnostics
- keep secret credentials outside scene/profile assets

Presence derivation must avoid event flapping during brief occlusion or low-confidence frames.

## Initial implementation order

1. normalized event envelope
2. internal/local test source
3. tracking-derived subject presence source
4. WebSocket/OSC event injection
5. one broadcast chat adapter
6. one donation/support adapter
7. event runtime conditions/actions
8. visual node editor later

The event runtime is implemented before the visual graph editor.
