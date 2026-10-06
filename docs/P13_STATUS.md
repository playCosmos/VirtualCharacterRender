# P13 Status — 2D Extension

P13 starts from `checkpoint/p12-source-implementation`.

The product remains 3D/VRM-first and one-active-performer. P13 adds an optional 2D presentation extension over the existing tracking/event/output contracts rather than turning the existing VRM runtime into a mixed 2D/3D renderer.

## Status

Source implementation is checkpoint-ready.

Unity Editor/runtime execution, real backend SDK integration, model rendering, Windows/macOS standalone packaging, OBS capture, and measured performance remain deferred evidence and are not marked PASS.

## Current decision

See ADR-0030.

No production 2D backend is selected yet.

Current evaluation:

- Live2D Cubism: strongest current Windows + macOS Unity prototype candidate, but proprietary Core and publication/distribution licensing require an explicit package/legal acceptance boundary.
- Inochi2D: preferred open alternative direction; current official Unity package is BSD-2-Clause and beta, but its current public native package layout does not yet satisfy the project's macOS first-class evidence requirement.

The core P13 runtime therefore has no dependency on either SDK.

## Implemented source slice

### Backend-neutral contracts

Assembly:

```text
VCR.Runtime.Presentation2D
```

The assembly references only:

- VCR.Runtime.Core
- VCR.Runtime.Tracking

It does not reference Live2D/Inochi2D SDK assemblies.

Implemented contracts:

- `Character2DInputDomain`
- `Character2DBackendState`
- `Character2DBackendStatus`
- `Character2DModelRequest`
- `Character2DInputSnapshot`
- `ICharacter2DBackend`

Supported input-domain flags are:

- Face
- BodyHands
- HumanoidPose
- Expressions

A backend may support only the domains it can actually consume.

### Character2DRuntime

`Character2DRuntime` is the optional host between one `ITrackingFrameProvider` and one `ICharacter2DBackend`.

Source behavior:

- validates model requests against the active backend id
- requires a non-empty model id/path
- supports configured or explicit model load
- unloads/reset frame cache on model unload
- polls only requested ∩ backend-supported tracking domains
- defaults to Face + Expressions
- does not call the backend when no relevant snapshot is available
- deduplicates unchanged immutable frame objects
- retries a frame after backend apply failure because failed input is not accepted into the cache
- treats a new frame object as new even if sequence value matches an earlier frame
- resets frame-object caches across model reload
- treats destroyed Unity backend/provider objects as unavailable instead of retaining stale interface references
- internally idle-disables when backend/provider/model/input readiness disappears without unloading an otherwise loaded model
- re-enables from explicit dependency reconfiguration without forcing a model reload
- unloads a previously loaded backend before switching the host to a different backend
- contains backend exceptions/failures and exposes LastError
- reports model/apply success/failure metrics through `IRuntimeMetricsSource`

Metrics:

```text
presentation2d.model.loaded
presentation2d.mapping.enabled
presentation2d.apply.count
presentation2d.apply.failures
presentation2d.load.count
presentation2d.load.failures
```

The host itself does not own backend-specific parameter ids. Adapter packages may use the shared mapping profile/evaluator below without moving SDK-specific types into the core host.

### Common 2D parameter mapping

P13 now includes a backend-bound, SDK-neutral `Character2DParameterMappingProfile`.

Supported mapping sources are:

- normalized face coefficients
- normalized standard expressions
- head position X/Y/Z
- signed head pitch/yaw/roll degrees

Each binding defines a target parameter id, input/output ranges, optional input clamping, and optional fallback input when the source domain is unavailable. Validation rejects blank/duplicate target ids, non-finite values, zero-width input ranges, unsupported source kinds, invalid face coefficients, and out-of-range standard expressions.

`Character2DParameterMapper.TryEvaluate` requires the profile backend id to match the active adapter id and produces backend-agnostic target-id/value pairs. Mapping validation uses allocation-free duplicate/enum range checks; the public snapshot API pre-counts emitted values and returns one exact-size owned `Character2DParameterValue[]`. `Character2DRuntime` instead caches the validated backend/profile/revision tuple, grows one reusable parameter scratch array only when profile capacity increases, evaluates changed frames through `TryEvaluateValidatedInto`, and passes the emitted range to `ICharacter2DParameterSink` as a synchronous `ReadOnlySpan`. Profile `Configure`/Inspector validation increments a non-serialized revision so mutating the same profile object forces revalidation before the next mapped frame. `Configure` deep-clones caller-owned binding objects and the public `Bindings` accessor returns a deep-cloned snapshot; runtime validation/evaluation uses `BindingCount` plus internal indexed access so these ownership boundaries do not add per-frame allocations. Backends may implement optional `ICharacter2DParameterSink`; when a profile is configured, `Character2DRuntime` validates the backend/profile match before model activation, evaluates changed tracking snapshots, and routes the mapped values to that sink instead of the raw-snapshot apply path. Empty mapped results are treated as a no-op and cached without incrementing apply count. The final SDK parameter write remains inside the backend adapter.

Editor menu:

```text
VCR/P13/Open 2D Parameter Mapping
```

The Editor window creates/selects mapping-profile assets, exposes backend id/bindings through serialized authoring, and provides Validate / Validate & Save without installing or selecting a 2D SDK backend.

## Source validation

Menu:

```text
VCR/P13/Run Source Validation
```

The source-free batch entrypoint is `VCR.Editor.P13.P13BatchValidation.RunSourceFreeAndExit`, with Windows/macOS launchers under `tools/validate-p13-source-free.*`. It executes the inherited P0-P12 chain before the P13 checks.

The first validator uses fake backend/provider MonoBehaviours and covers:

- requested input domain ∩ backend supported domain behavior
- backend-id mismatch rejection
- model load through the backend contract
- Face/Expressions polling while unsupported BodyHands/HumanoidPose remain unpolled
- first snapshot apply
- identical immutable frame-reference deduplication
- new-frame-object apply even with the same sequence value
- backend apply failure surfacing
- failed-frame retry behavior
- unload/reload cache reset
- runtime diagnostics metrics for load/apply success/failure
- temporary provider removal idle-disable while preserving the loaded model, followed by explicit provider recovery
- destroyed backend/provider reference rejection
- previous-backend unload on backend reconfiguration
- valid face/expression/head-position parameter evaluation
- authored fallback values for unavailable input domains
- skip behavior when unavailable inputs have no fallback
- compact mixed-result behavior when only a subset of bindings emit values
- out-of-range source-kind rejection through allocation-free range validation
- cached validation invalidation/recovery when the same mapping profile object is mutated after model load
- stable runtime parameter-scratch reuse across changed mapped frames when required capacity is unchanged
- mapping-profile backend mismatch rejection
- duplicate target-parameter rejection
- out-of-range standard-expression rejection
- host-side backend/profile mismatch rejection before model activation
- mapped-value routing through `ICharacter2DParameterSink` without also invoking the raw snapshot path

These validation paths are implemented but have not been executed in this environment because Unity Editor/runtime execution is unavailable here.

## Deferred backend work

### Live2D adapter

Do not add Cubism packages to core assemblies.

Before implementation/acceptance:

- decide package acquisition/version pinning
- document proprietary Core distribution
- confirm Publication/SDK Release License treatment for the intended application distribution/business model
- isolate adapter assembly behind an explicit dependency
- verify Windows + macOS Apple Silicon standalone builds

### Inochi2D adapter

Do not treat the current official Unity package as cross-platform production-ready evidence yet.

Before production acceptance:

- verify/update the official Unity package's native platform artifacts
- establish a macOS Apple Silicon native binding path
- pin package/native versions
- validate model load/parameter mapping on Windows and macOS

An experimental Windows-only adapter may be useful for contract testing but must not be labeled the project default.

## Remaining backend-dependent work

The common source boundary is defined in `PRESENTATION2D_ADAPTERS.md`.

The remaining P13 work depends on an actual backend/package decision or real platform evidence:

- Live2D adapter spike after license/package acceptance
- Inochi2D adapter spike when a macOS native path is available or intentionally built/maintained
- P13 runtime UI for selecting an installed 2D backend/model only after at least one adapter is actually available
- real parameter sink behavior against backend model parameters
- reuse and validate the existing transparent output / OBS path rather than adding a 2D-specific window layer

## Evidence boundary

This is not a 2D runtime PASS.

No Live2D or Inochi2D model has been loaded or rendered in this environment.

Required later evidence includes:

- real SDK/package integration
- real model rendering
- parameter/rig mapping quality
- tracking loss/recovery
- output alpha and OBS capture
- Windows/macOS standalone packaging
- performance and memory measurements
- licensing/package review
