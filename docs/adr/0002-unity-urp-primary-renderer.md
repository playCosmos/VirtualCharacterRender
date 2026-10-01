# ADR-0002: Unity URP as primary renderer

- Status: Proposed
- Date: 2026-10-01

## Context

The product requires VRM rendering, custom shaders, post processing, transparent desktop output, and scalable operation from lightweight single-character use to advanced scenes on both Windows and macOS.

Unity currently supports desktop players on both Windows and macOS, making it a plausible shared backend candidate, but project-specific alpha, shader, packaging, and performance behavior still requires validation.

## Proposed decision

Use Unity with URP as the first production renderer/backend for Windows and macOS.

ADR-0001 and ADR-0008 remain mandatory: Unity types and platform-native types cannot become shared domain contracts.

## Alternatives

- Three.js/WebGL/WebGPU with three-vrm
- Godot
- custom native renderer
- separate native renderer per operating system
- multiple production backends from day one

## P0 validation

On both Windows and macOS:

- select and pin target Unity/LTS version
- VRM/UniVRM compatibility
- transparent-window behavior
- OBS-compatible capture
- alpha through URP/post effects
- custom/runtime shader workflow
- high-DPI behavior
- packaging/startup
- lightweight CPU/GPU/memory baseline
- advanced-feature scaling
- licensing/distribution implications

## Consequences if accepted

- shared 3D/rendering/tooling path across both desktop targets
- mature shader/content workflow
- Unity runtime/toolchain dependency
- platform-specific window/output adapters still required
- strict anti-leak boundaries required for portability

## Revisit conditions

Reject or defer if either target platform fails alpha/output requirements, lightweight performance targets, packaging constraints, or required shader/runtime behavior.
