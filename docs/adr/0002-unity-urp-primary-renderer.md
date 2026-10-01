# ADR-0002: Unity URP as primary renderer

- Status: Accepted
- Date: 2026-10-01

## Context

The product requires VRM rendering, custom shaders, post processing, transparent desktop output, dynamic environments, and scalable operation from lightweight single-character use to advanced one-character scenes on both Windows and macOS.

Unity provides the best fit for the current product constraints because it combines mature real-time rendering, cross-platform desktop deployment, strong VRM ecosystem support, custom shader tooling, and lower runtime/application integration complexity than the evaluated alternatives for the same target visual quality.

## Decision

Use Unity with URP as the primary production renderer/backend for Windows and macOS.

Unity is now an architectural dependency of the initial 3D product implementation.

ADR-0001 and ADR-0008 remain mandatory:

- Unity types do not become shared domain contracts.
- platform-native types do not become shared domain contracts.
- tracking, events, scene state, and external protocols remain behind application/runtime abstractions.

The exact Unity LTS version, URP package version, and UniVRM version are pinned separately after P0 compatibility validation.

## Alternatives considered

- Unreal Engine
- Blender/EEVEE as runtime
- Three.js/WebGL/WebGPU with three-vrm
- Godot
- custom native renderer
- multiple production backends from day one

## Rationale

For the current product:

- the runtime is optimized for one VRM character rather than a large world
- lightweight 720p60/1080p60 operation is a primary requirement
- Windows and macOS are both first-class targets
- built-in webcam/ARKit tracking, event integration, transparent overlay, and desktop application behavior are core features
- advanced 3D environments are supported, but they do not justify making a heavier world-oriented engine the baseline
- Blender remains useful for content authoring, validation, and reference rendering rather than as the application runtime

Unreal may provide a higher ceiling for very large or cinematic worlds, but that advantage does not outweigh the additional runtime/integration complexity for the current product target.

## P0 validation still required

Acceptance of Unity/URP does not remove implementation validation.

Before the production project baseline is frozen, validate on Windows and macOS:

- select and pin Unity LTS version
- select and pin URP version
- UniVRM compatibility
- transparent-window behavior
- OBS-compatible capture
- alpha through URP/post effects
- custom/runtime shader workflow
- high-DPI behavior
- packaging/startup
- lightweight CPU/GPU/memory baseline
- 720p60 minimum target
- 1080p60 recommended target
- advanced-feature scaling
- licensing/distribution implications

Failure of a specific Unity/URP version does not automatically revoke this ADR; first evaluate another supported Unity/URP version.

## Consequences

- Unity/URP becomes the implementation target for the 3D renderer
- shared 3D/rendering/tooling path across Windows and macOS
- UniVRM remains the preferred VRM candidate pending ADR-0003 acceptance
- custom shader architecture is implemented on top of URP while preserving higher-level shader/material abstractions
- platform-specific window/output adapters are still required
- strict anti-leak boundaries remain necessary for future portability and the separate 2D extension

## Revisit conditions

Revisit the engine decision only if Unity/URP proves unable to satisfy a core requirement across supported platforms after reasonable version/backend alternatives are tested, including:

- transparent output/capture
- required VRM behavior
- custom shader requirements
- baseline 60 FPS target
- distribution constraints

A future 2D extension does not by itself supersede this 3D renderer decision.
