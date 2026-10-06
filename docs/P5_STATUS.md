# P5 Status

Updated: 2026-10-03

## Active branch

```text
feature/p5-motion-expression-mixer
```

P5 starts from:

```text
checkpoint/p4-source-implementation
34f1cf461f74c3ed0fc3c9162e0108c98af0c163
```

P0-P4 real-device/runtime evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## First P5 source slice

The first P5 slice introduces `ITrackingMixProvider` as the final one-performer provider contract presented to character targets and diagnostics.

`MotionExpressionMixer` currently:
- uses routed tracking as its base input
- passes face, body/hands, humanoid pose, and performer presence through
- keeps the original primary humanoid-pose slot and supports additional ordered pose layers
- each pose layer carries Base/Tracking/Additive/Procedural role metadata, Override/Additive mode, global weight, root weights, and per-bone weights
- pose-layer masks/settings/slots now use explicit ownership copies at public/configuration boundaries: callers receive defensive `Mask`/`Settings` copies and mixer configuration clones caller-owned objects, while blending uses internal `RuntimeMask`/`RuntimeSettings` accessors so the frame hot path remains allocation-free
- pose/expression numeric configuration is finite-normalized at the runtime boundary: non-finite pose weights fall back to full weight, expression weight falls back to 1, and non-finite deadzone/smoothing fall back to 0; serialized expression-layer values are normalized again during mixer `Awake()` before entering frame hot paths
- tracks immutable input snapshots by `TrackingFrame` reference identity rather than relying on source ID/sequence uniqueness; additional pose layers reuse one sampled frame array instead of parallel sequence/source tracking arrays
- passes the routed base expression frame through unchanged when no expression overlay contributes and smoothing is disabled; mixer-owned frames are reserved for actual blend/smoothing output
- accepts one optional expression-only overlay
- blends standard and named/custom expressions
- supports Override, Additive, and Maximum modes
- applies weight and a rescaled deadzone
- exposes a smoothing-rate contract and declares smoothing ownership so the VRM target does not smooth the same mixed channel twice
- stops expression smoothing updates after convergence instead of emitting new frames indefinitely
- falls back deterministically from overlay to routed base, then to no-frame so the character target can return to neutral
- emits mixer diagnostics
- never treats the expression overlay as performer-presence evidence

Character targets and runtime diagnostics prefer `ITrackingMixProvider` over `ITrackingRouteProvider`.

## Source-free validation

Interactive: `VCR > P5 > Validate Expression Mixer`

Batch:
```text
tools/validate-p5-source-free.ps1
tools/validate-p5-source-free.sh
```

The suite covers expression blend modes, deadzone behavior, custom expressions, smoothing alpha/convergence, face/presence passthrough, global/per-bone pose weighting, pose-space mismatch guards, ordered Override-to-Additive pose composition, overlay-to-base fallback, no-frame neutral signaling, same-source/same-sequence immutable frame replacement, and mixer diagnostics.

These validation paths are implemented but have not been executed here because a Unity Editor/runtime is not available in this environment.

## Deferred P5 evidence

- execute the P0-P5 Unity source-free batch suites
- measure allocation/update cost with active smoothing and multiple pose layers
- validate mixed pose/expression behavior on real VRM models
- tune defaults only from measured evidence

The source architecture is complete enough for a checkpoint, but these evidence items are not marked PASS.

One active performer remains the product scope.
