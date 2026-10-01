# Shader System

## Status
Custom shader support is a core product requirement.

## Required capabilities
- default VRM/MToon rendering
- per-material custom shader override
- whole-character shader presets
- runtime parameter editing
- runtime/external parameter binding
- post-process effects
- safe fallback on failure
- future shader packages/plugins

## Material model
```text
Source Material
      ↓
Material Slot
      ↓
Optional Override
      ↓
Runtime Material Instance
      ↓
Shader Instance
```

## Parameter types
Float, Int, Bool, Color, Vector2/3/4, Texture, Enum.

Each definition contains a stable ID, display name, type, default, optional range/step, runtime mutability, serialization policy, and optional UI hints.

## Bindings
Parameters may be driven by tracking, audio, expressions, animation, OSC, VMC-derived values, MIDI, WebSocket/API commands, event output, or procedural/time sources.

Bindings support remap, clamp, curve, smoothing, and blending.

## Presets
```text
Character Profile
  └─ Material Slot Override
       ├─ Shader ID
       ├─ Parameters
       └─ Bindings
```

## Shader packages
Planned:
```text
package/
├─ manifest.json
├─ shaders/
├─ textures/
├─ preview/
└─ defaults/
```

The exact schema is deferred until P2/P3 evidence exists.

## Failure behavior
1. mark shader instance unhealthy
2. report compile/load error
3. detach failing instance
4. resolve fallback material
5. keep character renderable
6. retain configuration for diagnosis/retry

## Post processing
Post effects are separate from material shaders. Candidate effects include outline, bloom, color grading, pixelation, glitch, CRT, dissolve/composite, and hologram-style effects.
