# Event System

## Purpose

The event system turns broadcast, presence, application, and external events into character, environment, camera, shader, audio, prop, and effect actions.

It is optional. A basic avatar session does not require the event runtime to be active.

## Event sources

Initial source categories include:

### Presence / local state

- away / AFK
- return from away
- idle duration threshold
- tracking lost/restored
- microphone activity/silence
- application state
- timers

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
External/Local Event
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

## Away / AFK

Away is treated as a first-class local state rather than a hardcoded animation.

Example:

```text
No presence activity for N minutes
    ↓
AFK state entered
    ↓
Idle expression
    + optional seated/sleep motion
    + environment lighting change
    + optional overlay notice
```

Return events can restore previous state or trigger a configured transition.

## Reliability and safety

Event adapters must:

- handle duplicates where source IDs permit
- tolerate reconnect/replay
- avoid blocking render thread
- enforce rate limits/queues
- expose adapter health
- make dropped events visible in diagnostics
- keep secret credentials outside scene/profile assets

## Initial implementation order

1. normalized event envelope
2. internal/local test source
3. AFK/presence source
4. WebSocket/OSC event injection
5. one broadcast chat adapter
6. one donation/support adapter
7. event runtime conditions/actions
8. visual node editor later

The event runtime is implemented before the visual graph editor.
