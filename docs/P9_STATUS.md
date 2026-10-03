# P9 Status

Updated: 2026-10-03

## Active branch

```text
feature/p9-event-runtime
```

P9 starts from the preserved P8 source checkpoint:

```text
checkpoint/p8-source-implementation
d610380ae9fce2a6ecf7eee5b951ba2fb0ce453e
```

P0-P8 runtime/device/network/service evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## First P9 source slice

P9 introduces a Unity-independent event rule engine in `VCR.Runtime.EventRuntime`.

The rule pipeline currently supports:

```text
NormalizedEvent
    ↓
EventRuleFilter
    ↓
EventStateCondition[]
    ↓
EventStateMutation[]
    ↓
EventActionCommand[]
```

Implemented rule features:

- exact event type, source id, and actor id filters
- case-insensitive text-contains filter
- amount-required/minimum/maximum filters
- typed numeric/text runtime state
- Exists/Missing/numeric/text state conditions
- Set/Add numeric state mutations
- Set/Remove text/state mutations
- event amount/text/actor/type/source value mapping into state/actions
- numeric scale/offset transforms for state/action values with non-finite configuration containment
- rule-level cooldown using monotonic event timestamps
- ordered rule evaluation
- optional stop-after-match
- bounded action-command output per input event
- dropped-command diagnostics

Missing numeric/text state keys do not silently compare as zero/empty values.

## Unity execution boundary

`EventRuntimeHost` subscribes to `NormalizedEventHub.Published`, so rule processing and application action execution occur after bounded ingress dispatch on the Unity main thread.

Rules emit only `EventActionCommand` values. They do not contain `GameObject`, `Material`, camera, window, SDK, or other backend handles.

Action execution is delegated through `IEventActionHandler`.

The first concrete actions are:

```text
environment.set_state
camera.set_fov
material.set_float
expression.set
```

`EnvironmentStateEventActionHandler` resolves an `IEnvironmentRuntime` target by environment id and calls its state-change contract.

`CameraFieldOfViewEventActionHandler` resolves the configured primary camera id, requires a finite numeric command value, and applies it through `PrimaryCameraController` rather than exposing a Camera object to the rule.

`ExpressionEventActionHandler` maps `TargetId` to a logical manual-expression layer and `Name` through `StandardExpressionNames`. Values must remain in 0..1. `ManualExpressionLayerSource` publishes only on actual value changes, has no Update loop, and never contributes performer-presence evidence. When wired as the mixer's expression overlay with Maximum blending, routed lip-sync/eye channels remain intact.

`MaterialFloatEventActionHandler` maps `TargetId` to a discovered material slot, `Name` to a shader property, and the numeric command value to `MaterialOverrideController.TrySetFloat`. It only mutates an already-active runtime override; it does not edit the source material or create an override implicitly.

Unknown actions, target mismatches, and handler exceptions are contained and reported through host diagnostics. If more than one configured handler claims the same command, the host fails closed, increments the ambiguous-action metric, and executes none of them.

## Source-free validation

Interactive:

```text
VCR > P9 > Validate Event Runtime
```

Batch:

```text
tools/validate-p9-source-free.ps1
tools/validate-p9-source-free.sh
```

The P9 batch entry runs P0-P8 source-free suites first and then checks:

- amount threshold filtering
- state mutation from donation amount
- state-conditioned follow-up rules
- case-insensitive text filtering
- missing-state numeric condition behavior
- rule cooldown suppression
- numeric scale/offset action transform
- bounded commands per event
- NormalizedEventHub -> EventRuntimeHost main-thread dispatch
- environment.set_state execution through an application-level handler
- camera.set_fov execution through PrimaryCameraController
- material.set_float mutation of an active runtime override without source-material mutation
- expression.set alias/range validation, no redundant frame publication, and Maximum blend preservation of routed lip-sync
- unknown-action containment and diagnostics
- multiple-handler ambiguity fails closed without target mutation

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Next P9 work

The next source slices are:

- add windowed rate-limit policy for bursty chat/donation inputs beyond the implemented per-rule cooldown
- add text transform operators beyond direct value mapping
- add motion-layer application handlers and expand material/shader actions beyond float parameters using existing subsystem contracts
- add environment transition parameters without leaking concrete environment components into rule definitions
- version persisted rule configuration before P11 exposes editing UI
- add rule-level diagnostics and optional tracing that stays disabled by default
- validate allocation/frame-time cost under event bursts

Recursive/chained event emission is intentionally not part of the first slice to avoid accidental feedback loops.

One active performer remains the product scope.
