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

The exact product defaults are determined by tracking validation rather than hard-coded into the architecture.

The current P0 implementation uses configurable engineering defaults only:

- subject-lost grace: 0.50 s
- restore stability: 0.15 s
- source callback stale threshold: 1.00 s

These are measurement starting points, not accepted product thresholds.

A fresh callback that reports no subject contributes to `SubjectLost`. A callback stream that itself becomes stale contributes to `TrackingSourceLost`. Loss of all sources makes subject state `Unknown`; source failure must not be misreported as performer absence.

## Character fallback

The P0 VRM target holds the last valid pose during the subject-lost grace period.

After stable subject loss or tracking-source unavailability:

- stale face/body payloads are discarded
- head returns smoothly to its neutral reference
- tracking-driven VRM expressions fade toward zero
- torso and arms return toward their last calibrated neutral/reference pose
- the next stable restore begins a fresh face/body calibration

This visual fallback is separate from presence event semantics.

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
