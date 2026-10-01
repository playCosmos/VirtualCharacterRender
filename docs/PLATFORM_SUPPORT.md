# Platform Support

## Supported desktop targets

VirtualCharacterRender targets both:

- Windows
- macOS

Neither platform is a secondary port. Platform-specific features are implemented behind shared output/input abstractions.

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

P0 must validate the renderer candidate independently on Windows and macOS.

Required on both platforms:

- model load
- MToon/default rendering
- custom material/shader path
- alpha output
- resize/high-DPI behavior
- target frame-rate stability
- OBS-compatible capture path
- failure recovery

Platform-specific output transports such as Spout-class Windows paths or Syphon-class macOS paths are optional adapters, not core contracts.

## Architecture rule

No shared domain API may require Win32, DirectX, Metal, Cocoa, or other platform-native object types.

Platform-native handles stay inside platform/backend modules.

## Distribution

Packaging, code signing/notarization, permissions, and architecture targets are validated separately for Windows and macOS before production release.
