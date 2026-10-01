# Unity

Unity with URP is the accepted primary renderer/backend for the initial 3D product.

The exact Unity LTS, URP, and UniVRM versions are not pinned until P0 compatibility validation is complete.

The production Unity project should be scaffolded only after:

- target Unity LTS candidates are identified
- compatible URP and UniVRM combinations are checked
- Windows and macOS build targets are included from the start
- the project baseline records the selected package versions

Planned module areas:

```text
VCR/
├─ Runtime/Core
├─ Runtime/Character
├─ Runtime/Rendering
├─ Runtime/Materials
├─ Runtime/Shaders
├─ Runtime/Tracking
├─ Runtime/Motion
├─ Runtime/Expression
├─ Runtime/Environment
├─ Runtime/Events
├─ Runtime/Protocols
├─ Runtime/Diagnostics
└─ Runtime/Output
```

Unity-specific objects remain inside Unity/backend modules and must not become public domain/runtime contracts.
