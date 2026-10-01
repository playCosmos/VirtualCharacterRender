# Platform Support

## Supported desktop targets

VirtualCharacterRender targets both:

- Windows
- macOS

Neither platform is a secondary port. Platform-specific features are implemented behind shared output/input abstractions.

## Hardware baseline

### macOS

- Apple Silicon only for the current supported baseline
- M1 or newer
- M1 is the minimum performance-validation reference

Intel Mac support is not part of the current baseline.

### Windows

- minimum hardware: TBD
- recommended hardware: TBD

Windows requirements are intentionally deferred until P0 performance measurements establish realistic CPU/GPU tiers.

## Shared application layer

These systems must remain portable across supported desktop platforms:

- character/runtime state
- VRM model abstraction
- tracking normalization
- motion/expression mixing
- material/shader metadata
- event runtime
- protocol layer
- scene/profile serialization
- capability/profile system

## Platform adapters

Platform-specific implementation is expected for:

- transparent window creation/composition
- click-through/topmost/window behavior
- native camera/device enumeration where required
- GPU/backend-specific shader behavior
- capture/output integrations
- permissions
- application packaging/signing
- OS lifecycle behavior

## Rendering validation

P0 must validate Unity/URP independently on Windows and macOS.

Required on both platforms:

- model load
- MToon/default rendering
- custom material/shader path
- alpha output
- resize/high-DPI behavior
- target frame-rate stability
- OBS-compatible capture path
- failure recovery
- built-in tracking cost

The P0 transparent-window candidate is UniWindowController 0.9.8 behind the project-owned output adapter.

Windows transparent-output validation currently requires the D3D11 BitBlt path because the candidate DWM transparency path does not support D3D12/flip-model composition. This constraint belongs to the P0 output path and may be revisited if a different native output adapter is selected.

macOS validates the same output abstraction through the candidate's native macOS implementation on Apple Silicon M1+.

Platform-specific output transports such as Spout-class Windows paths or Syphon-class macOS paths are optional adapters, not core contracts.

## Architecture rule

No shared domain API may require Win32, DirectX, Metal, Cocoa, or other platform-native object types.

Platform-native handles stay inside platform/backend modules.

## Distribution

Packaging, code signing/notarization, permissions, and architecture targets are validated separately for Windows and macOS before production release.
