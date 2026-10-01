# Rendering

## Objective

Provide predictable low-latency one-character 3D rendering with correct transparent broadcast output on Windows and macOS while keeping core runtime contracts backend-independent.

## Primary candidate

Unity with URP is the P0 candidate. It remains Proposed until validated on both desktop targets.

## Output targets

- minimum supported: 1280×720 at 60 FPS
- recommended: 1920×1080 at 60 FPS
- higher/custom resolutions: allowed, with separate performance guarantees

The validated baseline should defend 60 FPS when the host PC is otherwise not meaningfully loaded.

See `PERFORMANCE.md`.

## Pipeline

```text
Character / Scene / Environment Runtime
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
           ↓           ↓
       Windows       macOS
       adapter       adapter
```

## Material resolution

1. runtime material override
2. character/profile override
3. imported source material
4. fallback material

Overrides are per material slot.

## One-character optimization

The renderer is optimized and tested around one active character.

Do not spend current implementation complexity on:

- multi-character culling
- multi-character material batching policy
- multi-character tracking/render synchronization
- multi-character performance guarantees

Avoid irreversible singleton assumptions where doing so is inexpensive, but do not build unused multi-character systems.

## Runtime scale

Advanced features add environment, events, effects, shaders, props, cameras, and integrations around the same character.

Lightweight/default operation reduces work through capability/resource policy:

- unused effects disabled
- no unnecessary render targets
- no heavy environment features unless enabled
- no event/protocol work unless configured
- no full-body solver unless enabled
- no optional plugin/output initialization without use

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
- render FPS and P95/P99 frame time
- input-to-runtime age
- tracking update rate
- dropped/late updates
- shader/material error count
- environment cost
- active capability/service count
- render-target/resource usage

## Backend escape policy

Unity GameObject, Material, RenderTexture, Camera and platform-native objects remain inside backend/platform modules. Public APIs use IDs, descriptors, handles, and application-level commands.
