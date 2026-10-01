# Diagnostics

## Purpose

Diagnostics are part of feature acceptance, not a later debugging add-on.

The P0 runtime uses `P0RuntimeDiagnostics` plus subsystem implementations of `IRuntimeMetricsSource`.

## Frame timing

The diagnostics component records a rolling frame-time window and reports:

- average frame time
- P95 frame time
- P99 frame time

P0 targets remain:

- P95 <= 16.67 ms
- P99 <= 25 ms

A report exceeding either threshold is logged as a warning. This is evidence, not proof of a platform PASS by itself.

## Tracking metrics

For each normalized provider channel, diagnostics records:

- face update Hz
- webcam upper-body/hand update Hz
- optional full-body update Hz
- expression update Hz
- latest normalized snapshot age when `RuntimeTimestampUs` is available

A process-wide monotonic clock is used for publication-age measurements so unrelated device/source clock epochs are not subtracted from one another.

## MediaPipe processing latency

`MediaPipeFaceSource` and `MediaPipeHolisticSource` record monotonic time when `DetectAsync` is submitted and match it to the callback timestamp.

Reported metrics:

- `tracking.mediapipe.face.latency`
- `tracking.mediapipe.holistic.latency`

These are submit-to-callback Task/inference measurements. They are not identical to camera-photon-to-avatar latency.

## Protocol metrics

Current metric sources include:

- ARKit/iFacialMocap received packet count
- VMC received packet count
- VMC malformed packet count
- VMC sent packet count
- VMC send error count

The generic metric-source boundary lets future subsystems add low-frequency gauges without coupling the diagnostics component to their concrete types.

## Evidence output

Console reports occur every five seconds by default.

Optional CSV evidence is written to `Application.persistentDataPath` and includes:

- UTC timestamp
- frame average/P95/P99
- tracking-domain update rates
- latest snapshot ages
- presence state
- source availability
- subsystem metric summary

CSV writing is disabled by default and occurs only at report cadence.

## Performance rule

Do not add expensive diagnostics work to every render frame.

Current per-frame diagnostics work is limited to:

- one frame-time sample
- non-destructive provider sequence checks
- monotonic age arithmetic

Sorting, scene metric discovery, string formatting, logging, and CSV file I/O occur only at the report cadence.
