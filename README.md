# VirtualCharacterRender

VirtualCharacterRender is a cross-platform real-time virtual character rendering, motion-capture, scene, and broadcast overlay project for Windows and macOS.

The product uses one scalable runtime pipeline: a lightweight single-avatar session can run with only the services it needs, while the same runtime can expand into advanced multi-character scenes, event automation, custom shaders, external tracking, and plugin-driven workflows.

## Product direction

The same application must cover:

- VSeeFace-class lightweight avatar operation
- built-in simple motion capture from webcam and Apple ARKit-compatible face tracking sources
- external VMC/OSC tracking interoperability
- Warudo-class advanced scene/event extensibility
- Windows and macOS as first-class desktop targets
- VRM 0.x / VRM 1.0 character support
- MToon plus material overrides and custom shaders
- transparent broadcast overlay output suitable for OBS
- scalable tracking, motion/expression mixing, protocols, events, props, effects, and plugins
- a future 2D backend using the same upstream runtime contracts

## One pipeline, optional capabilities

```text
Input Sources
    ↓
Adapters
    ↓
Normalized State
    ↓
Motion / Expression / Event Runtime
    ↓
Character / Scene Runtime
    ↓
Rendering Abstractions
    ↓
Renderer Backend
    ↓
Platform Output Adapter
```

Lightweight and advanced usage do not use separate character or rendering pipelines. Runtime profiles only control which capabilities and services are activated.

## Repository layout

```text
VirtualCharacterRender/
├─ docs/                 Architecture, roadmap, subsystem specifications
│  └─ adr/               Architecture Decision Records
├─ unity/                Primary renderer/backend candidate
├─ plugins/              Extension/package conventions
├─ schemas/              Portable data and package schemas
├─ samples/              Example scenes, bindings, and shader packages
└─ tools/                Development and content-pipeline tools
```

The production Unity project is intentionally not scaffolded until the P0 decisions and validation gates in `docs/ROADMAP.md` are satisfied.

## Architecture documents

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Runtime profiles](docs/RUNTIME_PROFILES.md)
- [Platform support](docs/PLATFORM_SUPPORT.md)
- [Rendering](docs/RENDERING.md)
- [Shader system](docs/SHADER_SYSTEM.md)
- [Tracking](docs/TRACKING.md)
- [Protocols](docs/PROTOCOLS.md)
- [Architecture Decision Records](docs/adr/README.md)

## Core rules

- External inputs never mutate renderer objects directly.
- Windows and macOS share application/runtime contracts.
- Built-in and external tracking use the same normalized tracking state.
- Lightweight operation does not initialize unused advanced services.
- Custom shaders are first-class but always have a safe fallback path.

## Status

P0 architecture and feasibility.
