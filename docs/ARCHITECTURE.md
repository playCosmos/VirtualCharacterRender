# Architecture

## Purpose
VirtualCharacterRender is a real-time character runtime and broadcast overlay. Tracking, motion, expressions, events, materials, shaders, protocols, and output are separated so individual integrations cannot own the application state.

## System layers
```text
External Inputs
  Camera / ARKit / VMC / OSC / MIDI / WebSocket / Audio
                         ↓
                  Input Adapters
                         ↓
                  Normalized State
                         ↓
        ┌────────────────┼────────────────┐
        │                │                │
   Motion Mixer     Expression State   Event Runtime
        │                │                │
        └────────────────┼────────────────┘
                         ↓
                 Character Runtime
                         ↓
              Rendering Abstractions
          Mesh / Material / Shader / FX
                         ↓
                 Renderer Backend
                         ↓
           Overlay / OBS / Streaming
```

## Architectural goals
- Keep input, state, behavior, rendering, and output independent.
- Treat custom shaders and material overrides as first-class runtime capabilities.
- Allow multiple tracking sources to contribute to one character.
- Keep external protocols outside renderer internals.
- Make renderer, shader, tracking, and plugin failures recoverable where practical.
- Preserve a path to a non-Unity renderer and a future 2D backend.

## Dependency rules
Allowed:
```text
Protocols → Input abstraction
Tracking  → Input abstraction
Runtime   → Rendering abstraction
Backend   → Rendering abstraction implementation
UI        → Application services
```

Disallowed:
```text
Tracking → Unity Transform
Protocol → Material
Event → GameObject
Character domain → concrete shader implementation
```

## Failure containment
- Invalid tracking data is rejected or clamped at adapter boundaries.
- Missing sources fall back to neutral/configured state.
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
- Runtime overrides never destructively rewrite source model materials.
- Custom shaders are overrides, not prerequisites for loading a model.
- A bad custom shader always has a deterministic fallback.
- Tracking sources can be replaced or mixed without changing renderer code.
- External protocols expose application concepts, not backend object handles.
