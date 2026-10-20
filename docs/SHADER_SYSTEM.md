# Shader System

## Status

Custom shader support is a core product requirement.

ADR-0029 fixes the runtime execution model: custom shader source is compiled during authoring/build, not inside the standalone application.

## Runtime architecture

```text
Source Material
      ↓
Material Slot
      ↓
Optional Override Request
      ↓
RuntimeShaderRegistry
   ├─ player-included Shader
   └─ platform AssetBundle Shader
      ↓
Runtime Material Clone
      ↓
Renderer Material Slot
```

Current Unity implementation:

- `VCR.Runtime.Materials.Unity.MaterialOverrideController`
- `VCR.Runtime.Materials.Unity.RuntimeShaderRegistry`
- `VCR.Runtime.Materials.Unity.RuntimeShaderBundleLoader`

P0 validation assets/tools:

- `Assets/VCR/P0/Shaders/P0TintUnlit.shader`
- `VCR > P0 > Validate Material Override Runtime`
- `VCR > P0 > Build Shader Bundle > Windows x64`
- `VCR > P0 > Build Shader Bundle > macOS`
- `VCR > P0 > Validate Current Platform Shader Bundle`

The source material is never modified.

## P0 material-slot model

Each discovered slot has:

- stable runtime slot ID
- renderer hierarchy path
- material index
- source material name
- source shader name
- override health/status

P0 supports:

- per-slot shader override
- float/int/bool/color/vector/texture runtime parameters
- source restoration
- invalid/unsupported shader fallback
- source-material mutation isolation
- precompiled Shader registration from a platform AssetBundle
- diagnostics counters

Whole-character presets and serialized parameter metadata remain later material/shader-runtime work.

## Parameter types

Target generic parameter vocabulary:

- Float
- Int
- Bool
- Color
- Vector2/3/4
- Texture
- Enum

The Unity P0 adapter already exposes the corresponding runtime setter primitives where applicable.

Each future serialized parameter definition contains a stable ID, display name, type, default, optional range/step, runtime mutability, serialization policy, and optional UI hints.

## Bindings

Parameters may be driven by tracking, audio, expressions, animation, OSC, VMC-derived values, MIDI, WebSocket/API commands, event output, or procedural/time sources.

Bindings support remap, clamp, curve, smoothing, and blending.

Bindings are not implemented inside `MaterialOverrideController`; they belong above the material backend.

## Precompiled shader packages

Runtime raw-HLSL compilation is intentionally excluded.

External shader content is delivered as precompiled Shader assets, initially through platform-specific AssetBundles.

```text
package/
├─ manifest.json
├─ windows/
│  └─ shaders.bundle
├─ macos/
│  └─ shaders.bundle
├─ textures/
├─ preview/
└─ defaults/
```

Package metadata must eventually declare:

- package/version ID
- target platform
- Unity version line
- URP version line
- shader IDs
- required keywords/variants
- parameter metadata
- fallback expectations

AssetBundles are platform-specific. Windows and macOS shader bundles are built independently.

## Failure behavior

1. mark override unhealthy
2. report resolution/load/application error
3. dispose failing runtime material clone
4. restore the original source material reference
5. keep the character renderable
6. retain status for diagnosis/retry

An invalid custom shader must never make the VRM model itself unrecoverable.

## Shader variants

Do not indiscriminately add large shaders to Always Included Shaders.

The package/build pipeline should preserve only required shader variants through explicit material/variant evidence. Variant policy is validated with the package because missing stripped variants can otherwise appear as incorrect/pink rendering.

## Post processing

Post effects are separate from material shaders.

Candidate effects include outline, bloom, color grading, pixelation, glitch, CRT, dissolve/composite, and hologram-style effects.

Post-process alpha compatibility must be validated separately from character-material shader compatibility.
