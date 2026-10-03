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
- accepts one optional expression-only overlay
- blends standard and named/custom expressions
- supports Override, Additive, and Maximum modes
- applies weight and a rescaled deadzone
- exposes a smoothing-rate contract
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

The suite covers blend modes, deadzone behavior, custom expressions, smoothing alpha, face/presence passthrough, additive overlay blending, base-expression preservation without an overlay, and mixer diagnostics.

These validation paths are implemented but have not been executed here because a Unity Editor/runtime is not available in this environment.

## Next P5 work

- separate expression application availability from full-body pose availability in the VRM target path
- define base/tracking/additive/procedural pose-layer contracts
- add per-region/per-bone masks and weights
- avoid duplicate smoothing between mixer and target layers
- define deterministic fallback-to-base/neutral behavior
- add pose weighting/mask source-free validation
- measure allocation/update cost before enabling always-on smoothing by default

One active performer remains the product scope.
