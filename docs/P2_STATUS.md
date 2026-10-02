# P2 Status

Updated: 2026-10-02

## Active branch

```text
feature/p2-material-shader-runtime
```

P2 starts from the preserved P1 source implementation checkpoint:

```text
checkpoint/p1-source-implementation
cae61915cd239e921c14bc96e638a08fea1ed655
```

P1 still requires Unity 6000.3.25f1 compile/package/build execution evidence. P0 hardware-dependent evidence remains deferred.

## Existing prototype carried forward

The P0 implementation already provided:

- material-slot discovery
- stable slot IDs from renderer path + slot index
- non-destructive runtime material clones
- direct float/int/bool/color/vector/texture setters
- source-material restore
- deterministic source-material fallback on shader/apply failure
- precompiled shader registry
- platform-specific AssetBundle shader loader
- runtime metrics

P2 does not duplicate these paths.

## Implemented in P2

### Serializable material presets

- `MaterialOverridePreset`
- required preset ID
- optional shader ID
- empty shader ID means preserve the source shader
- parameter array for:
  - float
  - int
  - bool
  - color
  - vector
  - enum
- texture remains available through the direct runtime API
- serialized texture preset binding is explicitly rejected until a resolver contract exists

### Source shader / MToon preservation

When a preset omits `ShaderId`:

- the original source shader is reused
- the runtime material is cloned from the source material
- source material data is not mutated
- preset parameters are applied only to the runtime clone

This path is intended to preserve MToon and other source shader behavior instead of replacing the shader unnecessarily.

### Compatibility preflight

`MaterialOverrideController.EvaluatePreset` checks before mutation:

- slot existence
- preset ID
- shader resolution
- current-device shader support
- shader property existence
- shader property type compatibility
- unsupported serialized texture bindings

Reports include:

- slot ID
- preset ID
- resolved shader ID
- issue code/property/message
- current RuntimePlatform
- current graphics API

A preflight-incompatible preset leaves the currently active material unchanged.

### Atomic preset application

`TryApplyPreset` performs:

1. compatibility preflight
2. runtime material clone / shader application
3. parameter application
4. active status update with preset ID

If mutation begins and then throws, the controller restores the source material through the existing fallback path.

### Status and restore

- `MaterialOverrideStatus` now carries `PresetId`
- `GetStatuses()` exposes a snapshot for later UI
- `ClearOverride` restores the exact source material and clears preset state
- source material remains the deterministic fallback material

## Validation

Interactive:

```text
VCR > P2 > Validate Material Shader Runtime
```

Batch:

```text
tools/validate-p2-source-free.ps1
tools/validate-p2-source-free.sh
```

The P2 batch path runs P0, P1, then P2 source-free suites.

Current P2 checks cover:

- source-shader preset compatibility
- runtime clone isolation
- source material preservation
- preset parameter application
- active preset status
- invalid property rejection before mutation
- current material unchanged on preflight failure
- explicit rejection of serialized texture preset bindings
- explicit shader-ID preset path
- status snapshot
- exact source-material restore

Actual Unity compilation/package resolution and real VRM/MToon execution remain unverified in this environment.

## Next P2 work

1. texture resolver contract for serialized presets
2. shader-bundle compatibility metadata and loaded-bundle status
3. preset persistence/package representation
4. per-preset compatibility summary suitable for UI
5. external bundle wrong-platform / missing-shader diagnostics
6. source-free tests for resolver and metadata behavior
7. real VRM/MToon validation when Unity and model assets are available
