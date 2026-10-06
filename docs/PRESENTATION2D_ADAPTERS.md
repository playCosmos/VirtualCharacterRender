# Presentation2D backend adapter contract

P13 keeps 2D SDK integration outside the core `VCR.Runtime.Presentation2D` assembly.

This document defines the source/package boundary for optional Live2D, Inochi2D, or future 2D adapters. It does not select or install a production backend.

## Required layout

A backend integration should live under a backend-owned adapter root, for example:

```text
unity/Assets/VCR/Adapters/Presentation2D/<Backend>/
  Runtime/
    VCR.Adapter.Presentation2D.<Backend>.asmdef
    <Backend>Character2DBackend.cs
  Editor/
    VCR.Editor.Adapter.Presentation2D.<Backend>.asmdef
    ...
```

If the SDK is delivered as a UPM package, the adapter may instead live in a dedicated VCR UPM package. The same assembly/dependency rules still apply.

## Assembly dependency rule

The core assembly remains:

```text
VCR.Runtime.Presentation2D
  -> VCR.Runtime.Core
  -> VCR.Runtime.Tracking
```

A backend assembly may reference:

- `VCR.Runtime.Presentation2D`
- the accepted backend SDK assembly/package
- backend-local helper assemblies

The core presentation assembly must not reference the backend adapter or SDK.

Backend-specific editor tooling must remain in an Editor-only assembly and must not leak SDK editor types into runtime assemblies.

## Compile-time dependency gates

An optional backend must fail closed when its SDK is absent.

Allowed mechanisms include:

- adapter package not installed at all
- adapter assembly `defineConstraints`
- UPM `versionDefines` when the SDK exposes a stable package identifier
- backend-local conditional compilation around SDK-only source

Do not place a fake/stub SDK type in the core project to make an absent dependency compile.

Do not add Live2D/Inochi2D SDK assemblies to `VCR.Runtime.Presentation2D.asmdef`.

## Runtime contract

The runtime backend component must implement:

```text
ICharacter2DBackend
```

It owns:

- stable `BackendId`
- supported input-domain declaration
- backend model load/unload
- backend status/error translation
- raw normalized snapshot application when it owns mapping internally

If the backend uses the shared P13 mapping evaluator, it also implements:

```text
ICharacter2DParameterSink
```

In that mode:

1. `Character2DRuntime` validates the configured mapping profile.
2. Profile `BackendId` must exactly match the active backend.
3. Changed normalized tracking snapshots are evaluated by `Character2DParameterMapper` into runtime-owned reusable scratch.
4. Empty mapped results are treated as a no-op.
5. The emitted parameter range is borrowed by the adapter as `ReadOnlySpan<Character2DParameterValue>` for the duration of the synchronous sink call.
6. The adapter must consume that span immediately and must not retain the span or its backing storage.
7. Final parameter-id/value writes are performed only by the adapter sink.

The adapter must not introduce a second webcam/ARKit/VMC tracking pipeline.

## Model and mapping ownership

Backend model files remain backend-specific.

The common host understands only:

- backend id
- logical model id
- model path
- normalized tracking snapshot
- optional mapped parameter id/value pairs

The adapter is responsible for resolving backend-specific model formats, parameter ids, drawables, physics and renderer objects.

A `Character2DParameterMappingProfile` is backend-bound authoring data, not a portable guarantee that two model formats share identical parameter ids.

## Failure containment

Backend errors must be returned through the existing boolean/error contracts.

Expected failure cases include:

- SDK/package unavailable
- unsupported platform/architecture
- invalid model file
- model load failure
- mapping/backend id mismatch
- missing target parameter
- parameter write failure
- backend runtime exception

An adapter failure must not prevent the 3D/VRM runtime from compiling or starting when the adapter is not installed/active.

## Platform acceptance

A backend is not accepted as production-ready until it demonstrates:

- Windows standalone model load/render
- macOS Apple Silicon standalone model load/render
- existing normalized face/head/eye/mouth response
- existing standard-expression response
- tracking loss/recovery
- model unload/reload without stale input
- transparent output and OBS capture
- disabled/idle recurring-cost evidence
- 720p60 minimum and 1080p60 recommended target measurements where applicable
- packaging/signing/notarization impact
- license/distribution review

Windows-only or editor-only success may be recorded as a spike, not as project-wide backend acceptance.

## Current P13 state

The repository currently contains only the backend-neutral host, shared parameter mapping/evaluator, optional mapped-parameter sink contract, editor mapping authoring, and source validation.

No Live2D or Inochi2D SDK/package is a core dependency, and no production 2D backend is selected.
