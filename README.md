# VirtualCharacterRender

VirtualCharacterRender is a cross-platform real-time 3D virtual-character rendering, basic motion-capture, dynamic-environment, event, and broadcast-overlay project for Windows and macOS.

The initial product is designed for exactly one active performer/avatar. It uses one scalable runtime pipeline: a lightweight one-character session initializes only the services it needs, while the same character/runtime pipeline can enable advanced scenes, dynamic environments, broadcast events, custom shaders, protocols, and plugin-driven workflows.

## Product direction

The application targets:

- one active 3D character
- VSeeFace-class lightweight desktop operation
- Warudo-class advanced scene/event/shader extensibility for that one character
- Windows and macOS as first-class desktop targets
- VRM 0.x / VRM 1.0 character support
- built-in basic motion capture
  - face, especially eyes and mouth
  - head
  - hands
  - upper body
- webcam and Apple ARKit-compatible face tracking sources
- full-body tracking as a separate optional capability
- VMC/OSC/WebSocket interoperability
- dynamic environments from static/lightweight backgrounds to reactive 3D spaces
- broadcast event inputs such as away/AFK, chat, donations/support, commands, and external events
- MToon plus material overrides and custom shaders
- transparent broadcast overlay output suitable for OBS
- 2D support later as a separate extension/backend

## Output targets

- minimum supported target: 1280×720 at 60 FPS
- recommended target: 1920×1080 at 60 FPS
- higher/custom resolutions are supported, but performance guarantees are documented separately

The baseline performance goal is to defend 60 FPS when the host PC is otherwise not under meaningful load.

## One pipeline, optional capabilities

```text
Tracking / Broadcast / Local Inputs
               ↓
            Adapters
               ↓
  Normalized Tracking / Events
               ↓
 Motion / Expression / Event Runtime
               ↓
     Character / Scene / Environment
               ↓
       Rendering Abstractions
               ↓
          Renderer Backend
               ↓
       Platform Output Adapter
```

Lightweight and advanced operation do not use separate character/rendering pipelines. Optional services are activated through capabilities and lazy initialization.

## Repository layout

```text
VirtualCharacterRender/
├─ docs/                 Architecture, roadmap, subsystem specifications
│  └─ adr/               Architecture Decision Records
├─ unity/                Primary renderer/backend candidate
├─ plugins/              Extension/package conventions
├─ schemas/              Portable data and package schemas
├─ samples/              Example scenes, bindings, shader/environment packages
└─ tools/                Development and content-pipeline tools
```

The production Unity project is intentionally not scaffolded until the P0 decisions and validation gates in `docs/ROADMAP.md` are satisfied.

## Architecture documents

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Performance](docs/PERFORMANCE.md)
- [Runtime profiles](docs/RUNTIME_PROFILES.md)
- [Platform support](docs/PLATFORM_SUPPORT.md)
- [Rendering](docs/RENDERING.md)
- [Shader system](docs/SHADER_SYSTEM.md)
- [Tracking](docs/TRACKING.md)
- [Event system](docs/EVENT_SYSTEM.md)
- [Environment runtime](docs/ENVIRONMENT.md)
- [Protocols](docs/PROTOCOLS.md)
- [Architecture Decision Records](docs/adr/README.md)

## Core rules

- The product is implemented and validated for one active character.
- Multi-character support is not an initial feature; future expansion must remain possible without shaping current UI/tracking complexity.
- External inputs never mutate renderer objects directly.
- Windows and macOS share application/runtime contracts.
- Built-in and external tracking use the same normalized tracking state.
- Built-in basic tracking includes face/eyes/mouth, head, hands, and upper body.
- Full-body tracking is optional and separate.
- Disabled optional capabilities should have no meaningful recurring frame cost.
- Custom shaders are first-class but always have a safe fallback path.
- 2D is a later extension; the initial implementation is 3D-first.

## Status

P0 architecture and feasibility.
