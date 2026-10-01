# Rendering

## Objective

Provide predictable low-latency one-character 3D rendering through Unity URP with correct transparent broadcast output on Windows and macOS while keeping core runtime contracts backend-independent.

## Primary renderer

Unity with URP is the accepted primary renderer for the 3D product. P0 still pins and validates the exact Unity LTS, URP, and UniVRM versions on both desktop targets.

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

## Transparent output P0 path

The shared output contract is independent from the native window implementation:

```text
Render back buffer
      ↓
IOverlayOutputAdapter
      ↓
UniWinCOverlayOutput (P0 candidate)
      ↓
Windows/macOS native window
```

The dependency is pinned to UniWindowController 0.9.8 for P0.

Rendering and output ownership are separate:

- `DesktopRenderBootstrap` owns resolution and frame pacing
- `UniWinCOverlayOutput` owns transparency, topmost, click-through, and native-window status

### URP alpha

URP 17 Alpha Processing is required.

Initial P0 path:

- SDR
- RGBA8
- HDR disabled
- Solid Color camera background
- clear alpha 0

`P0AlphaTestPattern` provides opaque, 50%, 25%, and overlapping semi-transparent patches through the same back-buffer path used by the character.

### Windows

P0 transparent-window baseline:

- x86-64 standalone
- explicit Direct3D 11
- D3D12 excluded
- D3D11 flip-model swapchain disabled
- windowed/resizable
- OBS Game Capture transparency validation

The BitBlt presentation constraint is measured as part of the performance gate.

### macOS

P0 validates the same output abstraction on Apple Silicon M1+ using the dependency's macOS native adapter and Metal.

OBS uses the macOS screen/window capture workflow rather than Windows Game Capture.

### Hit testing

Automatic opacity hit testing is disabled by default because it adds recurring pixel inspection. The baseline uses explicit click-through state. Interactive automatic hit testing can be added later only with measured cost.

### Required checks

Required independently on Windows and macOS:

- true alpha
- straight vs premultiplied alpha
- semi-transparent overlap
- hair/outline edge artifacts
- transparent post-processing
- OBS-compatible capture behavior
- high-DPI/Retina behavior
- transparent/click-through/topmost window behavior
- resize and multi-monitor behavior
- shutdown/relaunch

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
