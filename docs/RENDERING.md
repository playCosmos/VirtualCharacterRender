# Rendering

## Objective

Provide predictable low-latency character rendering with correct transparent broadcast output on Windows and macOS while keeping core runtime contracts backend-independent.

## Primary candidate

Unity with URP is the P0 candidate. It is not final until ADR-0002 is accepted after validation on both desktop targets.

## Pipeline

```text
Character / Scene Runtime
          ↓
    Renderable State
          ↓
   Material Resolution
          ↓
    Shader Resolution
          ↓
     Scene Rendering
          ↓
    Post Processing
          ↓
  Output Abstraction
      ↓         ↓
  Windows     macOS
  adapter     adapter
```

## Material resolution

1. runtime material override
2. character/profile override
3. imported source material
4. fallback material

Overrides are per material slot.

## Runtime scale

Rendering uses the same pipeline for all profiles.

Lightweight operation reduces work through capability/resource policy:

- fewer active characters
- unused effects disabled
- no unnecessary render targets
- no advanced scene/event updates unless enabled
- update-rate reduction for eligible inactive elements
- no optional plugin/output initialization without use

Advanced operation adds capabilities without changing the character/material contracts.

## Transparency P0 checks

Required independently on Windows and macOS:

- true alpha
- straight vs premultiplied alpha
- hair/outline edge artifacts
- transparent post-processing
- OBS-compatible capture behavior
- high-DPI/retina behavior
- transparent/click-through/topmost window behavior
- resize and multi-monitor behavior

Chroma key is not a substitute for correct alpha.

## Metrics

- CPU/GPU frame time
- render FPS
- input-to-runtime age
- tracking update rate
- dropped/late updates
- shader/material error count
- active capability/service count
- render-target/resource usage

## Multi-character

The runtime must not assume a single character even if Lightweight UI defaults to one.

## Backend escape policy

Unity GameObject, Material, RenderTexture, Camera and platform-native objects remain inside backend/platform modules. Public APIs use IDs, descriptors, handles, and application-level commands.
