# ADR-0007: Transparent window as initial broadcast output

- Status: Proposed
- Date: 2026-10-01

## Context

VirtualCharacterRender is intended to act as a local virtual-character rendering overlay for broadcasting on Windows and macOS. The minimum output path should be easy to use and should not require a separate video transport.

The product needs correct alpha, low overhead, topmost/click-through control, resize/high-DPI behavior, and an OBS-compatible capture workflow on both supported desktop platforms.

## Proposed decision

Use a transparent desktop window as the initial output abstraction.

For P0, use UniWindowController 0.9.8 as the candidate native window adapter behind the project-owned `IOverlayOutputAdapter` boundary.

The dependency is pinned to the UPM release commit for 0.9.8 rather than the moving `upm` branch:

```text
com.kirurobo.uniwinc
https://github.com/kirurobo/UniWindowController.git#304f9ba2aa4a8fae7f3c71f38118c44722a2f6cc
```

The release commit contains the UPM package at repository root.

The shared runtime does not expose Kirurobo, Win32, Cocoa, Metal, D3D, Unity Camera, or native window types.

High-performance transports such as Spout-class Windows output or Syphon-class macOS output remain optional future adapters.

## P0 Windows baseline

The candidate alpha-window path requires:

- Windows standalone x86-64
- Direct3D 11
- automatic graphics API selection disabled
- D3D12 excluded from the transparent-window baseline
- D3D11 flip-model swapchain disabled so Unity uses the BitBlt path required by the DWM transparency technique used by the adapter
- windowed/resizable player
- URP Alpha Processing enabled
- SDR RGBA8 path with camera HDR disabled
- camera background Solid Color with alpha 0

OBS validation uses Game Capture with transparency enabled.

The BitBlt constraint may cost some presentation efficiency. P0 measures the real delta instead of assuming it is negligible.

## P0 macOS baseline

The candidate path uses the same project-owned output abstraction with UniWindowController's macOS native implementation.

Validate on Apple Silicon M1 or newer:

- Metal standalone player
- true alpha
- Retina scaling
- topmost
- explicit click-through
- move/resize and multi-monitor behavior
- OBS macOS Screen Capture/window workflow
- sleep/wake and relaunch behavior

macOS acceptance is evidence-based. Cross-platform API support in the dependency is not itself a PASS.

## Hit testing

Automatic per-pixel opacity hit testing is disabled in the lightweight baseline.

The P0 adapter uses explicit click-through state:

```text
isHitTestEnabled = false
HitTestType = None
```

This avoids continuous pixel-readback work. A later interactive-overlay mode may use raycast or another measured hit-test policy.

## Rendering requirements

URP 17 Alpha Processing must be enabled. Otherwise post-processing can replace output alpha with 1.

The initial low-complexity alpha format is SDR RGBA8:

- camera HDR off
- transparent camera clear alpha
- no separate RenderTexture for the basic overlay path

A small P0 alpha-reference pattern renders opaque, 50%, 25%, and overlapping semi-transparent patches through the same back buffer as the character.

## Ownership

```text
Rendering subsystem
  ├ resolution
  ├ frame pacing
  └ camera render quality

Output subsystem
  ├ transparent window
  ├ topmost
  ├ click-through
  └ native window status
```

Output does not own the 720p/1080p preset or the 60 FPS frame policy.

## P0 validation

Static checks:

- package resolves as version 0.9.8 from pinned commit `304f9ba2aa4a8fae7f3c71f38118c44722a2f6cc`
- camera background alpha is 0
- HDR is disabled for the SDR baseline
- URP Alpha Processing is enabled
- Windows graphics API is explicitly D3D11
- D3D11 flip model is disabled
- automatic per-pixel hit testing is disabled

Standalone checks on Windows and macOS independently:

- true alpha
- straight/premultiplied edge correctness
- semi-transparent overlap correctness
- VRM hair/outline edge behavior
- OBS-compatible capture
- high-DPI/Retina behavior
- resize and multi-monitor behavior
- topmost/click-through behavior
- 720p60 minimum baseline
- 1080p60 measurement
- shutdown/relaunch
- no persistent output-related frame-time regression

## Consequences if accepted

- simple local broadcast workflow
- platform window/compositor behavior becomes part of the support surface
- output implementation remains replaceable behind a project-owned interface
- optional high-performance native video transports remain possible

## Sources

- Unity URP 17 Alpha Processing:
  https://docs.unity3d.com/6000.0/Documentation/Manual/urp/whats-new/urp-whats-new.html
- Unity PlayerSettings graphics APIs:
  https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerSettings.SetGraphicsAPIs.html
- UniWindowController:
  https://github.com/kirurobo/UniWindowController
- OBS Game Capture:
  https://obsproject.com/kb/game-capture-source

## Revisit conditions

Accept after the transparent standalone/OBS gates pass on both Windows and macOS.

Replace the P0 candidate adapter if either platform has unreliable alpha, unacceptable presentation overhead, unresolved platform lifecycle failures, or a better lower-complexity native path is demonstrated.
