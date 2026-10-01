# ADR-0029: Runtime custom shaders use precompiled shader assets

- Status: Accepted
- Date: 2026-10-02

## Context

Custom shaders are a required first-class feature, but the runtime must remain lightweight, recoverable, and portable across Windows and macOS.

Treating arbitrary HLSL source as something the standalone application compiles on demand would add a new compiler/toolchain surface, platform-specific failure modes, shader-cache complexity, and security/diagnostic burden.

Unity's runtime/content pipeline is already built around compiled Shader assets and platform-specific AssetBundles. AssetBundles themselves are platform-specific, and shader variants can also be stripped if the build/content pipeline does not preserve the required combinations.

## Decision

The production runtime consumes precompiled Shader assets.

Supported resolution paths:

1. Shader assets already included in the player build
2. Shader assets registered from a compatible platform-specific AssetBundle
3. future package tooling that builds/registers those same assets

The runtime does **not** compile arbitrary raw HLSL source in-process.

P0 implementation:

```text
shaderId
  ↓
RuntimeShaderRegistry
  ├─ registered Shader from bundle
  └─ Shader.Find for player-included shader
  ↓
MaterialOverrideController
  ↓
runtime Material clone
  ↓
renderer material slot
```

Source materials are never modified. Each active override owns a runtime clone.

If shader resolution or application fails:

1. increment diagnostics
2. destroy the failing runtime clone if one exists
3. restore the original source material reference
4. keep the character renderable
5. retain error/status information

## P0 external bundle rule

A custom shader bundle is built separately for each target platform.

Windows and macOS bundles are not interchangeable.

The bundle must also be validated against the pinned Unity/URP line. Cross-version compatibility is not assumed.

P0 uses explicit one-shot loading. No shader package loader performs recurring per-frame work after registration.

## Shader variants

Do not solve stripping by globally placing large variant-heavy shaders in Always Included Shaders.

Package/build tooling should preserve only the required shader/keyword combinations, using explicit materials and/or ShaderVariantCollection policy as evidence requires.

## Consequences

- predictable runtime behavior
- no embedded arbitrary shader compiler
- smaller security and maintenance surface
- package producers need per-platform build output
- package compatibility metadata must include Unity/URP/platform information
- hot parameter changes remain runtime operations, but shader source compilation remains an authoring/build operation

## Revisit conditions

Revisit only if Unity exposes a supported cross-platform runtime shader-source compilation path that materially improves the product without increasing baseline cost or failure surface.
