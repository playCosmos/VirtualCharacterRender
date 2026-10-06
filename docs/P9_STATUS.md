# P9 Status

Updated: 2026-10-05

## Active branch

Current integrated source of truth:

```text
develop
```

The P9 feature/checkpoint branches are historical references. P9 originally starts from the preserved P8 source checkpoint:

```text
checkpoint/p8-source-implementation
d610380ae9fce2a6ecf7eee5b951ba2fb0ce453e
```

P0-P8 runtime/device/network/service evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## Source implementation checkpoint

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
- text trim, invariant lower/upper-case, prefix, and suffix transforms for state/action mappings
- rule-level cooldown using monotonic event timestamps
- optional rule-level fixed-window rate limiting for bursty inputs; quota counts only successful rule executions and resets deterministically at the next window
- ordered rule evaluation
- optional stop-after-match
- bounded action-command output per input event
- dropped-command diagnostics
- versioned persisted rule documents with atomic save, explicit max-command settings, current-version reload, fail-closed rejection of newer unsupported versions, and a 16 MiB strict UTF-8 persistence bound checked before JSON allocation
- per-rule diagnostics snapshots for evaluations, rejects, cooldown/rate-limit suppression, matches, emitted/dropped commands, and last-match timestamp
- structured opt-in rule tracing that is disabled by default and emits no trace events unless explicitly enabled

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
material.set_int
material.set_bool
material.set_color
material.set_vector
material.set_texture
material.set_shader
material.apply_preset
expression.set
motion.pose_weight
```

`EnvironmentStateEventActionHandler` resolves an `IEnvironmentRuntime` target by environment id and calls its state-change contract.

`CameraFieldOfViewEventActionHandler` resolves the configured primary camera id, requires a finite numeric command value, and applies it through `PrimaryCameraController` rather than exposing a Camera object to the rule.

`ExpressionEventActionHandler` maps `TargetId` to a logical manual-expression layer and `Name` through `StandardExpressionNames`. Values must remain in 0..1. `ManualExpressionLayerSource` publishes only on actual value changes, has no Update loop, and never contributes performer-presence evidence. When wired as the mixer's expression overlay with Maximum blending, routed lip-sync/eye channels remain intact.

`MaterialFloatEventActionHandler` preserves the existing float path. `MaterialPropertyEventActionHandler` adds strict int/bool/color/vector command validation plus texture/shader resource-id actions. `MaterialPresetEventActionHandler` resolves logical preset ids through `IMaterialPresetResolver`; the P7 shader-package loader implements that resolver over its active versioned preset document. Rules never receive Material, Texture, Shader, preset objects, or preset file paths, and handlers do not mutate source materials.

`MotionPoseWeightEventActionHandler` applies a validated 0..1 value to the mixer's primary pose-layer weight through the P5 mixer contract.

Unknown actions, target mismatches, and handler exceptions are contained and reported through host diagnostics. Destroyed Unity handlers cached behind interfaces are ignored rather than invoked. Duplicate references to the same handler instance are deduplicated; genuinely distinct handlers claiming the same command still fail closed and increment the ambiguous-action metric. When auto-find is enabled in a Player build, the host rechecks its event-hub subscription at a bounded 1 Hz cadence so a destroyed/replaced `NormalizedEventHub` can be rebound without a per-frame global search.

`EventRuntimeHost` rule application is non-destructive on validation failure. `TrySetRuleEnabled` rolls back the requested `Enabled` mutation, `TrySetMaxCommandsPerEvent` restores the previous limit, and a failed engine apply leaves the existing engine rules and host rule array intact instead of clearing the host to an empty rule set while returning success.

P11 extends the established P9 action boundary with `appearance.set_preset`, `appearance.set_outfit`, `appearance.set_accessory`, `appearance.clear_accessory`, and `appearance.restore_default`. The P11 Appearance Transition Runtime reuses registered application-action handlers directly for presentation cues while owning timing and the single atomic `appearance.commit` boundary. It rejects recursive `appearance.*` transition actions. P11 also adds shared `effect.play` / `effect.stop` and `motion.play` / `motion.release` action handlers; this is a later-phase extension and does not change the preserved P9 checkpoint claim.

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
- windowed rule rate-limit burst allowance, excess suppression, and next-window reset
- numeric scale/offset action transform
- deterministic text transform mapping for state mutation and action commands
- bounded commands per event
- versioned rule save/reload plus newer-version and oversized-file rejection
- per-rule diagnostics snapshot counts and tracing-disabled-by-default behavior
- structured matched-rule trace emission after explicit opt-in
- NormalizedEventHub -> EventRuntimeHost main-thread dispatch
- environment.set_state execution through an application-level handler
- environment transition mode/duration mapping through IEnvironmentRuntime without concrete component leakage
- camera.set_fov execution through PrimaryCameraController
- material.set_float mutation of an active runtime override without source-material mutation
- material int/bool strict-value validation plus color/vector command component contracts
- registered texture/shader id resolution without leaking Unity resource objects into rule definitions
- logical material preset-id resolution through the active package preset resolver without rule-owned file paths
- expression.set alias/range validation, no redundant frame publication, and Maximum blend preservation of routed lip-sync
- unknown-action containment and diagnostics
- duplicate references to the same handler instance are deduplicated while multiple distinct matching handlers still fail closed
- destroyed action handlers fail closed as unhandled instead of invoking stale Unity interface references
- destroyed/replaced NormalizedEventHub rebinding through the bounded lifecycle refresh path

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Deferred P9 evidence

- execute the P0-P9 Unity source-free batch suite
- measure allocation/frame-time cost under bursty chat/donation inputs
- validate action dispatch against real environment/material/motion scenes
- validate persisted rule documents through the future P11 editing UI

Recursive/chained event emission remains intentionally out of the P9 source scope to avoid accidental feedback loops.

The P9 source architecture is complete enough for a checkpoint. Deferred source-free/runtime evidence is not marked PASS.

One active performer remains the product scope.
