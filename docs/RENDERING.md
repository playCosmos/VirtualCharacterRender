# Rendering

## Objective
Provide predictable low-latency character rendering with correct transparent broadcast output while keeping core runtime contracts backend-independent.

## Primary candidate
Unity with URP is the P0 candidate. It is not final until ADR-0002 is accepted.

## Pipeline
```text
Character Runtime
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
Output Surface
```

## Material resolution
1. runtime material override
2. character/profile override
3. imported source material
4. fallback material

Overrides are per material slot.

## Transparency P0 checks
- true alpha
- straight vs premultiplied alpha
- hair/outline edge artifacts
- transparent post-processing
- OBS capture behavior
- Windows compositor behavior

Chroma key is not a substitute for correct alpha.

## Metrics
- render FPS/frame time
- input-to-runtime age
- tracking update rate
- dropped/late updates
- shader/material error count

## Multi-character
The runtime must not assume a single character even if the first UI exposes only one.

## Backend escape policy
Unity GameObject, Material, RenderTexture, Camera and similar concrete objects remain inside backend modules. Public APIs use IDs, descriptors, handles, and application-level commands.
