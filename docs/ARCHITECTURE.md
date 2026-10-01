# Architecture

## Purpose

VirtualCharacterRender is a cross-platform real-time character runtime, basic motion-capture system, advanced scene system, and broadcast overlay.

The architecture must scale from lightweight single-character operation to advanced multi-character/event-driven operation without switching pipelines or data models.

## System layers

```text
External / Built-in Inputs
 Webcam / ARKit / VMC / OSC / MIDI / WebSocket / Audio
                           ↓
                    Input Adapters
                           ↓
                    Normalized State
                           ↓
          ┌────────────────┼─────────────────┐
          │                │                 │
     Motion Mixer     Expression State   Event Runtime
          │                │                 │
          └────────────────┼─────────────────┘
                           ↓
                Character / Scene Runtime
                           ↓
                 Rendering Abstractions
             Mesh / Material / Shader / FX
                           ↓
                  Renderer Backend
                           ↓
                Platform Output Adapter
                           ↓
                   Overlay / OBS
```

## Capability-driven runtime

A capability registry controls optional systems. Runtime profiles are predefined capability sets and budgets, not separate implementations.

```text
Runtime
 ├─ Core — always active
 ├─ Character
 ├─ Rendering
 ├─ Tracking adapters
 ├─ Motion/Expression
 ├─ Protocols
 ├─ Scene
 ├─ Events
 ├─ Plugins
 └─ Output adapters
```

Unused optional subsystems should remain uninitialized or idle.

See `RUNTIME_PROFILES.md`.

## Architectural goals

- Windows and macOS are first-class targets.
- Keep input, state, behavior, rendering, and output independent.
- Use one pipeline from lightweight to advanced operation.
- Provide built-in basic motion capture without requiring another VTuber application.
- Treat custom shaders/material overrides as first-class capabilities.
- Allow multiple tracking sources to contribute to one character.
- Keep external protocols outside renderer internals.
- Make renderer, shader, tracking, plugin, and output failures recoverable where practical.
- Preserve a path to a non-Unity renderer and future 2D backend.

## Platform boundary

Shared runtime code cannot require Win32, DirectX, Cocoa, Metal, or platform-native window handles.

Platform-specific implementation belongs behind adapters for:

- window composition
- transparent/click-through behavior
- capture/output integration
- device enumeration where necessary
- permissions
- packaging/signing
- platform lifecycle

See `PLATFORM_SUPPORT.md`.

## Dependency rules

Allowed:

```text
Protocols → Input abstraction
Tracking  → Input abstraction
Runtime   → Rendering abstraction
Backend   → Rendering abstraction implementation
Output    → Platform output abstraction
UI        → Application services
```

Disallowed:

```text
Tracking → Unity Transform
Protocol → Material
Event → GameObject
Character domain → concrete shader implementation
Shared runtime → Win32/Cocoa object
```

## Failure containment

- Invalid tracking data is rejected or clamped at adapter boundaries.
- Missing sources fall back to neutral/configured state.
- Camera/mobile tracking can be disabled without leaving inference/network work active.
- Shader compile/load failure falls back to a safe material.
- Plugin failure must not terminate the renderer.
- Network/protocol work must not block render timing.
- Output failure must not corrupt character state.

## Repository modules

```text
docs/       Decisions and subsystem specifications
unity/      Primary renderer/backend candidate
plugins/    Extension contracts and package conventions
schemas/    Portable configuration/package schemas
samples/    Reference examples
tools/      Build, validation, conversion, diagnostics
```

## Architectural invariants

- External input never mutates renderer state directly.
- Built-in webcam/mobile capture and external VMC share normalized state.
- Runtime overrides never destructively rewrite source model materials.
- Custom shaders are overrides, not prerequisites for model load.
- A bad custom shader always has a deterministic fallback.
- Tracking sources can be replaced or mixed without changing renderer code.
- Lightweight and advanced modes use the same character/runtime contracts.
- External protocols expose application concepts, not backend object handles.
