# P6 Status

Updated: 2026-10-03

## Active branch

```text
feature/p6-environment-runtime
```

P6 starts from the preserved P5 source implementation line. P0-P5 hardware/runtime evidence remains deferred where previously documented; source checkpoints are not validation PASS results.

## Current source implementation

The environment runtime now has an explicit engine-neutral update contract:

- `EnvironmentUpdatePolicy`: Static / EventDriven / Hz10 / Hz30 / EveryFrame
- `EnvironmentUpdateReason`: StateChanged / Scheduled / Manual
- `EnvironmentUpdateContext`
- `IEnvironmentUpdateTarget`
- drop-only `EnvironmentUpdateScheduler`

Unity `BasicEnvironmentRuntime` now:

- owns one environment ID/state and explicit World/Camera/Screen/Character space mode
- validates state bindings before applying them
- rejects duplicate/invalid/unsafe state roots without partially changing the active visual state
- switches bound state roots without scene reload
- dispatches state-driven and manual environment updates explicitly
- lazily creates/enables `EnvironmentUpdateDriver` only for recurring Hz10/Hz30/EveryFrame policies
- disables the recurring driver again for Static/EventDriven policies
- de-duplicates update targets and supports runtime register/unregister
- isolates update-target exceptions and reports failure metrics
- emits state/update/driver diagnostics

Static/EventDriven environments still have no `Update()` method on the runtime itself; recurring work exists only in the optional driver.

## Source-free validation

Interactive:

```text
VCR > P6 > Validate Environment Runtime
```

Batch:

```text
tools/validate-p6-source-free.ps1
tools/validate-p6-source-free.sh
```

The P6 suite checks:

- Static/EventDriven no-recurring scheduler behavior
- Hz10 interval and drop-only stall behavior
- EveryFrame due behavior
- immediate state-root application
- atomic state switching and unknown-state rejection
- duplicate/unsafe binding rejection without replacing the valid set
- idempotent same-state assignment
- manual and scheduled dispatch
- lazy recurring-driver creation/disable
- update-target de-duplication, register/unregister, and exception isolation
- environment diagnostics

These validation paths are implemented but have not been executed here because a Unity Editor/runtime is not available in this environment.

## Next P6 work

- turn EnvironmentSpaceMode into an actual Unity anchor/transform policy for World/Camera/Screen/Character environments
- add transition contracts (Cut first; Fade/Crossfade/Dissolve as optional capabilities)
- add lightweight 2D image/video/parallax environment targets without forcing them into 3D scene semantics
- add explicit environment-lighting influence hooks without mutating character source materials
- attribute environment update/transition cost in runtime diagnostics

One active performer/character remains the product scope.
