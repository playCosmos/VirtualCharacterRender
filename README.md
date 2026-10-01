# VirtualCharacterRender

VirtualCharacterRender is a real-time virtual character rendering and broadcast overlay project.

The project is designed around a renderer-independent runtime boundary: tracking, motion, expressions, events, materials, shaders, protocols, and output are separated so that individual backends can evolve without coupling the whole application to one input source or rendering implementation.

## Initial scope

The first implementation target is a Windows-oriented 3D runtime with:

- Unity URP rendering
- VRM 0.x / VRM 1.0 through UniVRM
- MToon plus overridable/custom materials
- Custom shader packages and runtime parameter binding
- VMC input/output
- Pluggable tracking sources
- Motion and expression mixing
- Transparent overlay output suitable for OBS
- OSC/WebSocket integration
- A future event/node runtime
- A pluggable 2D backend after the 3D runtime is stable

## Repository layout

```text
VirtualCharacterRender/
├─ docs/                 Architecture, roadmap, subsystem specifications
│  └─ adr/               Architecture Decision Records
├─ unity/                Unity runtime and project-facing integration
├─ plugins/              Extension/package conventions
├─ schemas/              Portable data and package schemas
├─ samples/              Example scenes, bindings, and shader packages
└─ tools/                Development and content-pipeline tools
```

The Unity project itself is intentionally not scaffolded until the P0 decisions and validation gates documented in `docs/ROADMAP.md` are satisfied.

## Architecture documents

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Rendering](docs/RENDERING.md)
- [Shader system](docs/SHADER_SYSTEM.md)
- [Tracking](docs/TRACKING.md)
- [Protocols](docs/PROTOCOLS.md)
- [Architecture Decision Records](docs/adr/README.md)

## Core rule

External inputs never mutate renderer objects directly.

```text
Input Sources
    ↓
Normalized State
    ↓
Runtime / Mixers
    ↓
Rendering Abstractions
    ↓
Backend
```

This boundary is the central compatibility rule for future tracking sources, rendering backends, 2D support, and external integrations.

## Status

Project bootstrap / P0 architecture.
