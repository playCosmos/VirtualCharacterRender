# P4 Status

Updated: 2026-10-03

## Active branch

```text
feature/p4-tracking-routing
```

P4 starts from the preserved P3 source checkpoint:

```text
checkpoint/p3-source-implementation
020f7794f4e521150e5650213f356ac665e0423b
```

P0 hardware validation and later Unity/device evidence remain deferred. No deferred hardware item is reclassified as PASS.

## Current P4 implementation

The existing `PriorityTrackingRouter` remains the one-performer region router:

- face/head: ARKit-compatible preferred source
- face/head fallback: MediaPipe FaceLandmarker
- hands/upper body: MediaPipe Holistic
- optional full body / expression source: VMC
- fallback face inference is suspended while the preferred face source is healthy
- source/device loss remains distinct from performer absence

P4 now exposes a route-status contract through `ITrackingRouteStatusProvider` / `TrackingRouteStatus`:

- selected face source ID
- selected body/hands source ID
- selected full-body source ID
- selected expression source ID
- preferred-face active state
- fallback face-inference enabled state
- per-region routed-frame age

`PriorityTrackingRouter` also contributes low-rate runtime diagnostics metrics:

- routed-frame ages
- preferred/fallback face activation state
- source-switch counts for face, body/hands, full body, and expressions

The source-switch counters only increment after an already-selected source changes; initial source acquisition is not counted as a switch.

## Source-free validation

Interactive:

```text
VCR > P4 > Validate Tracking Routing
```

Batch:

```text
tools/validate-p4-source-free.ps1
tools/validate-p4-source-free.sh
```

The P4 batch suite runs P0 through P3 source-free checks first, then verifies:

- healthy preferred face selection
- fallback FaceLandmarker suspension while the preferred face source is active
- hot switch to the fallback face source when the preferred source becomes unavailable
- fallback face inference re-enable
- non-negative routed face age
- exactly one face source-switch metric for the preferred-to-fallback transition
- route diagnostics emission

## Still deferred to real devices / players

- physical ARKit -> MediaPipe fallback transition quality
- transition visual smoothness on an actual VRM
- webcam/ARKit timestamp behavior under device stalls
- route age behavior during real packet jitter
- source-loss/recovery timing thresholds
- M1 and Windows routing/diagnostics overhead
- full-body VMC source hot switching with a real sender

## Next P4 work

The next source implementation slice is:

- introduce a common source-health provider contract over the existing per-source health states
- expose health/staleness snapshots to the router without source-specific type checks
- move source-priority decisions to explicit policy data rather than hard-coded provider roles where this can be done without adding multi-person complexity
- route optional expression fallback as a source choice, not as expression mixing; weighted mixing remains P5
- keep region-level loss semantics and one-performer scope intact
