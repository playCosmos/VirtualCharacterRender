# P7 Status

Updated: 2026-10-03

## Active branch

```text
feature/p7-material-shader-package-plugin
```

P7 starts from the preserved P6 source checkpoint:

```text
checkpoint/p6-source-implementation
32be14d51354d94a6de26b1966b22077875f1593
```

P0-P6 runtime/device evidence remains deferred where previously documented. A source checkpoint is not a validation PASS.

## Source implementation scope

P7 now provides a declarative material/shader package boundary without enabling arbitrary executable plugins.

### Shader package manifest and resources

`ShaderPackageManifest` and its validator cover:

- package id/version
- Unity and URP compatibility metadata
- Windows/macOS precompiled shader bundles
- declared shader ids
- optional validated material preset
- declared textures and preview images
- canonical relative resource paths
- duplicate resource/id rejection
- traversal and unsafe-path rejection
- raw shader source rejection
- executable/script extension rejection

The runtime loader also preflights the actual package inventory with file-count, total-size, and per-resource size limits. Undeclared executable/source files reject the package even when they are not referenced by the manifest.

### Transactional load and reload

`RuntimeShaderPackageLoader`:

- stages and validates resources before commit
- validates the loaded bundle shader set against declared shader ids
- registers textures only after successful staging
- records underlying shader/texture registry values before shadowing them
- restores prior registry values when the active package unloads
- supports explicit active-package reload
- retains the previous active package when reload fails
- restores registry snapshots when a load transaction fails
- exposes package load attempts/success/failure/timing diagnostics

Automatic file watching is not introduced; reload remains an explicit operation so idle packages do not create recurring filesystem polling cost.

### Declarative capability registration

P7 adds `DeclarativeCapabilityCatalog` in the capabilities layer.

This catalog is intentionally separate from the executable `CapabilityRegistry`:

- it stores provider id/version/capability metadata only
- it has no factory, callback, reflection, assembly loading, file execution, or network execution path
- one owner can atomically replace its registration
- provider-id ownership conflicts are rejected
- registrations can be snapshotted/restored for transaction rollback
- unloading a package removes only that package owner's registration

A successfully loaded shader package advertises `render.custom-shader`. Failed reload retains the previous registration; unload removes it.

### Executable plugin boundary

ADR-0015 remains Deferred.

P7 does **not** load third-party DLLs, scripts, native libraries, reflection entry points, or other executable plugin code. Tracking and protocol integrations remain compiled adapters/protocol boundaries until a concrete use case justifies reopening the executable-plugin security ADR.

## Source-free validation

Interactive:

```text
VCR > P7 > Validate Shader Package Runtime
```

Batch:

```text
tools/validate-p7-source-free.ps1
tools/validate-p7-source-free.sh
```

The P7 suite runs P0-P6 checks first and then covers:

- valid/invalid manifest rules
- path traversal, executable, script, and raw-shader-source rejection
- platform bundle routing
- declarative capability catalog ownership/replace/rollback/unregister behavior
- temporary platform AssetBundle creation on supported Editor hosts
- package shader and texture registration
- material preset exposure
- declarative `render.custom-shader` registration on successful load
- failed hot-reload asset and capability rollback
- unload restoration of underlying shader/texture values
- unload removal of package capability registration
- undeclared executable-file rejection
- load diagnostics

These validation paths are implemented but have not been executed in this environment because a Unity Editor/runtime is not available here.

## Deferred P7 evidence

- execute the P0-P7 Unity source-free batch suite
- validate real Windows and macOS package bundles built by the production content pipeline
- measure large-package load/reload memory and frame impact
- validate real package material presets/textures against VRM/MToon scenes
- tune size limits only from measured package evidence
- reopen ADR-0015 only for a concrete executable-plugin requirement

The P7 declarative package architecture is complete enough for a source checkpoint, but deferred evidence is not marked PASS.
