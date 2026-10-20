# Plugins and declarative packages

P7 establishes a declarative extension boundary first.

Supported current extension shape:

- precompiled shader/material packages
- validated package manifests/resources
- metadata-only declarative capability registration
- explicit transactional load/unload/reload
- compiled-in tracking/protocol adapters behind normalized runtime contracts

A declarative package may advertise capabilities, but capability metadata does not execute code. `DeclarativeCapabilityCatalog` is intentionally separate from the executable-service `CapabilityRegistry`.

## Security boundary

Third-party executable plugin loading is not supported.

Packages must not include or load:

- managed/native executable libraries
- scripts or command files
- runtime shader source
- reflection entry points
- arbitrary process/network/file callbacks

Shader packages use precompiled platform AssetBundles plus validated JSON/image resources. Unsafe or undeclared executable/source files reject the package before commit.

See `docs/adr/0015-plugin-execution-security-boundary.md`. ADR-0015 remains Deferred until a concrete executable-plugin requirement cannot be served by declarative packages or protocol adapters.

## Required lifecycle for future extension types

Any new supported extension contract must define:

- manifest and format version
- host compatibility metadata
- declared resources and capabilities
- validation before activation
- ownership of runtime resources
- failure containment and rollback
- unload/reload semantics
- diagnostics
- permissions/trust model if executable behavior is ever introduced

Do not add arbitrary in-process plugin execution as a shortcut around these rules.
