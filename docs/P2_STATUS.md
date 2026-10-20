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
- serialized texture preset binding is supported through texture IDs
- custom texture resolvers can implement `IMaterialTextureResolver`
- `RuntimeTextureRegistry` provides the default lightweight resolver fallback

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
- missing/unknown texture IDs

Reports include:

- slot ID
- preset ID
- resolved shader ID
- issue code/property/message
- current RuntimePlatform
- current graphics API

A preflight-incompatible preset leaves the currently active material unchanged.

### Texture resolver

- serialized presets store texture IDs rather than Unity object references
- optional custom resolver injection through `IMaterialTextureResolver`
- fallback resolution through `RuntimeTextureRegistry`
- unresolved texture IDs fail during compatibility preflight
- resolved textures are applied only to the runtime clone

### Atomic preset application

`TryApplyPreset` performs:

1. compatibility preflight
2. runtime material clone / shader application
3. parameter application
4. active status update with preset ID

If mutation begins and then throws, the controller restores the source material through the existing fallback path.

### Preset persistence

- versioned `MaterialPresetDocument`
- current preset schema version: 1
- JSON store with atomic replace
- duplicate preset IDs rejected
- null/invalid presets rejected
- unsupported schema versions rejected
- all-slot compatibility summary through `EvaluatePresetForAllSlots`

### Shader bundle compatibility metadata and diagnostics

- optional `<bundle>.vcr.json` sidecar metadata
- metadata format version 1
- bundle ID
- target platform
- Unity version
- declared shader IDs
- wrong-target metadata rejected before AssetBundle parsing
- Unity-version mismatch rejected before AssetBundle parsing
- declared shader IDs verified after load
- metadata-free legacy bundles remain allowed
- P0 validation bundle builder now emits sidecar metadata
- `ShaderBundleLoadStatus`
- per-attempt sequence
- normalized bundle path
- success/failure
- registered shader count and IDs
- current RuntimePlatform
- current graphics API
- metadata presence/path/bundle/target/Unity context
- guarded invalid/missing path handling
- runtime shader registry exposes registered shader IDs for later UI

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
- registered texture-ID preset application
- unresolved texture-ID preflight rejection
- explicit shader-ID preset path
- shader registry ID snapshot
- missing bundle failure status with platform/API context
- wrong-target bundle metadata preflight rejection
- all-slot compatibility summary
- preset JSON save/load round-trip
- atomic preset document replacement
- duplicate preset ID rejection
- status snapshot
- exact source-material restore

Actual Unity compilation/package resolution and real VRM/MToon execution remain unverified in this environment.

## P2 implementation status

The planned P2 material/shader runtime source implementation is complete enough to proceed to P3.

Implemented source scope now covers:

- slot abstraction
- source/MToon-preserving runtime clones
- runtime shader replacement
- generic scalar/color/vector/texture parameters
- serializable presets
- texture-ID resolver/registry
- deterministic source-material fallback
- current-platform compatibility reports
- all-slot compatibility summaries
- preset persistence
- shader registry diagnostics
- sidecar bundle compatibility metadata
- wrong-target / wrong-Unity preflight rejection
- explicit error/status surfaces

Still unresolved:

- Unity 6000.3.25f1 compile/package-resolution execution
- successful real external shader bundle loading on Windows/macOS
- real VRM 0.x / VRM 1.0 MToon preservation checks
- shader variant/stripping behavior in built players
- GPU-specific visual/performance evidence

These remain execution evidence, not reasons to expand P2 architecture further without measured failures.

## Next phase

Proceed to P3 — Built-in Basic Motion Capture.

Hardware-dependent validation remains deferred until test devices are available.
