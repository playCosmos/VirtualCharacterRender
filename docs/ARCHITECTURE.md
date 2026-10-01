# Architecture

## Purpose

VirtualCharacterRender is a cross-platform, one-character-first real-time 3D character runtime, built-in motion-capture system, dynamic-environment system, broadcast-event system, and overlay.

The product is implemented for one active performer/avatar. Advanced features deepen the scene, environment, events, shaders, effects, and integrations around that character rather than introducing multi-character orchestration.

## System layers

```text
Built-in / External Inputs
 Webcam / ARKit / VMC / OSC / WebSocket / MIDI / Audio
 Broadcast chat / Donation / Tracking subject presence / Local state
                           ↓
                     Input Adapters
                           ↓
       Normalized Tracking + Normalized Events
                           ↓
        ┌──────────────────┼──────────────────┐
        │                  │                  │
   Motion Mixer      Expression State     Event Runtime
        │                  │                  │
        └──────────────────┼──────────────────┘
                           ↓
                    Scene Runtime
             ┌─────────────┼─────────────┐
             ↓             ↓             ↓
         Character    Environment      Props/FX
             └─────────────┼─────────────┘
                           ↓
                 Rendering Abstractions
                           ↓
                   Renderer Backend
                           ↓
                 Platform Output Adapter
                           ↓
                     Overlay / OBS
```

## Product scope

Current implementation scope:

- exactly one active character
- 3D/VRM first
- face, head, hands, and upper-body basic tracking
- full-body tracking optional
- dynamic environment
- broadcast/local event automation
- Windows and macOS

Multi-character and 2D are future expansion areas, not current implementation targets.

## Capability-driven runtime

Optional systems are capabilities, not separate editions.

```text
Runtime
 ├─ Core — always active
 ├─ Character
 ├─ Rendering
 ├─ Tracking adapters
 ├─ Motion/Expression
 ├─ Environment
 ├─ Protocols
 ├─ Events
 ├─ Plugins
 └─ Output adapters
```

Unused optional subsystems remain uninitialized or idle where practical.

See `RUNTIME_PROFILES.md`.

## Architectural goals

- Windows and macOS are first-class targets.
- Defend 60 FPS for the validated one-character baseline on an otherwise-unloaded host PC.
- Keep input, state, behavior, rendering, and output independent.
- Use one pipeline from lightweight to advanced one-character operation.
- Provide built-in basic motion capture without requiring another VTuber application.
- Prioritize face quality, especially eyes and mouth, while also supporting head, hands, and upper body.
- Keep full-body tracking separate from the baseline tracking budget.
- Treat environment, events, and custom shaders as first-class capabilities.
- Keep streaming-platform integrations outside event/runtime internals.
- Make renderer, shader, tracking, plugin, event-adapter, and output failures recoverable where practical.
- Preserve future paths to multi-character and 2D without implementing them now.

## Platform boundary

Shared runtime code cannot require Win32, DirectX, Cocoa, Metal, or platform-native window handles.

Platform-specific implementation belongs behind adapters for window composition, transparent/click-through behavior, capture/output integration, device enumeration where necessary, permissions, packaging/signing, and platform lifecycle.

See `PLATFORM_SUPPORT.md`.

## Dependency rules

Allowed:

```text
Protocols/Event adapters → normalized input abstractions
Tracking                → normalized tracking abstraction
Runtime                 → rendering abstraction
Environment             → scene/rendering abstractions
Backend                 → rendering abstraction implementation
Output                  → platform output abstraction
UI                      → application services
```

Disallowed:

```text
Tracking → Unity Transform
Chat/Donation Adapter → GameObject
Protocol → Material
Event → concrete renderer object
Character domain → concrete shader implementation
Shared runtime → Win32/Cocoa object
```

## Failure containment

- Invalid tracking/event data is rejected or clamped at adapter boundaries.
- Missing tracking sources fall back to neutral/configured state.
- Camera/mobile/full-body tracking can be disabled without leaving inference/network work active.
- Broadcast adapters can disconnect without blocking or corrupting render state.
- Custom shader resolution/load/application failure restores the source material or another deterministic safe fallback.
- Plugin failure must not terminate the renderer.
- Network/protocol work must not block render timing.
- Output failure must not corrupt character or scene state.

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

- The supported product path contains one active character.
- External input never mutates renderer state directly.
- Built-in webcam/mobile capture and external VMC share normalized tracking state.
- Platform event adapters emit normalized events.
- Runtime overrides never destructively rewrite source model materials.
- Custom shaders are overrides, not prerequisites for model load.
- A bad custom shader always has a deterministic fallback.
- Tracking sources can be replaced or mixed without changing renderer code.
- Lightweight and advanced feature sets use the same character/runtime contracts.
- Runtime capability settings and graphics quality settings are independent.
- External protocols expose application concepts, not backend object handles.
