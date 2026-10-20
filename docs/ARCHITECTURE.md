# Architecture

## Purpose

VirtualCharacterRender is a cross-platform, one-character-first real-time 3D character runtime with an optional backend-neutral 2D presentation extension, built-in motion-capture system, dynamic-environment system, broadcast-event system, and overlay.

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
             │
        Appearance
       Outfit/Accessory
             │
      Transition Runtime
   Motion / FX / Commit Marker
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

Multi-character remains a future expansion area. P13 now develops 2D as an optional extension while the supported/default production path remains 3D/VRM-first.

## Capability-driven runtime

Optional systems are capabilities, not separate editions.

```text
Runtime
 ├─ Core — always active
 ├─ Character
 ├─ Optional 2D Presentation Backend
 ├─ Appearance / Wardrobe
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
- Preserve the future multi-character path while keeping the active 2D extension isolated behind backend-neutral contracts.

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
2D presentation host     → normalized tracking + optional backend adapter
2D backend adapter       → backend-specific SDK/package only
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
Presentation2D core → Live2D/Inochi2D concrete SDK type
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
- Appearance quick changes preserve the active character runtime and must not reset tracking, motion/expression, environment, event, or output state.
- Outfit/accessory changes are transactional: invalid or incompatible requests leave the previous complete appearance active.
- Choreographed appearance transitions are presentation orchestration around one atomic appearance commit; motion/effects cannot partially mutate wardrobe state.
- Appearance transition definitions reference logical motion/effect/action IDs, never Unity object instance references.
- User-authored transition actions execute through registered application-level handlers and must not recursively emit arbitrary normalized events to drive the same transition.
- Custom shaders are overrides, not prerequisites for model load.
- A bad custom shader always has a deterministic fallback.
- Tracking sources can be replaced or mixed without changing renderer code.
- Lightweight and advanced feature sets use the same character/runtime contracts.
- Runtime capability settings and graphics quality settings are independent.
- External protocols expose application concepts, not backend object handles.
- 2D presentation reuses normalized tracking/expression and existing output/event boundaries; concrete 2D SDK types remain in optional adapter assemblies.
