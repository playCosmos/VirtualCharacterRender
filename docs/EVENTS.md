# Event Runtime

## Purpose

Events enter the application through normalized adapters. Tracking, broadcast chat, donations/support, manual controls, protocols, and future platform integrations do not call renderer objects directly.

P8 extends the source/event boundary with validated external WebSocket/OSC ingress and a SOOP chat/donation bridge. P9 now implements rule execution and application-level action dispatch; the visual node editor remains a later P12 concern.

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

## Tracking flow

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
EventRuntimeHost
        ↓
Filter / Condition
        ↓
Transform / State
        ↓
EventActionCommand
        ↓
IEventActionHandler
```

External adapters feed the same `INormalizedEventSink` boundary:

```text
WebSocket bridge client / OSC UDP / SOOP bridge
                    ↓
             ingress validation
                    ↓
              NormalizedEvent
                    ↓
        NormalizedEventHub bounded queue
                    ↓
          main-thread event dispatch
```

External adapters cannot inject `tracking.*` event types. Tracking-derived events remain owned by `TrackingPresenceEventAdapter`.

## Payload

The initial normalized payload carries:

- canonical type
- source ID
- monotonic timestamp
- sequence assigned at main-thread dispatch
- optional actor ID
- optional actor display name
- optional text
- optional amount/currency

Service-specific credentials and raw payloads remain inside their adapters/connectors. External source IDs and receive timestamps are assigned locally rather than trusted from remote payloads.

## Queue policy

External/network producers may publish from non-main threads into `NormalizedEventHub`.

The hub:

- uses a bounded queue
- dispatches a bounded number per frame
- drops the oldest queued events under sustained overload
- reports queue/dispatched/dropped counters through runtime diagnostics

This prevents chat/donation bursts from monopolizing render time.

## Action layer

The P9 event runtime consumes normalized events and targets application-level concepts:

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

Rules emit only application-level `EventActionCommand` values. Concrete handlers now include environment, camera, material/shader, expression/motion, appearance, effect, and audio actions. Quick-change audio uses `audio.play` / `audio.stop` with a logical audio id in command text; `audio.play` may optionally carry a normalized volume in `Value`. `motion.play` / `motion.release` route through one handler that can select procedural or baked AnimationClip cue runtimes by explicit runtime id or unique cue id. Event handlers may also implement `IEventActionCompletionProbe`; the appearance transition bridge uses it for Blocking actions. Built-in motion, effect, and audio handlers expose completion state without leaking their Unity objects into transition definitions. Rule definitions never receive a Unity `GameObject`, `Material`, `AudioSource`, `AnimationClip`, Camera, native window handle, or streaming-service SDK object.

`expression.set` writes only to a `ManualExpressionLayerSource`; the source has no recurring Update loop and does not implement performer presence. It is intended to enter `MotionExpressionMixer` as an expression-only overlay, with Maximum blending available to preserve routed lip-sync/eye channels.

The engine also provides typed runtime state, state conditions/mutations, numeric scale/offset transforms, per-rule cooldown, ordered rule evaluation, stop-after-match, and a bounded command count per input event. Unknown or failed action commands are contained and surfaced through diagnostics rather than falling through to direct scene mutation.
