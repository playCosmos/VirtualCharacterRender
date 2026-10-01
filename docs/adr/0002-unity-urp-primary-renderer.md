# ADR-0002: Unity URP as primary renderer

- Status: Proposed
- Date: 2026-10-01

## Context
The initial product requires VRM rendering, transparent desktop output, custom shaders, post processing, and a mature Windows runtime.

## Proposed decision
Use Unity with URP as the first production renderer/backend.

ADR-0001 remains mandatory so Unity types do not become domain contracts.

## Alternatives
- Three.js/WebGL/WebGPU with three-vrm
- Godot
- custom native renderer
- multiple production backends from day one

## P0 validation
- select target Unity/LTS version
- transparent-window behavior
- OBS capture
- alpha through URP/post effects
- UniVRM compatibility
- custom/runtime shader workflow
- packaging/startup/performance
- licensing/distribution implications

## Consequences if accepted
- mature 3D tooling and shader workflow
- Unity runtime/toolchain dependency
- strict anti-leak boundaries required for portability

## Revisit conditions
Reject or defer if alpha/output behavior, distribution constraints, runtime cost, or shader limitations conflict materially with the product goals.
