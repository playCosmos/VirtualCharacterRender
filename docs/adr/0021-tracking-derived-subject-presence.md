# ADR-0021: Tracking-derived subject presence

- Status: Accepted
- Date: 2026-10-01

## Context

The intended "away" behavior is not a timer-based AFK state. It occurs when the active performer is no longer detected by the configured webcam or ARKit-compatible tracking input.

Transient occlusion, low confidence, camera disconnection, and actual subject absence are different conditions and must not be collapsed into one event.

## Decision

Derive performer-presence events from tracking state.

Core presence events:

- `SubjectLost` — the performer is no longer reliably detected after a configurable grace period.
- `SubjectRestored` — the performer is reliably detected again after a configurable recovery/stability period.
- `TrackingSourceLost` — the input device, network source, or adapter itself became unavailable.
- `TrackingSourceRestored` — the tracking source became available again.

`SubjectLost` / `SubjectRestored` are the basis for the product's "자리비움 / 복귀" event behavior.

A time-based inactivity/AFK feature, if added later, is a separate event source and must not reuse subject-presence semantics.

## Detection policy

Presence derivation may use:

- subject detection validity
- face/head tracking confidence
- upper-body detection when available
- source-specific validity signals
- configurable lost grace period
- configurable restore stability period

The exact default timing thresholds are determined by tracking validation rather than hard-coded into the architecture.

## Consequences

- brief occlusion does not have to trigger an away scene
- device/network failure can be handled differently from the performer leaving
- webcam and ARKit-compatible sources expose consistent presence semantics
- event graphs can react to performer presence without knowing tracking implementation details

## Validation

- brief tracking jitter/occlusion does not generate repeated lost/restored events
- actual performer departure generates one stable `SubjectLost`
- performer return generates one stable `SubjectRestored`
- camera/mobile-source disconnect generates `TrackingSourceLost`, not `SubjectLost` alone
- presence-state processing does not block the render loop

## Revisit conditions

Default grace/stability timing and source-specific confidence rules may change after tracking measurements without superseding this decision.
