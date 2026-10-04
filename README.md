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
- broadcast and tracking-derived event inputs such as SubjectLost/SubjectRestored, chat, donations/support, commands, and external events
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
├─ unity/                Primary Unity/URP renderer/backend
├─ plugins/              Extension/package conventions
├─ schemas/              Portable data and package schemas
├─ samples/              Example scenes, bindings, shader/environment packages
└─ tools/                Development and content-pipeline tools
```

The source implementation has progressed through the P11 Application UI and P12 Advanced One-Character Scene Tooling checkpoints into P13 2D Extension work. P11 provides the runtime application shell and Appearance / Quick Change runtime; P12 adds wardrobe/accessory authoring, scene/effect automation, event-node authoring, package/import tooling, and skinned-compatibility preview; P13 now adds a backend-neutral optional 2D presentation host plus shared normalized parameter-mapping profiles without making Live2D/Inochi2D SDKs core dependencies. P0 real-device/platform evidence remains intentionally deferred: Windows/macOS standalone, hardware tracking quality, OBS output, timed appearance choreography, real 2D SDK/model rendering, and measured performance still require evidence before those gates can be called complete.

## Architecture documents

- [P0 status](docs/P0_STATUS.md)
- [P1 status](docs/P1_STATUS.md)
- [P2 status](docs/P2_STATUS.md)
- [P3 status](docs/P3_STATUS.md)
- [P4 status](docs/P4_STATUS.md)
- [P5 status](docs/P5_STATUS.md)
- [P6 status](docs/P6_STATUS.md)
- [P7 status](docs/P7_STATUS.md)
- [P8 status](docs/P8_STATUS.md)
- [P9 status](docs/P9_STATUS.md)
- [P10 status](docs/P10_STATUS.md)
- [P11 status](docs/P11_STATUS.md)
- [P12 status](docs/P12_STATUS.md)
- [P13 status](docs/P13_STATUS.md)
- [P0 validation plan](docs/P0_VALIDATION_PLAN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Performance](docs/PERFORMANCE.md)
- [Runtime profiles](docs/RUNTIME_PROFILES.md)
- [Platform support](docs/PLATFORM_SUPPORT.md)
- [Rendering](docs/RENDERING.md)
- [Shader system](docs/SHADER_SYSTEM.md)
- [Tracking](docs/TRACKING.md)
- [Event system](docs/EVENTS.md)
- [Environment runtime](docs/ENVIRONMENT.md)
- [Protocols](docs/PROTOCOLS.md)
- [Architecture Decision Records](docs/adr/README.md)

## Core rules

- The product is implemented and validated for one active character.
- Multi-character support is not an initial feature; future expansion must remain possible without shaping current UI/tracking complexity.
- External inputs never mutate renderer objects directly.
- Unity/URP is the primary 3D renderer on Windows and macOS; shared application/runtime contracts remain engine-facing abstractions.
- Built-in and external tracking use the same normalized tracking state.
- Built-in basic tracking includes face/eyes/mouth, head, hands, and upper body.
- Full-body tracking is optional and separate.
- Disabled optional capabilities should have no meaningful recurring frame cost.
- Custom shaders are first-class but always have a safe fallback path.
- 2D is a later extension; the initial implementation is 3D-first.

## Status

P1-P12 source implementations are preserved as source checkpoints. P13 2D Extension is active with a backend-neutral presentation runtime, backend/model lifecycle contract, supported-domain tracking polling, immutable-frame deduplication, diagnostics, and SDK-neutral parameter mapping authoring/evaluation. No production 2D backend has been accepted yet; Live2D/Inochi2D integration remains isolated behind optional adapter/package boundaries. Timed transition behavior, real VRM appearance switching, and real 2D rendering are not marked PASS without Unity runtime evidence. Executable third-party plugins remain deferred under ADR-0015.

Real-device/platform validation remains deferred rather than assumed: real VRM 0.x/1.0 runtime validation, webcam/ARKit tracking quality and cost, VMC external interoperability, Windows/macOS transparent output + OBS capture, custom-shader bundle validation, and 720p60/1080p60 measurements still require evidence.
