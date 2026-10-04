# P13 Status — 2D Extension

P13 starts from `checkpoint/p12-source-implementation`.

The product remains 3D/VRM-first and one-active-performer. P13 adds an optional 2D presentation extension over the existing tracking/event/output contracts rather than turning the existing VRM runtime into a mixed 2D/3D renderer.

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
- contains backend exceptions/failures and exposes LastError
- reports model/apply success/failure metrics through `IRuntimeMetricsSource`

Metrics:

```text
presentation2d.model.loaded
presentation2d.apply.count
presentation2d.apply.failures
presentation2d.load.count
presentation2d.load.failures
```

No backend-specific parameter mapping exists in the core host.

## Source validation

Menu:

```text
VCR/P13/Run Source Validation
```

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

## Next P13 source work

- backend adapter package/assembly layout and dependency gates
- common parameter-mapping authoring model for mapping normalized face/expression semantics to backend parameter ids
- Live2D adapter spike after license/package acceptance
- Inochi2D adapter spike when a macOS native path is available or intentionally built/maintained
- P13 UI surface for selecting an installed 2D backend/model only after at least one adapter is actually available
- reuse existing transparent output / OBS path rather than adding a 2D-specific window layer

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
