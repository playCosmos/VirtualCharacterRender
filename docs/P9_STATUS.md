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
- ordered rule evaluation
- optional stop-after-match
- bounded action-command output per input event
- dropped-command diagnostics

Missing numeric/text state keys do not silently compare as zero/empty values.

## Unity execution boundary

`EventRuntimeHost` subscribes to `NormalizedEventHub.Published`, so rule processing and application action execution occur after bounded ingress dispatch on the Unity main thread.

Rules emit only `EventActionCommand` values. They do not contain `GameObject`, `Material`, camera, window, SDK, or other backend handles.

Action execution is delegated through `IEventActionHandler`.

The first concrete action is:

```text
environment.set_state
```

`EnvironmentStateEventActionHandler` resolves an `IEnvironmentRuntime` target by environment id and calls its state-change contract. Unknown actions, target mismatches, and handler exceptions are contained and reported through host diagnostics.

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
- bounded commands per event
- NormalizedEventHub -> EventRuntimeHost main-thread dispatch
- environment.set_state execution through an application-level handler
- unknown-action containment and diagnostics

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Next P9 work

The next source slices are:

- add rule cooldown/debounce/rate-limit policy for bursty chat/donation inputs
- add explicit numeric/text transform operators rather than only direct value mapping
- add application handlers for expression/motion, material/shader parameters, and camera using existing subsystem contracts
- add environment transition parameters without leaking concrete environment components into rule definitions
- define deterministic handler selection when multiple handlers claim the same command
- version persisted rule configuration before P11 exposes editing UI
- add rule-level diagnostics and optional tracing that stays disabled by default
- validate allocation/frame-time cost under event bursts

Recursive/chained event emission is intentionally not part of the first slice to avoid accidental feedback loops.

One active performer remains the product scope.
