# Output

## Scope

Output is the final platform/window boundary. It does not own character state, tracking, scene logic, render resolution presets, or frame pacing.

```text
Scene/Character
      ↓
Unity URP render
      ↓
back buffer with alpha
      ↓
IOverlayOutputAdapter
      ↓
platform window
      ↓
OBS / desktop composition
```

## P0 implementation

Shared contract:

- `OverlayOutputSettings`
- `OverlayOutputStatus`
- `IOverlayOutputAdapter`

Unity/platform adapter:

- `UniWinCOverlayOutput`

Candidate native dependency:

- UniWindowController 0.9.8
- MIT
- pinned by Git tag/subfolder
- Windows/macOS only for current output assembly

## Responsibility split

`DesktopRenderBootstrap`:

- 720p/1080p/custom resolution
- 60 FPS target
- vSync policy
- camera render quality

`UniWinCOverlayOutput`:

- transparent window
- topmost
- explicit click-through
- native client-size/status reporting

This prevents output backends from changing performance/render policy.

## Lightweight hit-test policy

Automatic opacity sampling is disabled.

Default:

```text
isHitTestEnabled = false
HitTestType = None
```

Click-through is an explicit state change. This avoids continuous pixel sampling in the normal broadcast overlay.

## Alpha path

P0 uses:

- URP 17 Alpha Processing enabled
- SDR RGBA8
- camera HDR off
- Solid Color clear
- clear alpha = 0
- direct back-buffer/window composition

`P0AlphaTestPattern` renders reference patches through that same path:

- alpha 1.00
- alpha 0.50
- alpha 0.25
- overlapping red/blue 0.50 patches

It exists only for P0 visual evidence and can be hidden for normal character tests.

## Windows

Static baseline:

- Windows x86-64
- D3D11 only
- automatic graphics API selection off
- D3D11 flip-model swapchain off
- windowed/resizable
- run in background

OBS:

- use Game Capture
- enable transparency
- compare the alpha reference patches and VRM hair/outline edges

## macOS

Baseline:

- Apple Silicon M1+
- Metal
- standalone player required for native transparency validation
- Retina and multi-monitor checks required

OBS:

- validate the macOS window/screen capture workflow
- do not assume Windows Game Capture behavior exists on macOS

## Failure behavior

Output failure must not invalidate the loaded character or tracking state.

The adapter exposes support/activity/error status. A future application layer can fall back to an opaque preview window or another output adapter without reloading the character.

## P0 status

Static architecture and configuration are implemented.

Still unverified in this repository environment:

- Unity package resolution/compile
- Windows standalone alpha
- macOS standalone alpha
- OBS capture
- real frame-time impact
- resize/Retina/high-DPI/multi-monitor behavior
- lifecycle recovery

Do not mark ADR-0007 Accepted until both platform evidence sets exist.
