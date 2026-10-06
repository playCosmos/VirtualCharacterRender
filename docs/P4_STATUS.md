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

P4 now exposes a common region-addressable source-health contract through `ITrackingSourceHealthProvider` / `TrackingSourceHealthSnapshot`. MediaPipe returns independent FaceLandmarker and Holistic health snapshots; ARKit and VMC expose the same contract without router-side type checks. Runtime frame timestamps are kept separate from source/device timestamps so age calculations do not assume a shared epoch.

Face and expression source priority are now explicit policy data through `TrackingRoutePolicy`. The default face order remains ARKit face first and MediaPipe FaceLandmarker second. The default expression order is VMC first and optional audio mouth fallback second. Existing serialized provider fields remain scene wiring for compatibility; changing priority no longer requires rewiring those references. Providers without source-kind health metadata retain the historical preferred-then-fallback behavior.

Router presence grace/stability timings are normalized before `TrackingPresenceResolver` construction. Non-finite serialized timings restore the default 0.5s loss grace / 0.15s restore stability values, while seconds-to-microseconds conversion is explicitly non-finite-safe and saturates extreme finite values rather than overflowing the resolver timestamps.

P4 also exposes a route-status contract through `ITrackingRouteStatusProvider` / `TrackingRouteStatus`:

- selected face source ID
- selected body/hands source ID
- selected full-body source ID
- selected expression source ID, including VMC -> audio fallback changes
- preferred-face active state
- fallback face-inference enabled state
- per-region routed-frame age

`PriorityTrackingRouter` also contributes low-rate runtime diagnostics metrics:

- routed-frame ages
- preferred/fallback face activation state
- source-switch counts for face, body/hands, full body, and expressions

The source-switch counters only increment after an already-selected source changes; initial source acquisition is not counted as a switch.

Routed outputs now preserve the selected provider's immutable `TrackingFrame` object directly for face, body/hands, full-body, and expressions. The router no longer allocates a second envelope frame merely to restamp sequence metadata. Duplicate suppression uses immutable frame reference identity while source IDs remain only for switch metrics, so a replacement snapshot that reuses an old source ID/sequence is still observable without requiring a loss gap.

Face routing also samples each needed candidate at most once per router update. A healthy preferred source that outranks the fallback is sampled once and then reused for activation, selection, and output; the fallback face provider is not polled in that case. If policy or health makes fallback competitive, it is enabled first and then sampled once.

Expression fallback now follows the same activation principle. A healthy higher-priority external expression source such as VMC suspends an `IExpressionTrackingActivationControl` fallback before it is polled, allowing `AudioDrivenExpressionSource` to skip `AudioSource.GetOutputData` and RMS work entirely. If the external source is lost or routing policy explicitly prefers audio, the fallback is re-enabled before selection. Suspension clears the audio source's retained expression frame so a later reactivation waits for a fresh sample instead of routing stale mouth data.

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
- deterministic source-switch metrics across policy reversal, policy restore, and preferred-source loss
- runtime reversal of the face priority policy without provider rewiring
- VMC expression selection with automatic audio-expression fallback after VMC expression health loss
- expression source-switch diagnostics
- route diagnostics emission
- preferred source rejection when common health reports `SourceLost` even if cached presence/frame state remains
- direct routed-frame reference reuse for face, body/hands, full-body, and expressions without wrapper allocation
- loss/recovery and provider replacement when a source reuses a previous source ID/sequence
- single-sample preferred-face routing and skipped fallback face reads when the preferred source already wins
- suspended audio-expression fallback polling while VMC wins, automatic re-enable on VMC loss, and explicit audio-first policy override

## Still deferred to real devices / players

- physical ARKit -> MediaPipe fallback transition quality
- transition visual smoothness on an actual VRM
- webcam/ARKit timestamp behavior under device stalls
- route age behavior during real packet jitter
- source-loss/recovery timing thresholds
- M1 and Windows routing/diagnostics overhead
- full-body VMC source hot switching with a real sender

## Next P4 work

The P4 source implementation now covers the planned source-health, region routing, hot-switching, route diagnostics, source-priority policy, and expression-fallback routing contracts.

Weighted pose/expression mixing, masks, deadzones, smoothing policy, and fallback blending remain P5. P4 intentionally exposes one selected expression source at a time; it does not combine VMC and audio values. Audio fallback is not included in performer-presence resolution.
